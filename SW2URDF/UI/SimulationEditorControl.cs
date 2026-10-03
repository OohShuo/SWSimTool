using SW2URDF.Simulation;
using SW2URDF.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace SW2URDF.UI {
 public sealed class SimulationEditorControl:UserControl {
  readonly AttachmentService service;
  readonly SimulationProject draft;
  readonly CollisionPreview preview;
  readonly ComboBox link=new CollisionComboBox();
  readonly Label status=new Label{AutoSize=true,MaximumSize=new Size(350,0),ForeColor=Color.DarkRed};
  readonly CheckBox show=new CheckBox{Text="实时预览附着点 / 坐标系",Checked=true,AutoSize=true};
  readonly Timer timer=new Timer{Interval=180},cadTimer=new Timer{Interval=500};
  readonly HashSet<TextBox> invalid=new HashSet<TextBox>();
  readonly List<Action> refreshers=new List<Action>();
  readonly Dictionary<string,string> jointOwners;
  readonly string configuration;
  string revision,displayedLink;
  Attachment armed;
  bool loading;
  public Action BeginSelection{get;set;}
  public SimulationEditorControl(AttachmentService service,string selectedLink,Dictionary<string,string> joints){
   this.service=service;jointOwners=joints;var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};draft=serializer.Deserialize<SimulationProject>(serializer.Serialize(service.Project));
   preview=new CollisionPreview(service);Dock=DockStyle.Fill;
   configuration=service.Model.ConfigurationManager.ActiveConfiguration.Name;revision=service.CollisionRevision;
   var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);
   var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};top.Controls.Add(new Label{Text="所属 link",AutoSize=true});top.Controls.Add(link);link.Dock=DockStyle.Fill;Controls.Add(top);
   link.Items.AddRange(service.LinkTransforms().Keys.ToArray());link.SelectedItem=link.Items.Contains(selectedLink)?selectedLink:link.Items[0];displayedLink=(string)link.SelectedItem;
   AttachmentTab(tabs);
   Rules<SensorConfig>(tabs,"传感器",draft.sensors,()=>new SensorConfig{name=Unique("sensor"),site=draft.attachments.FirstOrDefault(a=>a.link==Owner&&a.type=="frame")?.name},s=>draft.attachments.Any(a=>a.name==s.site&&a.link==Owner)||!draft.attachments.Any(a=>a.name==s.site),new[]{"imu","tof","camera"},
    "IMU：加速度计 + 陀螺仪；TOF 沿 site +Z 测距；相机朝 -Z。",
    (panel,s)=>{Combo(panel,s,"site","附着坐标系",draft.attachments.Where(a=>a.type=="frame").Select(a=>a.name).ToArray());if(s.type=="camera")Numeric(panel,s,"fovy","垂直视场角 °");else Numeric(panel,s,"cutoff","输出裁剪值（0 不裁剪）");Note(panel,"noise 固定为 0；噪声在仿真采样层添加。");});
   Rules<ActuatorConfig>(tabs,"执行器",draft.actuators,()=>new ActuatorConfig{name=Unique("actuator"),joint=joints.Where(j=>j.Value==Owner).Select(j=>j.Key).FirstOrDefault()},a=>!joints.ContainsKey(a.joint??"")||joints[a.joint]==Owner,new[]{"motor","position","velocity"},
    "joint 单位：转动 rad、移动 m；力矩 N·m、力 N。gear 决定传动与控制量映射。",
    (panel,a)=>{Combo(panel,a,"joint","所属 joint",joints.Where(j=>j.Value==Owner).Select(j=>j.Key).ToArray());Numeric(panel,a,"gear","传动系数 gear");if(a.type!="motor")Numeric(panel,a,"gain",a.type=="position"?"位置增益 kp":"速度增益 kv");Numeric(panel,a,"ctrl_min","控制下限");Numeric(panel,a,"ctrl_max","控制上限");Numeric(panel,a,"force_min","执行器力 / 力矩下限");Numeric(panel,a,"force_max","执行器力 / 力矩上限");});
   Rules<EqualityConfig>(tabs,"闭链约束",draft.equalities,()=>new EqualityConfig{name=Unique("closure")},e=>true,new[]{"connect","weld"},
    "connect 约束两个 site 的位置；weld 约束两个坐标系。两端须属于不同 link。",
    (panel,e)=>{var options=draft.attachments.Where(a=>e.type!="weld"||a.type=="frame").Select(a=>a.name).ToArray();Combo(panel,e,"site1","端点 1",options);Combo(panel,e,"site2","端点 2",options);Note(panel,string.Join("\n",draft.attachments.Select(a=>a.name+" → "+a.link)));});
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};var save=new Button{Text="保存配置到装配",AutoSize=true};footer.Controls.Add(show);footer.Controls.Add(save);footer.Controls.Add(status);Controls.Add(footer);
   save.Click+=(s,e)=>Guard(Save);show.CheckedChanged+=(s,e)=>Schedule();
   link.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;link.SelectedItem=displayedLink;loading=false;status.Text="请先修正无效数字。";return;}displayedLink=Owner;armed=null;Refresh();Schedule();};
   timer.Tick+=(s,e)=>{timer.Stop();Preview();};
   cadTimer.Tick+=(s,e)=>Guard(()=>{if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration){timer.Stop();cadTimer.Stop();preview.Clear();Enabled=false;status.Text="Configuration 已切换，请重新进入。";return;}string next=service.CollisionRevision;if(next!=revision){revision=next;Schedule();}});
   Refresh();cadTimer.Start();Schedule();
  }
  string Owner=>(string)link.SelectedItem;
  string Unique(string prefix)=>prefix+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
  new void Refresh(){foreach(var refresh in refreshers)refresh();}
  void AttachmentTab(TabControl tabs){
   var page=new CollisionTabPage("附着点");tabs.TabPages.Add(page);var panel=Layout(page);var list=new ListBox{Height=90};Add(panel,list);
   var buttons=new FlowLayoutPanel{AutoSize=true};var add=new Button{Text="添加"};var remove=new Button{Text="删除"};buttons.Controls.Add(add);buttons.Controls.Add(remove);Add(panel,buttons);
   var name=new TextBox();Field(panel,"名称",name);var type=new CollisionComboBox();type.Items.AddRange(new[]{"point","frame"});Field(panel,"类型",type);
   var pick=new Button{Text="拾取参考",AutoSize=true};Add(panel,pick);var source=new Label{AutoSize=true,MaximumSize=new Size(320,0)};Add(panel,source);
   Note(panel,"point：参考点、顶点、草图点，只记录位置。\nframe：参考坐标系，记录位置与方向。\n均相对所属 link；确认保存允许保留未拾取草稿，导出时严格检查。");
   Attachment current=null;
   Action load=()=>{bool previous=loading;loading=true;current=list.SelectedItem as Attachment;name.Text=current?.name??"";type.SelectedItem=current?.type;source.Text=current?.source_name??"尚未拾取";name.Enabled=type.Enabled=pick.Enabled=current!=null;loading=previous;};
   Action refresh=()=>{loading=true;string selected=current?.name;list.Items.Clear();foreach(var a in draft.attachments.Where(a=>a.link==Owner))list.Items.Add(a);list.SelectedItem=list.Items.Cast<Attachment>().FirstOrDefault(a=>a.name==selected)??list.Items.Cast<Attachment>().FirstOrDefault();load();loading=false;};refreshers.Add(refresh);
   list.SelectedIndexChanged+=(s,e)=>{if(loading)return;armed=null;load();Schedule();};
   name.TextChanged+=(s,e)=>{if(loading||current==null)return;current.name=name.Text.Trim();RefreshListDisplay(list);};
   type.SelectedIndexChanged+=(s,e)=>{if(loading||current==null)return;current.type=(string)type.SelectedItem;current.reference=null;current.source_pid=null;current.source_name=null;source.Text="类型已改变，请重新拾取。";RefreshListDisplay(list);Schedule();};
   add.Click+=(s,e)=>{current=new Attachment{name=Unique("site"),link=Owner,type="point"};draft.attachments.Add(current);refresh();};
   remove.Click+=(s,e)=>{if(current==null)return;draft.attachments.Remove(current);current=null;armed=null;refresh();Schedule();};
   pick.Click+=(s,e)=>{armed=current;status.Text=current.type=="frame"?"请选择参考坐标系。":"请选择参考点、顶点或草图点。";BeginSelection?.Invoke();};
  }
  void Rules<T>(TabControl tabs,string title,List<T> items,Func<T> create,Func<T,bool> filter,string[] kinds,string hint,Action<TableLayoutPanel,T> fields)where T:class{
   var page=new CollisionTabPage(title);tabs.TabPages.Add(page);var panel=Layout(page);Note(panel,hint);var list=new ListBox{Height=80};Add(panel,list);
   var buttons=new FlowLayoutPanel{AutoSize=true};var add=new Button{Text="添加"};var remove=new Button{Text="删除"};buttons.Controls.Add(add);buttons.Controls.Add(remove);Add(panel,buttons);var details=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(panel,details);T current=null;
   Action load=null;load=()=>{foreach(var box in Descendants(details).OfType<TextBox>())invalid.Remove(box);foreach(Control child in details.Controls.Cast<Control>().ToArray())child.Dispose();details.Controls.Clear();if(current==null)return;loading=true;var p=Layout(details);StringField(p,current,"name","名称",()=>RefreshListDisplay(list));var type=Combo(p,current,"type","类型",kinds);fields(p,current);loading=false;
    type.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;type.SelectedItem=typeof(T).GetProperty("type").GetValue(current);loading=false;status.Text="请先修正无效数字。";return;}typeof(T).GetProperty("type").SetValue(current,type.SelectedItem);RefreshListDisplay(list);load();};};
   Action refresh=()=>{loading=true;list.Items.Clear();foreach(var item in items.Where(filter))list.Items.Add(item);if(current!=null&&list.Items.Contains(current))list.SelectedItem=current;else list.SelectedIndex=list.Items.Count>0?0:-1;current=list.SelectedItem as T;load();loading=false;};refreshers.Add(refresh);
   list.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;list.SelectedItem=current;loading=false;status.Text="请先修正无效数字。";return;}current=list.SelectedItem as T;load();};
   page.Enter+=(s,e)=>{if(invalid.Count==0)refresh();};
   add.Click+=(s,e)=>{if(invalid.Count>0){status.Text="请先修正无效数字。";return;}current=create();items.Add(current);refresh();};
   remove.Click+=(s,e)=>{if(current==null)return;foreach(var box in Descendants(details).OfType<TextBox>())invalid.Remove(box);items.Remove(current);current=null;refresh();};
  }
  void RefreshListDisplay(ListBox list){bool previous=loading;loading=true;int selected=list.SelectedIndex;for(int i=0;i<list.Items.Count;i++)list.Items[i]=list.Items[i];list.SelectedIndex=selected;loading=previous;}
  static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
  static TableLayoutPanel Layout(Control parent){var panel=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(4)};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));parent.Controls.Add(panel);return panel;}
  static void Add(TableLayoutPanel panel,Control c){c.Dock=DockStyle.Top;c.Margin=new Padding(0,2,0,2);panel.Controls.Add(c);}
  static void Field(TableLayoutPanel panel,string text,Control c){Add(panel,new Label{Text=text,AutoSize=true});Add(panel,c);}
  static void Note(TableLayoutPanel panel,string text){Add(panel,new Label{Text=text,AutoSize=true,MaximumSize=new Size(320,0)});}
  void StringField(TableLayoutPanel p,object item,string key,string label,Action changed=null){var property=item.GetType().GetProperty(key);var box=new TextBox{Text=(string)property.GetValue(item)};Field(p,label,box);box.TextChanged+=(s,e)=>{if(!loading){property.SetValue(item,box.Text.Trim());changed?.Invoke();}};}
  ComboBox Combo(TableLayoutPanel p,object item,string key,string label,string[] choices){var property=item.GetType().GetProperty(key);var box=new CollisionComboBox();box.Items.AddRange(choices);string value=(string)property.GetValue(item);if(value!=null&&!box.Items.Contains(value))box.Items.Add(value);box.SelectedItem=value;Field(p,label,box);if(key!="type")box.SelectedIndexChanged+=(s,e)=>{if(!loading)property.SetValue(item,box.SelectedItem);};return box;}
  void Numeric(TableLayoutPanel p,object item,string key,string label){var property=item.GetType().GetProperty(key);var box=new TextBox{Text=((double)property.GetValue(item)).ToString("G12",CultureInfo.InvariantCulture)};Field(p,label,box);box.TextChanged+=(s,e)=>{double value;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)){property.SetValue(item,value);invalid.Remove(box);box.BackColor=SystemColors.Window;}else{invalid.Add(box);box.BackColor=Color.MistyRose;status.Text="请输入有限数字："+label;}};}
  public void CaptureSelected(){if(armed==null)return;Guard(()=>{var captured=service.CaptureSelectedAttachment(armed.link,armed.name,armed.type);armed.reference=captured.reference;armed.source_name=captured.source_name;armed=null;Refresh();Schedule();});}
  void Schedule(){timer.Stop();timer.Start();}
  void Preview(){
   if(!show.Checked){preview.Clear();return;}var geometries=new List<CollisionGeometry>();string error=null;
   foreach(var a in draft.attachments)try{
    if(a.reference==null&&string.IsNullOrEmpty(a.source_pid)){error=a.name+"：尚未拾取参考。";continue;}var pose=service.AttachmentPose(a);geometries.Add(new CollisionGeometry{id="site:"+a.name,name=a.name,link=a.link,type="sphere",size=new[]{.003},xyz=MathOps.GetXYZ(pose),rpy=new double[3]});
    if(a.type=="frame")for(int axis=0;axis<3;axis++){var xyz=new double[3];xyz[axis]=.01;var rpy=axis==0?new[]{0.0,Math.PI/2,0}:axis==1?new[]{-Math.PI/2,0.0,0}:new double[3];var frame=pose*MathOps.GetTransformation(xyz,rpy);geometries.Add(new CollisionGeometry{id="site:"+a.name+":"+axis,name=a.name+":"+axis,link=a.link,type="cylinder",size=new[]{.0006,.02},xyz=MathOps.GetXYZ(frame),rpy=MathOps.GetRPY(frame)});}
   }catch(Exception e){error=a.name+"："+e.Message;}
   try{if(geometries.Count==0)preview.Clear();else preview.Show(geometries,null);}catch(Exception e){error=e.Message;}status.Text=error??"附着点预览已更新。";
  }
  void Guard(Action action){try{action();}catch(Exception e){status.Text=e.Message;}}
  public void Save(){
   if(invalid.Count>0)throw new InvalidOperationException("请修正无效数字。");if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("Configuration 已切换，请重新进入。");
   foreach(IEnumerable values in new IEnumerable[]{draft.attachments,draft.sensors,draft.actuators,draft.equalities}){var names=new HashSet<string>();foreach(object item in values){string name=(string)item.GetType().GetProperty("name").GetValue(item);if(string.IsNullOrWhiteSpace(name)||!names.Add(name))throw new InvalidOperationException("名称不能为空或重复："+name);}}
   var previous=service.Project;service.Project=draft;try{service.Save();}catch{service.Project=previous;throw;}status.Text="配置已写入装配；请保存 .sldasm。未完成参考在导出时检查。";
  }
  protected override void Dispose(bool disposing){if(disposing){timer.Dispose();cadTimer.Dispose();preview.Dispose();}base.Dispose(disposing);}
 }
}
