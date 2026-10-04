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
  Attachment armed;Action<Attachment> completePick;readonly bool constraintsOnly;EqualityConfig selectedEquality;
  bool loading;
  public Action BeginSelection{get;set;}
  public SimulationEditorControl(AttachmentService service,string selectedLink,Dictionary<string,string> joints,bool constraintsOnly=false){
   this.service=service;this.constraintsOnly=constraintsOnly;jointOwners=joints;var serializer=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};draft=serializer.Deserialize<SimulationProject>(serializer.Serialize(service.Project));
   preview=new CollisionPreview(service);Dock=DockStyle.Fill;
   configuration=service.Model.ConfigurationManager.ActiveConfiguration.Name;revision=service.CollisionRevision;
   var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);tabs.SelectedIndexChanged+=(s,e)=>{armed=null;completePick=null;};
   var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};top.Controls.Add(new Label{Text="所属 link",AutoSize=true});top.Controls.Add(link);link.Dock=DockStyle.Fill;Controls.Add(top);
   link.Items.AddRange(service.LinkTransforms().Keys.ToArray());link.SelectedItem=link.Items.Contains(selectedLink)?selectedLink:link.Items[0];displayedLink=(string)link.SelectedItem;
   if(!constraintsOnly){AttachmentTab(tabs);
   Rules<SensorConfig>(tabs,"传感器",draft.sensors,()=>new SensorConfig{name=Unique("sensor"),site=draft.attachments.FirstOrDefault(a=>a.link==Owner&&a.type=="frame")?.name},s=>draft.attachments.Any(a=>a.name==s.site&&a.link==Owner)||!draft.attachments.Any(a=>a.name==s.site),new[]{"imu","tof","camera"},
    "IMU：加速度计 + 陀螺仪；TOF 沿 site +Z 测距；相机朝 -Z。",
    (panel,s)=>{Combo(panel,s,"site","附着坐标系",draft.attachments.Where(a=>a.type=="frame"&&a.link==Owner).Select(a=>a.name).ToArray());PickFrame(panel,"拾取坐标系",Owner,"frame",a=>s.site=a.name);if(s.type=="camera")Numeric(panel,s,"fovy","垂直视场角 °");else Numeric(panel,s,"cutoff","输出裁剪值（0 不裁剪）");if(s.type!="camera")Note(panel,"noise 固定为 0；噪声在仿真采样层添加。");});
   Rules<ActuatorConfig>(tabs,"执行器",draft.actuators,()=>new ActuatorConfig{name=Unique("actuator"),joint=joints.Where(j=>j.Value==Owner).Select(j=>j.Key).FirstOrDefault()},a=>!joints.ContainsKey(a.joint??"")||joints[a.joint]==Owner,new[]{"motor","position","velocity"},
    "joint 单位：转动 rad、移动 m；力矩 N·m、力 N。gear 决定传动与控制量映射。",
    (panel,a)=>{var target=Combo(panel,a,"joint","所属 joint",joints.Where(j=>j.Value==Owner).Select(j=>j.Key).ToArray());Numeric(panel,a,"gear","传动系数 gear");if(a.type!="motor")Numeric(panel,a,"gain",a.type=="position"?"位置增益 kp":"速度增益 kv");Numeric(panel,a,"ctrl_min","控制下限");Numeric(panel,a,"ctrl_max","控制上限");Numeric(panel,a,"force_min","执行器力 / 力矩下限");Numeric(panel,a,"force_max","执行器力 / 力矩上限");JointForceFields(panel,a,target);});
   SolverTab(tabs);
   }else{show.Text="实时预览约束端点 / 坐标系";ConstraintTab(tabs);}
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};var save=new Button{Text="保存配置到装配",AutoSize=true};footer.Controls.Add(show);footer.Controls.Add(save);footer.Controls.Add(status);Controls.Add(footer);
   save.Click+=(s,e)=>Guard(Save);show.CheckedChanged+=(s,e)=>Schedule();
   link.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;link.SelectedItem=displayedLink;loading=false;status.Text="请先修正无效数字。";return;}displayedLink=Owner;armed=null;completePick=null;Refresh();Schedule();};
   timer.Tick+=(s,e)=>{timer.Stop();Preview();};
   cadTimer.Tick+=(s,e)=>Guard(()=>{if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration){timer.Stop();cadTimer.Stop();preview.Clear();Enabled=false;status.Text="Configuration 已切换，请重新进入。";return;}string next=service.CollisionRevision;if(next!=revision){revision=next;Schedule();}});
   Refresh();cadTimer.Start();Schedule();
  }
  void PickFrame(TableLayoutPanel panel,string title,string owner,string type,Action<Attachment> complete){PickFrame(panel,title,()=>owner,type,complete);}
  void PickFrame(TableLayoutPanel panel,string title,Func<string> owner,string type,Action<Attachment> complete){var button=new Button{Text=title,AutoSize=true};Add(panel,button);button.Click+=(s,e)=>{if(invalid.Count>0){status.Text="请先修正无效数字。";return;}armed=new Attachment{name=Unique("site"),link=owner(),type=type};completePick=complete;status.Text=type=="frame"?"请选择坐标系；成功后自动创建或复用 frame。":"请选择参考点、顶点或草图点。";BeginSelection?.Invoke();};}
  void ArrayFields(TableLayoutPanel p,string label,double[] values){for(int i=0;i<values.Length;i++){int index=i;var box=new TextBox{Text=values[i].ToString("G12",CultureInfo.InvariantCulture)};Field(p,label+" "+(values.Length==3?"XYZ"[i].ToString():"a"+i),box);Action check=()=>{double n;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!double.IsNaN(n)&&!double.IsInfinity(n)){values[index]=n;invalid.Remove(box);box.BackColor=SystemColors.Window;Schedule();}else{invalid.Add(box);box.BackColor=Color.MistyRose;}};box.Tag=check;box.TextChanged+=(s,e)=>check();box.Disposed+=(s,e)=>invalid.Remove(box);}}
  void ConstraintTab(TabControl tabs){
   Rules<EqualityConfig>(tabs,"约束",draft.equalities,()=>new EqualityConfig{name=Unique("constraint")},e=>true,new[]{"connect","weld","joint"},"connect：位置重合；weld：位置与方向重合；joint：关节坐标耦合。各条目独立保存。",(p,e)=>{
    selectedEquality=e;var active=new CheckBox{Text="启用约束",AutoSize=true,Checked=e.active};Add(p,active);active.CheckedChanged+=(s,args)=>{e.active=active.Checked;Schedule();};
    if(e.type=="joint"){
     Combo(p,e,"joint1","关节一",jointOwners.Keys.ToArray());Combo(p,e,"joint2","关节二（留空：固定到参考值 + a0）",new[]{""}.Concat(jointOwners.Keys).ToArray());
     Note(p,"y-y0 = a0 + a1(x-x0) + … + a4(x-x0)^4。关节二留空时仅 a0 生效。转动 rad、移动 m。");ArrayFields(p,"多项式系数",e.polycoef);
    }else{
     var binding=Combo(p,e,"binding","连接对象",new[]{"site","body"});var host=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(p,host);
     Action render=()=>{foreach(Control c in host.Controls.Cast<Control>().ToArray())c.Dispose();var fields=Layout(host);if(e.binding=="site"){
      var options=draft.attachments.Where(a=>e.type!="weld"||a.type=="frame").Select(a=>a.name).ToArray();
      foreach(bool first in new[]{true,false}){Combo(fields,e,first?"site1":"site2",first?"端点一 site":"端点二 site",options);var owner=new CollisionComboBox();owner.Items.AddRange(link.Items.Cast<string>().ToArray());var existing=draft.attachments.FirstOrDefault(a=>a.name==(first?e.site1:e.site2));owner.SelectedItem=existing?.link??Owner;owner.SelectedIndexChanged+=(sender,args)=>{armed=null;completePick=null;};Field(fields,first?"端点一所属 link（拾取用）":"端点二所属 link（拾取用）",owner);PickFrame(fields,first?"拾取端点一":"拾取端点二",()=>owner.SelectedItem as string,e.type=="weld"?"frame":"point",a=>{if(first)e.site1=a.name;else e.site2=a.name;});}
      Note(fields,e.type=="weld"?"拾取两个坐标系；仿真时使两坐标系重合，方向由 site 定义。":"拾取两个点；两端须属于不同 link。");
     }else{
      Combo(fields,e,"body1","body 一 / link",link.Items.Cast<string>().ToArray());Combo(fields,e,"body2","body 二 / link（留空：世界）",new[]{""}.Concat(link.Items.Cast<string>()).ToArray());
      if(e.type=="connect"){Note(fields,"连接点相对 body 一，单位 m；初始时自动对应 body 二上的同一世界点。");ArrayFields(fields,"连接点 m",e.anchor);}
      else{var pose=Combo(fields,e,"pose_mode","相对姿态",new[]{"inherit","custom"});var values=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(fields,values);var vp=Layout(values);ArrayFields(vp,"body 二相对 body 一的位置 m",e.position);ArrayFields(vp,"RPY rad（X / Y / Z）",e.orientation);values.Enabled=values.Visible=e.pose_mode=="custom";pose.SelectedIndexChanged+=(sender,args)=>JointEditorControl.PreserveScroll(values,()=>{values.Enabled=values.Visible=e.pose_mode=="custom";foreach(var b in Descendants(values).OfType<TextBox>()){if(!values.Enabled)invalid.Remove(b);else (b.Tag as Action)?.Invoke();}Schedule();});}
     }};
     binding.SelectedIndexChanged+=(sender,args)=>{if(!loading)JointEditorControl.PreserveScroll(host,render);Schedule();};render();
     if(e.type=="weld")Numeric(p,e,"torquescale","旋转约束权重 torquescale m（≥ 0）");
    }
    Add(p,new SolverParametersControl(e.solver,draft.solver?.equality??new ConstraintSettings(),false,v=>{e.solver=v;Schedule();},invalid,true,null,e.type=="joint"?"关节一单位（转动 rad / 移动 m）":"m"));Schedule();
   });
  }
  void JointForceFields(TableLayoutPanel panel,ActuatorConfig actuator,ComboBox target){
   var host=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(panel,host);
   Action rebuild=null;rebuild=()=>{foreach(Control c in host.Controls.Cast<Control>().ToArray())c.Dispose();if(string.IsNullOrWhiteSpace(actuator.joint))return;var p=Layout(host);var current=draft.joint_force_limits.FirstOrDefault(f=>f.joint==actuator.joint);var enable=new CheckBox{Text="高级：限制所属关节的合计驱动力",AutoSize=true,Checked=current!=null};Add(p,enable);
    enable.CheckedChanged+=(s,e)=>JointEditorControl.PreserveScroll(host,()=>{if(enable.Checked){if(current==null){current=new JointForceLimit{joint=actuator.joint};draft.joint_force_limits.Add(current);}}else if(current!=null){draft.joint_force_limits.Remove(current);current=null;}rebuild();});
    if(current!=null){Note(p,"按 joint 保存；同关节所有执行器共享。单位：转动 N·m / 移动 N，作用在 gear 映射之后。");ForceNumber(p,current,true);ForceNumber(p,current,false);}
   };target.SelectedIndexChanged+=(s,e)=>{if(!loading)rebuild();};rebuild();
  }
  void ForceNumber(TableLayoutPanel p,JointForceLimit limit,bool lower){var box=new TextBox{Text=(lower?limit.lower:limit.upper)?.ToString("G12",CultureInfo.InvariantCulture)??""};Field(p,lower?"关节合计驱动力下限":"关节合计驱动力上限",box);if(!(lower?limit.lower:limit.upper).HasValue)invalid.Add(box);box.Disposed+=(s,e)=>invalid.Remove(box);box.TextChanged+=(s,e)=>{double value;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)){if(lower)limit.lower=value;else limit.upper=value;invalid.Remove(box);box.BackColor=SystemColors.Window;}else{invalid.Add(box);box.BackColor=Color.MistyRose;}};}
  void SolverTab(TabControl tabs){
   var page=new CollisionTabPage("求解设置");tabs.TabPages.Add(page);var panel=Layout(page);var enable=new CheckBox{Text="启用全局求解设置",AutoSize=true,Checked=draft.solver?.enabled??false};Add(panel,enable);
   var preset=new Button{Text="应用刚性机器人预设",AutoSize=true};Add(panel,preset);var fields=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(panel,fields);
   Note(panel,"Newton + elliptic；保留 refsafe。margin 默认 1 mm，可在碰撞对中改为 2 mm。Noslip 默认关闭。局部覆盖优先；未启用且没有局部覆盖时保留原行为。");
   Action rebuild=()=>{foreach(Control c in fields.Controls.Cast<Control>().ToArray())c.Dispose();var settings=draft.solver=draft.solver??new SolverSettings();var p=Layout(fields);fields.Enabled=fields.Visible=settings.enabled;
    SolverParametersControl.AddNumber(p,settings,"timestep","步长 s",invalid);SolverParametersControl.AddNumber(p,settings,"iterations","最大迭代数",invalid);SolverParametersControl.AddNumber(p,settings,"tolerance","收敛容差",invalid);SolverParametersControl.AddNumber(p,settings,"impratio","摩擦阻抗比",invalid);SolverParametersControl.AddNumber(p,settings,"noslip_iterations","防慢滑后处理迭代数",invalid);
    Note(p,"默认闭链参数");Add(p,new SolverParametersControl(settings.equality??(settings.equality=new ConstraintSettings()),new ConstraintSettings(),false,v=>settings.equality=v,invalid,false));
    Note(p,"默认接触参数（环境接触及未覆盖碰撞对）");Add(p,new SolverParametersControl(settings.contact??(settings.contact=ConstraintSettings.Contact()),ConstraintSettings.Contact(),true,v=>settings.contact=v,invalid,false));};
   enable.CheckedChanged+=(s,e)=>{draft.solver=draft.solver??new SolverSettings();draft.solver.enabled=enable.Checked;JointEditorControl.PreserveScroll(fields,()=>{fields.Enabled=fields.Visible=enable.Checked;foreach(var box in SolverParametersControl.Children(fields).OfType<TextBox>()){double n;if(!enable.Checked)invalid.Remove(box);else if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)||double.IsNaN(n)||double.IsInfinity(n))invalid.Add(box);}});};
   preset.Click+=(s,e)=>{draft.solver=new SolverSettings{enabled=true};enable.Checked=true;rebuild();status.Text="已应用预设；已有局部覆盖保留。保存配置并重新导出后生效。";};rebuild();
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
   pick.Click+=(s,e)=>{completePick=null;armed=current;status.Text=current.type=="frame"?"请选择参考坐标系。":"请选择参考点、顶点或草图点。";BeginSelection?.Invoke();};
  }
  void Rules<T>(TabControl tabs,string title,List<T> items,Func<T> create,Func<T,bool> filter,string[] kinds,string hint,Action<TableLayoutPanel,T> fields)where T:class{
   var page=new CollisionTabPage(title);tabs.TabPages.Add(page);var panel=Layout(page);Note(panel,hint);var list=new ListBox{Height=80};Add(panel,list);
   var buttons=new FlowLayoutPanel{AutoSize=true};var add=new Button{Text="添加"};var remove=new Button{Text="删除"};buttons.Controls.Add(add);buttons.Controls.Add(remove);Add(panel,buttons);var details=new Panel{AutoSize=true,Dock=DockStyle.Top};Add(panel,details);T current=null;
   Action load=null;load=()=>{armed=null;completePick=null;foreach(var box in Descendants(details).OfType<TextBox>())invalid.Remove(box);foreach(Control child in details.Controls.Cast<Control>().ToArray())child.Dispose();details.Controls.Clear();if(current==null){if(constraintsOnly){selectedEquality=null;Schedule();}return;}loading=true;var p=Layout(details);StringField(p,current,"name","名称",()=>RefreshListDisplay(list));var type=Combo(p,current,"type","类型",kinds);fields(p,current);loading=false;
    type.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;type.SelectedItem=typeof(T).GetProperty("type").GetValue(current);loading=false;status.Text="请先修正无效数字。";return;}typeof(T).GetProperty("type").SetValue(current,type.SelectedItem);RefreshListDisplay(list);JointEditorControl.PreserveScroll(details,load);Schedule();};};
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
  ComboBox Combo(TableLayoutPanel p,object item,string key,string label,string[] choices){var property=item.GetType().GetProperty(key);var box=new CollisionComboBox();box.Items.AddRange(choices);string value=(string)property.GetValue(item);if(value!=null&&!box.Items.Contains(value))box.Items.Add(value);box.SelectedItem=value;Field(p,label,box);if(key!="type")box.SelectedIndexChanged+=(s,e)=>{if(!loading){if(invalid.Count>0){loading=true;box.SelectedItem=property.GetValue(item);loading=false;status.Text="请先修正无效数字。";return;}property.SetValue(item,box.SelectedItem);Schedule();}};return box;}
  void Numeric(TableLayoutPanel p,object item,string key,string label){var property=item.GetType().GetProperty(key);var box=new TextBox{Text=((double)property.GetValue(item)).ToString("G12",CultureInfo.InvariantCulture)};Field(p,label,box);box.TextChanged+=(s,e)=>{double value;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)){property.SetValue(item,value);invalid.Remove(box);box.BackColor=SystemColors.Window;}else{invalid.Add(box);box.BackColor=Color.MistyRose;status.Text="请输入有限数字："+label;}};}
  public void CaptureSelected(){if(armed==null)return;Guard(()=>{var captured=service.CaptureSelectedAttachment(armed.link,armed.name,armed.type);AcceptCapturedAttachment(captured);armed=null;completePick=null;Refresh();Schedule();});}
  void AcceptCapturedAttachment(Attachment captured){if(completePick!=null){var existing=draft.attachments.FirstOrDefault(a=>a.link==captured.link&&a.type==captured.type&&a.reference!=null&&a.reference.pid==captured.reference.pid&&a.reference.component_pid==captured.reference.component_pid);if(existing==null){draft.attachments.Add(captured);existing=captured;}completePick(existing);}else{armed.reference=captured.reference;armed.source_name=captured.source_name;}}
  void Schedule(){timer.Stop();timer.Start();}
  void Preview(){
   if(!show.Checked){preview.Clear();return;}var geometries=new List<CollisionGeometry>();string error=null;
   var visible=draft.attachments.AsEnumerable();if(constraintsOnly)visible=selectedEquality==null||selectedEquality.type=="joint"||selectedEquality.binding=="body"?Enumerable.Empty<Attachment>():visible.Where(a=>a.name==selectedEquality.site1||a.name==selectedEquality.site2);
   foreach(var a in visible)try{
    if(a.reference==null&&string.IsNullOrEmpty(a.source_pid)){error=a.name+"：尚未拾取参考。";continue;}var pose=service.AttachmentPose(a);geometries.Add(new CollisionGeometry{id="site:"+a.name,name=a.name,link=a.link,type="sphere",size=new[]{.003},xyz=MathOps.GetXYZ(pose),rpy=new double[3]});
    if(a.type=="frame")for(int axis=0;axis<3;axis++){var xyz=new double[3];xyz[axis]=.01;var rpy=axis==0?new[]{0.0,Math.PI/2,0}:axis==1?new[]{-Math.PI/2,0.0,0}:new double[3];var frame=pose*MathOps.GetTransformation(xyz,rpy);geometries.Add(new CollisionGeometry{id="site:"+a.name+":"+axis,name=a.name+":"+axis,link=a.link,type="cylinder",size=new[]{.0006,.02},xyz=MathOps.GetXYZ(frame),rpy=MathOps.GetRPY(frame)});}
   }catch(Exception e){error=a.name+"："+e.Message;}
   if(constraintsOnly&&selectedEquality!=null&&selectedEquality.type!="joint"&&selectedEquality.binding=="site"){var a=draft.attachments.FirstOrDefault(x=>x.name==selectedEquality.site1);var b=draft.attachments.FirstOrDefault(x=>x.name==selectedEquality.site2);if(a!=null&&b!=null)try{geometries.AddRange(service.ConstraintPreview(a,b,selectedEquality.name));}catch(Exception ex){error=ex.Message;}}
   if(constraintsOnly&&selectedEquality!=null&&selectedEquality.type!="joint"&&selectedEquality.binding=="body")try{geometries.AddRange(service.ConstraintBodyPreview(selectedEquality));}catch(Exception ex){error=ex.Message;}
   try{if(geometries.Count==0)preview.Clear();else preview.Show(geometries,null);}catch(Exception e){error=e.Message;}status.Text=error??(constraintsOnly?"约束预览已更新。":"附着点预览已更新。");
  }
  void Guard(Action action){try{action();}catch(Exception e){status.Text=e.Message;}}
  public void Save(){
   if(invalid.Count>0)throw new InvalidOperationException("请修正无效数字。");if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("Configuration 已切换，请重新进入。");
   foreach(IEnumerable values in new IEnumerable[]{draft.attachments,draft.sensors,draft.actuators,draft.equalities}){var names=new HashSet<string>();foreach(object item in values){string name=(string)item.GetType().GetProperty("name").GetValue(item);if(string.IsNullOrWhiteSpace(name)||!names.Add(name))throw new InvalidOperationException("名称不能为空或重复："+name);}}
   draft.ValidateSolver();var previous=service.Project;service.Project=draft;try{service.Save();}catch{service.Project=previous;throw;}status.Text="配置已写入装配；请保存 .sldasm。未完成参考在导出时检查。";
  }
  protected override void Dispose(bool disposing){if(disposing){timer.Dispose();cadTimer.Dispose();preview.Dispose();}base.Dispose(disposing);}
 }
}
