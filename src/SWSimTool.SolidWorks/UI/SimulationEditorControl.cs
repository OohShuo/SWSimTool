using SWSimTool.Simulation;
using SWSimTool.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace SWSimTool.UI {
 public sealed class SimulationEditorControl:UserControl {
  bool ownsPage;readonly AttachmentService service;
  readonly SimulationProject draft;
  readonly CollisionPreview preview;
  readonly ComboBox link=new CollisionComboBox();
  readonly Label status=new Label{AutoSize=true,MaximumSize=new Size(350,0),ForeColor=Color.DarkRed};
  readonly CheckBox show=new CheckBox{Text="实时预览附着点 / 坐标系",Checked=true,AutoSize=true};
  readonly Timer timer=new Timer{Interval=180},cadTimer=new Timer{Interval=500};
  readonly HashSet<TextBox> invalid=new HashSet<TextBox>();
  readonly List<Action> refreshers=new List<Action>();
  readonly List<Action> siteRefreshers=new List<Action>(); List<Action> conditions;
  readonly Dictionary<string,string> jointOwners;
  readonly string configuration;
  string revision,displayedLink,previewKey;
  Attachment armed;readonly bool constraintsOnly;EqualityConfig selectedEquality;SiteForceConfig selectedForce;
  bool loading;
  public Action BeginSelection{get;set;}
  public SimulationEditorControl(AttachmentService service,string selectedLink,Dictionary<string,string> joints,bool constraintsOnly=false){try{
   this.service=service;service.BeginPage();ownsPage=true;this.constraintsOnly=constraintsOnly;jointOwners=joints;draft=service.Project;
   draft.NormalizeSiteReferences();preview=new CollisionPreview(service);Dock=DockStyle.Fill;
   configuration=service.Model.ConfigurationManager.ActiveConfiguration.Name;revision=service.CollisionRevision;
   var tabs=new TabControl{Dock=DockStyle.Fill};Controls.Add(tabs);tabs.SelectedIndexChanged+=(s,e)=>{armed=null;};
   var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};top.Controls.Add(new Label{Text="所属 link",AutoSize=true});top.Controls.Add(link);link.Dock=DockStyle.Fill;if(!constraintsOnly)Controls.Add(top);
   link.Items.AddRange(service.LinkTransforms().Keys.ToArray());link.SelectedItem=link.Items.Contains(selectedLink)?selectedLink:link.Items[0];displayedLink=(string)link.SelectedItem;
   if(!constraintsOnly){AttachmentTab(tabs);
   Rules<SensorConfig>(tabs,"传感器",draft.sensors,()=>new SensorConfig{name=Unique("sensor"),site=draft.attachments.FirstOrDefault(a=>a.link==Owner&&a.type=="frame")?.name},s=>draft.attachments.Any(a=>a.name==s.site&&a.link==Owner)||!draft.attachments.Any(a=>a.name==s.site),new[]{"imu","tof","camera"},
    "IMU：加速度计 + 陀螺仪；TOF 沿 site +Z 测距；相机朝 -Z。",
    (panel,s)=>{SiteChoice(panel,s,"site","附着坐标系",()=>true);Section(panel,()=>s.type=="camera",p=>Numeric(p,s,"fovy","垂直视场角 °"));Section(panel,()=>s.type!="camera",p=>{Numeric(p,s,"cutoff","输出裁剪值（0 不裁剪）");Note(p,"noise 固定为 0；噪声在仿真采样层添加。");});});
   Rules<ActuatorConfig>(tabs,"执行器",draft.actuators,()=>new ActuatorConfig{name=Unique("actuator"),joint=joints.Where(j=>j.Value==Owner).Select(j=>j.Key).FirstOrDefault()},a=>!joints.ContainsKey(a.joint??"")||joints[a.joint]==Owner,new[]{"motor","position","velocity"},
    "joint 单位：转动 rad、移动 m；力矩 N·m、力 N。gear 决定传动与控制量映射。",
    (panel,a)=>{var target=Combo(panel,a,"joint","所属 joint",joints.Where(j=>j.Value==Owner).Select(j=>j.Key).ToArray());Numeric(panel,a,"gear","传动系数 gear");Section(panel,()=>a.type!="motor",p=>{Numeric(p,a,"gain","增益");var label=p.Controls.OfType<Label>().First();conditions.Add(()=>label.Text=a.type=="position"?"位置增益 kp":"速度增益 kv");});Numeric(panel,a,"ctrl_min","控制下限");Numeric(panel,a,"ctrl_max","控制上限");Numeric(panel,a,"force_min","执行器力 / 力矩下限");Numeric(panel,a,"force_max","执行器力 / 力矩上限");JointForceFields(panel,a,target);});
   SolverTab(tabs);
   ForceTab(tabs);
   tabs.SelectedIndexChanged+=(s,e)=>JointEditorControl.PreserveScroll(this,()=>{top.Enabled=top.Visible=tabs.SelectedTab?.Text!="两点作用力";});
   }else{show.Text="实时预览约束端点 / 坐标系";ConstraintTab(tabs);}
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};var save=new Button{Text="保存配置到装配",AutoSize=true};var refreshCAD=new Button{Text="刷新 CAD 参考",AutoSize=true};refreshCAD.Click+=(s,e)=>{service.InvalidateCADCache();Schedule();};footer.Controls.Add(show);footer.Controls.Add(refreshCAD);footer.Controls.Add(save);footer.Controls.Add(status);Controls.Add(footer);
   save.Click+=(s,e)=>Guard(()=>Save());show.CheckedChanged+=(s,e)=>Schedule();
   link.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;link.SelectedItem=displayedLink;loading=false;status.Text="请先修正无效数字。";return;}displayedLink=Owner;armed=null;Refresh();Schedule();};
   timer.Tick+=(s,e)=>{timer.Stop();Preview();};
   cadTimer.Tick+=(s,e)=>Guard(()=>{if(!CheckSession())return;if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration){timer.Stop();cadTimer.Stop();preview.Clear();Enabled=false;status.Text="Configuration 已切换，请重新进入。";return;}string next=service.CollisionRevision;if(next!=revision){revision=next;Schedule();}});
   service.PageDraft.Flush=FlushDraft;Refresh();cadTimer.Start();Schedule();
  }catch{Dispose();throw;}}
  void Section(TableLayoutPanel parent,Func<bool> visible,Action<TableLayoutPanel> build){var host=Layout(parent);Add(parent,host);build(host);conditions.Add(()=>{bool enabled=visible();host.Visible=host.Enabled=enabled;foreach(var box in Descendants(host).OfType<TextBox>()){if(!enabled)invalid.Remove(box);else (box.Tag as Action)?.Invoke();}});}
  void SiteChoice(TableLayoutPanel p,object item,string key,string title,Func<bool> allowPoint){
   var name=item.GetType().GetProperty(key);var id=item.GetType().GetProperty(key+"_id");var box=new CollisionComboBox();Field(p,title,box);var owner=new Label{AutoSize=true};Add(p,owner);
   Action update=()=>{bool previous=loading;loading=true;draft.NormalizeSiteReferences();box.Items.Clear();var options=draft.attachments.Where(a=>allowPoint()||a.type=="frame").ToArray();box.Items.AddRange(options.Select(a=>a.name).ToArray());var selected=draft.attachments.FirstOrDefault(a=>!string.IsNullOrEmpty((string)id.GetValue(item))?a.id==(string)id.GetValue(item):a.name==(string)name.GetValue(item));string value=(string)name.GetValue(item);if(selected==null&&value!=null)value="（失效）"+value;if(value!=null&&!box.Items.Contains(value))box.Items.Add(value);box.SelectedItem=value;owner.Text=selected==null?"所属 link：未选择 / site 已失效":"所属 link："+selected.link+(options.Contains(selected)?"":"（类型不适用）");loading=previous;};
   siteRefreshers.Add(update);box.Disposed+=(sender,args)=>siteRefreshers.Remove(update);conditions.Add(update);box.SelectedIndexChanged+=(sender,args)=>{if(loading)return;var selected=draft.attachments.FirstOrDefault(a=>a.name==(string)box.SelectedItem);name.SetValue(item,selected?.name);id.SetValue(item,selected?.id);update();Schedule();};update();
  }
  void UpdateSites(){draft.NormalizeSiteReferences();foreach(var action in siteRefreshers.ToArray())action();Schedule();}
  void ArrayFields(TableLayoutPanel p,string label,double[] values){for(int i=0;i<values.Length;i++){int index=i;var box=new TextBox{Text=values[i].ToString("G12",CultureInfo.InvariantCulture)};Field(p,label+" "+(values.Length==3?"XYZ"[i].ToString():"a"+i),box);Action check=()=>{double n;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!double.IsNaN(n)&&!double.IsInfinity(n)){values[index]=n;invalid.Remove(box);box.BackColor=SystemColors.Window;Schedule();}else{invalid.Add(box);box.BackColor=Color.MistyRose;}};box.Tag=check;box.TextChanged+=(s,e)=>check();box.Disposed+=(s,e)=>invalid.Remove(box);}}
  void ForceTab(TabControl tabs){
   Rules<SiteForceConfig>(tabs,"两点作用力",draft.site_forces,()=>new SiteForceConfig{name=Unique("force"),site1=draft.attachments.FirstOrDefault(a=>a.link==Owner)?.name},f=>true,new[]{"pull","push","spring"},"pull 恒定拉力 / push 恒定推力 / spring 双向弹簧。point 和 frame 均可，只使用位置；两端所属 link 自动推断。",(p,f)=>{
    selectedForce=f;var enabled=new CheckBox{Text="启用两点作用力",AutoSize=true,Checked=f.enabled};Add(p,enabled);enabled.CheckedChanged+=(sender,args)=>{f.enabled=enabled.Checked;Schedule();};
    SiteChoice(p,f,"site1","端点一 site",()=>true);SiteChoice(p,f,"site2","端点二 site",()=>true);Section(p,()=>f.type!="spring",fields=>Numeric(fields,f,"magnitude","恒定力大小 N（≥ 0）"));
    Section(p,()=>f.type=="spring",fields=>{Numeric(fields,f,"stiffness","弹簧刚度 N/m（≥ 0）");Numeric(fields,f,"damping","弹簧阻尼 N·s/m（≥ 0）");var updates=conditions;var mode=Combo(fields,f,"length_mode","自然长度：initial 取初始距离 / custom 自定义",new[]{"initial","custom"});Section(fields,()=>f.length_mode=="custom",values=>Numeric(values,f,"rest_length","自然长度 m（≥ 0）"));mode.SelectedIndexChanged+=(sender,args)=>{if(!loading)JointEditorControl.PreserveScroll(p,()=>{foreach(var update in updates)update();});};});
    Note(p,"默认力值、刚度和阻尼为 0。两端须属于不同 link；方向随连线变化，偏心安装产生力矩。spring 可拉可压，取初始距离时初始弹性力为 0。有效条目初始重合会阻止导出，运动中也应避免重合。CAD 预览仅为连线，MJCF 无需回调。");
   });
  }
  void ConstraintTab(TabControl tabs){
   Rules<EqualityConfig>(tabs,"约束",draft.equalities,()=>new EqualityConfig{name=Unique("constraint")},e=>true,new[]{"connect","weld","joint"},"connect：位置重合；weld：位置与方向重合；joint：关节坐标耦合。各条目独立保存。",(p,e)=>{
    selectedEquality=e;var active=new CheckBox{Text="启用约束",AutoSize=true,Checked=e.active};Add(p,active);active.CheckedChanged+=(s,args)=>{e.active=active.Checked;Schedule();};
    Section(p,()=>e.type=="joint",fields=>{foreach(bool first in new[]{true,false}){var selector=Combo(fields,e,first?"joint1":"joint2",first?"关节一":"关节二（留空：固定到参考值 + a0）",first?jointOwners.Keys.ToArray():new[]{""}.Concat(jointOwners.Keys).ToArray());var owner=new Label{AutoSize=true};Add(fields,owner);Action update=()=>{string value=first?e.joint1:e.joint2;owner.Text="所属 link："+(jointOwners.ContainsKey(value??"")?jointOwners[value]:"未选择");};conditions.Add(update);selector.SelectedIndexChanged+=(sender,args)=>update();update();}Note(fields,"y-y0 = a0 + a1(x-x0) + … + a4(x-x0)^4。关节二留空时仅 a0 生效。转动 rad、移动 m。");ArrayFields(fields,"多项式系数",e.polycoef);});
    Section(p,()=>e.type!="joint"&&e.binding=="site",fields=>{SiteChoice(fields,e,"site1","端点一 site",()=>e.type!="weld");SiteChoice(fields,e,"site2","端点二 site",()=>e.type!="weld");Note(fields,"选择已有 site；所属 link 自动推断。weld 需选择两个 frame，connect 可选择 point 或 frame。");});
    Section(p,()=>e.type!="joint"&&e.binding=="body",fields=>{Note(fields,"历史 body 约束："+e.body1+" ↔ "+(e.body2??"世界")+"。保留原参数；转换后请选择已有 site。");var convert=new Button{Text="改用 site 定义",AutoSize=true};Add(fields,convert);convert.Click+=(sender,args)=>{e.binding="site";JointEditorControl.PreserveScroll(p,()=>{foreach(var update in (List<Action>)p.Tag)update();});Schedule();};});
    Section(p,()=>e.type=="weld",fields=>Numeric(fields,e,"torquescale","旋转约束权重 torquescale m（≥ 0）"));
    Add(p,new SolverParametersControl(e.solver,draft.solver?.equality??new ConstraintSettings(),false,v=>{e.solver=v;Schedule();},invalid,true,null,e.type=="joint"?"关节一单位（转动 rad / 移动 m）":"m"));Schedule();
   });
  }
  void JointForceFields(TableLayoutPanel panel,ActuatorConfig actuator,ComboBox target){
   var enable=new CheckBox{Text="高级：限制所属关节的合计驱动力",AutoSize=true};Add(panel,enable);var host=new Panel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top};Add(panel,host);var p=Layout(host);Note(p,"按 joint 保存；同关节执行器共享。转动 N·m / 移动 N，作用在 gear 映射之后。");var lower=new TextBox();var upper=new TextBox();Field(p,"关节合计驱动力下限",lower);Field(p,"关节合计驱动力上限",upper);JointForceLimit current=null;bool updating=false;
   Action check=()=>{if(updating||current==null)return;foreach(var box in new[]{lower,upper}){double value;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)){if(box==lower)current.lower=value;else current.upper=value;invalid.Remove(box);box.BackColor=SystemColors.Window;}else{invalid.Add(box);box.BackColor=Color.MistyRose;}}};
   Action refresh=()=>{updating=true;current=draft.joint_force_limits.FirstOrDefault(f=>f.joint==actuator.joint);enable.Enabled=!string.IsNullOrWhiteSpace(actuator.joint);enable.Checked=current!=null;host.Enabled=host.Visible=current!=null;lower.Text=current?.lower?.ToString("G12",CultureInfo.InvariantCulture)??"";upper.Text=current?.upper?.ToString("G12",CultureInfo.InvariantCulture)??"";invalid.Remove(lower);invalid.Remove(upper);updating=false;check();};
   lower.TextChanged+=(sender,args)=>check();upper.TextChanged+=(sender,args)=>check();lower.Disposed+=(sender,args)=>invalid.Remove(lower);upper.Disposed+=(sender,args)=>invalid.Remove(upper);
   enable.CheckedChanged+=(sender,args)=>{if(updating)return;JointEditorControl.PreserveScroll(host,()=>{if(enable.Checked){current=new JointForceLimit{joint=actuator.joint};draft.joint_force_limits.Add(current);}else if(current!=null)draft.joint_force_limits.Remove(current);refresh();});};target.SelectedIndexChanged+=(sender,args)=>{if(!loading)JointEditorControl.PreserveScroll(host,refresh);};refresh();
  }
  void SolverTab(TabControl tabs){
   var page=new CollisionTabPage("求解设置");tabs.TabPages.Add(page);var panel=Layout(page);var enable=new CheckBox{Text="启用全局求解设置",AutoSize=true,Checked=draft.solver?.enabled??false};Add(panel,enable);
   var preset=new Button{Text="应用刚性机器人预设",AutoSize=true};Add(panel,preset);var fields=new Panel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Top};Add(panel,fields);
   Note(panel,"Newton + elliptic；保留 refsafe。margin 默认 1 mm，可在碰撞对中改为 2 mm。Noslip 默认关闭。局部覆盖优先；未启用且没有局部覆盖时保留原行为。");
   Action rebuild=()=>{foreach(Control c in fields.Controls.Cast<Control>().ToArray())c.Dispose();var settings=draft.solver=draft.solver??new SolverSettings();var p=Layout(fields);fields.Enabled=fields.Visible=settings.enabled;
    SolverParametersControl.AddNumber(p,settings,"timestep","步长 s",invalid);SolverParametersControl.AddNumber(p,settings,"iterations","最大迭代数",invalid);SolverParametersControl.AddNumber(p,settings,"tolerance","收敛容差",invalid);SolverParametersControl.AddNumber(p,settings,"impratio","摩擦阻抗比",invalid);SolverParametersControl.AddNumber(p,settings,"noslip_iterations","防慢滑后处理迭代数",invalid);
    Note(p,"默认闭链参数");Add(p,new SolverParametersControl(settings.equality??(settings.equality=new ConstraintSettings()),new ConstraintSettings(),false,v=>settings.equality=v,invalid,false));
    Note(p,"默认接触参数（环境接触及未覆盖碰撞对）");Add(p,new SolverParametersControl(settings.contact??(settings.contact=ConstraintSettings.Contact()),ConstraintSettings.Contact(),true,v=>settings.contact=v,invalid,false));};
   enable.CheckedChanged+=(s,e)=>{draft.solver=draft.solver??new SolverSettings();draft.solver.enabled=enable.Checked;JointEditorControl.PreserveScroll(fields,()=>{fields.Enabled=fields.Visible=enable.Checked;foreach(var box in SolverParametersControl.Children(fields).OfType<TextBox>()){double n;if(!enable.Checked)invalid.Remove(box);else if(!double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)||double.IsNaN(n)||double.IsInfinity(n))invalid.Add(box);}});};
   preset.Click+=(s,e)=>{JointEditorControl.PreserveScroll(fields,()=>{var defaults=new SolverSettings();var settings=draft.solver;foreach(var property in typeof(SolverSettings).GetProperties().Where(x=>x.PropertyType.IsValueType||x.PropertyType==typeof(string)))property.SetValue(settings,property.GetValue(defaults));foreach(var pair in new[]{new[]{settings.equality,defaults.equality},new[]{settings.contact,defaults.contact}})foreach(var property in typeof(ConstraintSettings).GetProperties())property.SetValue(pair[0],property.GetValue(pair[1]));settings.enabled=true;enable.Checked=true;foreach(var box in fields.Controls.Cast<Control>().SelectMany(Descendants).OfType<TextBox>()){var property=box.Tag as PropertyInfo;if(property!=null&&property.DeclaringType==typeof(SolverSettings))box.Text=Convert.ToDouble(property.GetValue(settings)).ToString("G12",CultureInfo.InvariantCulture);}foreach(var control in Descendants(fields).OfType<SolverParametersControl>())control.ReloadValues();});status.Text="已应用预设；已有局部覆盖保留。保存配置并重新导出后生效。";};rebuild();
  }
  string Owner=>(string)link.SelectedItem;
  string Unique(string prefix)=>prefix+"_"+Guid.NewGuid().ToString("N").Substring(0,8);
  new void Refresh(){foreach(var refresh in refreshers)refresh();}
  void AttachmentTab(TabControl tabs){
   var page=new CollisionTabPage("附着点");tabs.TabPages.Add(page);var panel=Layout(page);var list=new ListBox{Height=90};Add(panel,list);
   var buttons=new FlowLayoutPanel{AutoSize=true};var add=new Button{Text="添加"};var remove=new Button{Text="删除"};buttons.Controls.Add(add);buttons.Controls.Add(remove);Add(panel,buttons);
   var name=new TextBox();Field(panel,"名称",name);var type=new CollisionComboBox();type.Items.AddRange(new[]{"point","frame"});Field(panel,"类型",type);
   var pick=new Button{Text="拾取参考",AutoSize=true};Add(panel,pick);var source=new Label{AutoSize=true,MaximumSize=new Size(320,0)};Add(panel,source);
   var rebind=new Button{Text="重新绑定到所选 link",AutoSize=true};Add(panel,rebind);
   Note(panel,"point：参考点、顶点、草图点，只记录位置。\nframe：参考坐标系，记录位置与方向。\n均相对所属 link；确认保存允许保留未拾取草稿，导出时严格检查。");
   Attachment current=null;
   Action load=()=>{bool previous=loading;loading=true;current=list.SelectedItem as Attachment;name.Text=current?.name??"";type.SelectedItem=current?.type;source.Text=current?.source_name??"尚未拾取";name.Enabled=type.Enabled=pick.Enabled=current!=null;loading=previous;};
   Action refresh=()=>{loading=true;string selected=current?.name;list.Items.Clear();var identities=service.LinkIdentities();foreach(var a in draft.attachments.Where(a=>a.link==Owner||(!string.IsNullOrEmpty(a.link_id)&&!identities.ContainsKey(a.link_id))))list.Items.Add(a);list.SelectedItem=list.Items.Cast<Attachment>().FirstOrDefault(a=>a.name==selected)??list.Items.Cast<Attachment>().FirstOrDefault();load();if(current!=null&&!string.IsNullOrEmpty(current.link_id)&&!identities.ContainsKey(current.link_id))source.Text="所属 link 失效："+current.link+" / "+current.link_id+"\n"+source.Text;loading=false;};refreshers.Add(refresh);
   rebind.Click+=(s,e)=>Guard(()=>{if(current==null)return;service.RequireCurrentDocument();var target=service.LinkIdentities().Single(x=>x.Value==Owner);if(MessageBox.Show("将 site '"+current.name+"' 显式绑定到 link '"+target.Value+"'。参考几何体保留，导出时重新计算相对位置。继续？","重新绑定 site",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;current.link_id=target.Key;current.link=target.Value;refresh();UpdateSites();Schedule();});
   list.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;list.SelectedItem=current;loading=false;status.Text="请先修正无效输入。";return;}armed=null;load();Schedule();};
   name.TextChanged+=(s,e)=>{if(loading||current==null)return;Guard(()=>{draft.RenameSite(current,name.Text);invalid.Remove(name);name.BackColor=SystemColors.Window;RefreshListDisplay(list);UpdateSites();});if(current.name!=name.Text.Trim()){invalid.Add(name);name.BackColor=Color.MistyRose;}};
   type.SelectedIndexChanged+=(s,e)=>{if(loading||current==null)return;current.type=(string)type.SelectedItem;current.reference=null;current.source_pid=null;current.source_name=null;source.Text="类型已改变，请重新拾取。";RefreshListDisplay(list);UpdateSites();};
   add.Click+=(s,e)=>{if(invalid.Count>0)return;current=new Attachment{name=Unique("site"),link=Owner,type="point"};draft.attachments.Add(current);refresh();};
   remove.Click+=(s,e)=>Guard(()=>{if(current==null)return;service.RequireCurrentDocument();var plan=ConfigurationDependencies.Plan(draft,sites:new[]{current.id});if(MessageBox.Show("将删除以下配置：\n"+string.Join("\n",plan.Affected)+"\n\n删除并清理关联配置？","删除 site",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return;service.RequireCurrentDocument();plan.ApplyTo(draft);invalid.Remove(name);current=null;armed=null;Refresh();UpdateSites();Schedule();status.Text="已清理关联配置；保存配置后写入装配。";});
   pick.Click+=(s,e)=>{armed=current;status.Text=current.type=="frame"?"请选择参考坐标系。":"请选择参考点、顶点或草图点。";BeginSelection?.Invoke();};
  }
  void Rules<T>(TabControl tabs,string title,List<T> items,Func<T> create,Func<T,bool> filter,string[] kinds,string hint,Action<TableLayoutPanel,T> fields)where T:class{
   var page=new CollisionTabPage(title);tabs.TabPages.Add(page);var panel=Layout(page);Note(panel,hint);var list=new ListBox{Height=80};Add(panel,list);
   var buttons=new FlowLayoutPanel{AutoSize=true};var add=new Button{Text="添加"};var remove=new Button{Text="删除"};buttons.Controls.Add(add);buttons.Controls.Add(remove);Add(panel,buttons);var details=Layout(panel);Add(panel,details);T current=null;T displayed=null;List<Action> updates=null;
   Action load=null;load=()=>{if(ReferenceEquals(current,displayed)&&updates!=null){JointEditorControl.PreserveScroll(details,()=>{foreach(var update in updates)update();});return;}displayed=current;armed=null;foreach(var box in Descendants(details).OfType<TextBox>())invalid.Remove(box);foreach(Control child in details.Controls.Cast<Control>().ToArray())child.Dispose();details.Controls.Clear();updates=null;if(current==null){if(typeof(T)==typeof(SiteForceConfig))selectedForce=null;if(constraintsOnly){selectedEquality=null;Schedule();}return;}loading=true;details.RowCount=0;details.RowStyles.Clear();var p=details;conditions=updates=new List<Action>();p.Tag=updates;StringField(p,current,"name","名称",()=>RefreshListDisplay(list));var type=Combo(p,current,"type","类型",kinds);fields(p,current);foreach(var update in updates)update();loading=false;
    type.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;type.SelectedItem=typeof(T).GetProperty("type").GetValue(current);loading=false;status.Text="请先修正无效数字。";return;}typeof(T).GetProperty("type").SetValue(current,type.SelectedItem);RefreshListDisplay(list);JointEditorControl.PreserveScroll(details,()=>{foreach(var update in updates)update();});Schedule();};};
   Action refresh=()=>{loading=true;list.Items.Clear();foreach(var item in items.Where(filter))list.Items.Add(item);if(current!=null&&list.Items.Contains(current))list.SelectedItem=current;else list.SelectedIndex=list.Items.Count>0?0:-1;current=list.SelectedItem as T;load();loading=false;};refreshers.Add(refresh);
   list.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;list.SelectedItem=current;loading=false;status.Text="请先修正无效数字。";return;}current=list.SelectedItem as T;load();};
   page.Enter+=(s,e)=>{if(invalid.Count==0)refresh();};
   add.Click+=(s,e)=>{if(invalid.Count>0){status.Text="请先修正无效数字。";return;}current=create();items.Add(current);refresh();};
   remove.Click+=(s,e)=>{if(current==null)return;foreach(var box in Descendants(details).OfType<TextBox>())invalid.Remove(box);items.Remove(current);current=null;refresh();};
  }
  void RefreshListDisplay(ListBox list){bool previous=loading;loading=true;int selected=list.SelectedIndex;for(int i=0;i<list.Items.Count;i++)list.Items[i]=list.Items[i];list.SelectedIndex=selected;loading=previous;}
  static IEnumerable<Control> Descendants(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Descendants(c))yield return child;}}
  static TableLayoutPanel Layout(Control parent){var panel=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Padding=new Padding(4)};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));parent.Controls.Add(panel);return panel;}
  static void Add(TableLayoutPanel panel,Control c){c.Dock=DockStyle.Top;c.Margin=new Padding(0,2,0,2);panel.Controls.Add(c);}
  static void Field(TableLayoutPanel panel,string text,Control c){Add(panel,new Label{Text=text,AutoSize=true});Add(panel,c);}
  static void Note(TableLayoutPanel panel,string text){Add(panel,new Label{Text=text,AutoSize=true,MaximumSize=new Size(320,0)});}
  void StringField(TableLayoutPanel p,object item,string key,string label,Action changed=null){var property=item.GetType().GetProperty(key);var box=new TextBox{Text=(string)property.GetValue(item)};Field(p,label,box);box.TextChanged+=(s,e)=>{if(!loading){property.SetValue(item,box.Text.Trim());changed?.Invoke();if(item is SiteForceConfig)Schedule();}};}
  ComboBox Combo(TableLayoutPanel p,object item,string key,string label,string[] choices){var property=item.GetType().GetProperty(key);var box=new CollisionComboBox();box.Items.AddRange(choices);string value=(string)property.GetValue(item);if(value!=null&&!box.Items.Contains(value))box.Items.Add(value);box.SelectedItem=value;Field(p,label,box);if(key!="type")box.SelectedIndexChanged+=(s,e)=>{if(!loading){if(invalid.Count>0){loading=true;box.SelectedItem=property.GetValue(item);loading=false;status.Text="请先修正无效数字。";return;}property.SetValue(item,box.SelectedItem);var identity=item.GetType().GetProperty(key+"_id");if(identity!=null)identity.SetValue(item,null);Schedule();}};return box;}
  void Numeric(TableLayoutPanel p,object item,string key,string label){var property=item.GetType().GetProperty(key);var box=new TextBox{Text=((double)property.GetValue(item)).ToString("G12",CultureInfo.InvariantCulture)};Field(p,label,box);Action check=()=>{double value;if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)){property.SetValue(item,value);invalid.Remove(box);box.BackColor=SystemColors.Window;if(item is SiteForceConfig)Schedule();}else{invalid.Add(box);box.BackColor=Color.MistyRose;status.Text="请输入有限数字："+label;}};box.Tag=check;box.TextChanged+=(s,e)=>{if(!loading)check();};box.Disposed+=(s,e)=>invalid.Remove(box);}
  public void CaptureSelected(){if(armed==null)return;Guard(()=>{var captured=service.CaptureSelectedAttachment(armed.link,armed.name,armed.type);AcceptCapturedAttachment(captured);armed=null;Refresh();Schedule();});}
  void AcceptCapturedAttachment(Attachment captured){armed.reference=captured.reference;armed.source_name=captured.source_name;}
  void Schedule(){timer.Stop();timer.Start();}
  void Preview(){
   if(!show.Checked){preview.Clear();previewKey=null;return;}
   string key=service.CollisionRevision+new JavaScriptSerializer().Serialize(new{draft.attachments,force=constraintsOnly?null:selectedForce,selected=!constraintsOnly||selectedEquality==null?null:new{selectedEquality.name,selectedEquality.type,selectedEquality.binding,selectedEquality.site1,selectedEquality.site2,selectedEquality.body1,selectedEquality.body2,selectedEquality.anchor,selectedEquality.pose_mode,selectedEquality.position,selectedEquality.orientation}});if(key==previewKey)return;var geometries=new List<CollisionGeometry>();string error=null;
   var visible=draft.attachments.AsEnumerable();if(constraintsOnly)visible=selectedEquality==null||selectedEquality.type=="joint"||selectedEquality.binding=="body"?Enumerable.Empty<Attachment>():visible.Where(a=>a.name==selectedEquality.site1||a.name==selectedEquality.site2);
   foreach(var a in visible)try{
    if(a.reference==null&&string.IsNullOrEmpty(a.source_pid)){error=a.name+"：尚未拾取参考。";continue;}var pose=service.AttachmentPose(a);geometries.Add(new CollisionGeometry{id="site:"+a.id,name=a.name,link=a.link,type="sphere",size=new[]{.003},xyz=MathOps.GetXYZ(pose),rpy=new double[3]});
    if(a.type=="frame")for(int axis=0;axis<3;axis++){var xyz=new double[3];xyz[axis]=.01;var rpy=axis==0?new[]{0.0,Math.PI/2,0}:axis==1?new[]{-Math.PI/2,0.0,0}:new double[3];var frame=pose*MathOps.GetTransformation(xyz,rpy);geometries.Add(new CollisionGeometry{id="site:"+a.id+":"+axis,name=a.name+":"+axis,link=a.link,type="cylinder",size=new[]{.0006,.02},xyz=MathOps.GetXYZ(frame),rpy=MathOps.GetRPY(frame)});}
   }catch(Exception e){error=a.name+"："+e.Message;}
   if(!constraintsOnly&&selectedForce!=null&&selectedForce.enabled&&(selectedForce.type=="spring"?(selectedForce.stiffness>0||selectedForce.damping>0):selectedForce.magnitude>0)){var first=draft.attachments.FirstOrDefault(a=>a.id==selectedForce.site1_id);var second=draft.attachments.FirstOrDefault(a=>a.id==selectedForce.site2_id);if(first!=null&&second!=null&&first.link!=second.link)try{var line=service.ConstraintPreview(first,second,"force:"+selectedForce.name).ToArray();if(line.Length==0)error="两点作用力：端点重合，方向无定义。";else geometries.AddRange(line);}catch(Exception e){error=e.Message;}}
   if(constraintsOnly&&selectedEquality!=null&&selectedEquality.type!="joint"&&selectedEquality.binding=="site"){var a=draft.attachments.FirstOrDefault(x=>x.name==selectedEquality.site1);var b=draft.attachments.FirstOrDefault(x=>x.name==selectedEquality.site2);if(a!=null&&b!=null)try{geometries.AddRange(service.ConstraintPreview(a,b,selectedEquality.name));}catch(Exception ex){error=ex.Message;}}
   if(constraintsOnly&&selectedEquality!=null&&selectedEquality.type!="joint"&&selectedEquality.binding=="body")try{geometries.AddRange(service.ConstraintBodyPreview(selectedEquality));}catch(Exception ex){error=ex.Message;}
   try{if(geometries.Count==0)preview.Clear();else preview.Show(geometries,null);}catch(Exception e){error=e.Message;}if(error==null)previewKey=key;status.Text=error??(constraintsOnly?"约束预览已更新。":"附着点预览已更新。");
  }
  void Guard(Action action){try{action();}catch(Exception e){status.Text=e.Message;}}
  public ConfigurationCommitResult Save(){
   if(invalid.Count>0)throw new InvalidOperationException("请修正无效数字。");if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("Configuration 已切换，请重新进入。");
   foreach(IEnumerable values in new IEnumerable[]{draft.attachments,draft.sensors,draft.actuators,draft.equalities,draft.site_forces}){var names=new HashSet<string>();foreach(object item in values){string name=(string)item.GetType().GetProperty("name").GetValue(item);if(string.IsNullOrWhiteSpace(name)||!names.Add(name))throw new InvalidOperationException("名称不能为空或重复："+name);}}
   draft.NormalizeSiteReferences();draft.ValidateSolver();var previous=service.Project;service.Project=draft;ConfigurationCommitResult result;try{result=service.Save();}catch{service.Project=previous;throw;}status.Text="配置已写入装配；请保存 .sldasm。未完成参考在导出时检查。";if(!result.RefreshSucceeded){Enabled=false;status.Text=result.Message;}return result;
  }
  bool CheckSession(){try{service.RequireCurrentDocument();return true;}catch(System.IO.InvalidDataException e){timer.Stop();cadTimer.Stop();preview.Clear();Enabled=false;status.Text=e.Message;return false;}}
        void FlushDraft(){ValidateChildren();if(invalid.Count>0)throw new InvalidOperationException("请修正仿真配置中的无效数字。");}
  protected override void Dispose(bool disposing){if(disposing){if(ownsPage){ownsPage=false;service.EndPage();}timer.Dispose();cadTimer.Dispose();preview?.Dispose();}base.Dispose(disposing);}
 }
}
