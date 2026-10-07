using SW2URDF.Simulation;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace SW2URDF.UI {
 sealed class JointScrollPanel:Panel{protected override Point ScrollToControl(Control activeControl){return DisplayRectangle.Location;}}
 public sealed class JointEditorControl:UserControl {
  readonly AttachmentService service;readonly SimulationProject draft;readonly List<JointDescriptor> descriptors;
  readonly ComboBox joint=new CollisionComboBox();readonly Panel details=new JointScrollPanel{Dock=DockStyle.Fill,AutoScroll=true};
  readonly HashSet<TextBox> invalid=new HashSet<TextBox>();readonly Label status=new Label{AutoSize=true,MaximumSize=new Size(320,0)};
  readonly CheckBox show=new CheckBox{Text="实时预览关节轴 / 限位范围",Checked=true,AutoSize=true};readonly Timer timer=new Timer{Interval=180},cadTimer=new Timer{Interval=500};
  readonly CollisionPreview preview;readonly string configuration;string revision,displayed;JointConfiguration current;JointDescriptor descriptor;bool loading;readonly List<Action> refresh=new List<Action>();
  public Action BeginSelection{get;set;}
  public JointEditorControl(AttachmentService service,List<JointDescriptor> descriptors){
   this.service=service;this.descriptors=descriptors;draft=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<SimulationProject>(new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(service.Project));
   configuration=service.Model.ConfigurationManager.ActiveConfiguration.Name;revision=service.CollisionRevision;preview=new CollisionPreview(service);Dock=DockStyle.Fill;
   Controls.Add(details);
   var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};top.Controls.Add(new Label{Text="所属 joint（各关节独立配置）",AutoSize=true});top.Controls.Add(joint);joint.Dock=DockStyle.Top;
   joint.Items.AddRange(descriptors.Select(d=>d.name).ToArray());Controls.Add(top);
   var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};footer.Controls.Add(show);var refreshCAD=new Button{Text="刷新 CAD 参考",AutoSize=true};refreshCAD.Click+=(s,e)=>{service.InvalidateCADCache();Schedule();};footer.Controls.Add(refreshCAD);var save=new Button{Text="保存配置到装配",AutoSize=true};footer.Controls.Add(save);footer.Controls.Add(status);Controls.Add(footer);
   joint.SelectedIndexChanged+=(s,e)=>{if(loading)return;if(invalid.Count>0){loading=true;joint.SelectedItem=displayed;loading=false;status.Text="请先修正无效数字。";return;}displayed=(string)joint.SelectedItem;descriptor=descriptors.First(d=>d.name==displayed);current=draft.joints.FirstOrDefault(j=>!string.IsNullOrWhiteSpace(descriptor.id)?j.joint_id==descriptor.id:string.IsNullOrWhiteSpace(j.joint_id)&&j.joint==displayed);if(current==null){current=new JointConfiguration{joint=displayed,joint_id=descriptor.id};if(new[]{"revolute","continuous","prismatic"}.Contains(descriptor.type))draft.joints.Add(current);}Build();Schedule();};
   save.Click+=(s,e)=>Guard(Save);show.CheckedChanged+=(s,e)=>Schedule();timer.Tick+=(s,e)=>{timer.Stop();if(invalid.Count==0)Preview();};cadTimer.Tick+=(s,e)=>{if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration){timer.Stop();cadTimer.Stop();preview.Clear();Enabled=false;status.Text="Configuration 已切换，请重新进入。";return;}if(revision!=service.CollisionRevision){revision=service.CollisionRevision;Schedule();}};
   if(joint.Items.Count>0)joint.SelectedIndex=0;else Build();cadTimer.Start();
  }
  static void Add(TableLayoutPanel p,Control c){c.Dock=DockStyle.Top;c.Margin=new Padding(0,2,0,2);p.Controls.Add(c);}
  static void Note(TableLayoutPanel p,string text)=>Add(p,new Label{Text=text,AutoSize=true,MaximumSize=new Size(310,0)});
  void RefreshFields(){PreserveScroll(details,()=>{foreach(var action in refresh)action();});}
  internal static void PreserveScroll(Control root,Action action){var controls=SolverParametersControl.Children(root).OfType<ScrollableControl>().Where(c=>c.AutoScroll).ToList();for(var c=root;c!=null;c=c.Parent){var scroll=c as ScrollableControl;if(scroll!=null&&scroll.AutoScroll&&!controls.Contains(scroll))controls.Add(scroll);}var positions=controls.ToDictionary(c=>c,c=>c.AutoScrollPosition);root.SuspendLayout();try{action();}finally{root.ResumeLayout(true);foreach(var pair in positions){if(pair.Key.IsDisposed)continue;pair.Key.PerformLayout();pair.Key.AutoScrollPosition=new Point(-pair.Value.X,-pair.Value.Y);}}}
  void Choice(TableLayoutPanel p,string label,string[] values,string selected,Action<string> changed){Note(p,label);var box=new CollisionComboBox();box.Items.AddRange(values);box.SelectedItem=selected;Add(p,box);box.SelectedIndexChanged+=(s,e)=>{if(loading)return;changed((string)box.SelectedItem);if(label.StartsWith("限位："))Sync("限位",current.lower,current.upper);if(label.StartsWith("弹簧："))Sync("弹簧",current.stiffness,current.springref);RefreshFields();Schedule();};}
  void Sync(string prefix,params double?[] values){loading=true;try{int i=0;foreach(var box in SolverParametersControl.Children(details).OfType<TextBox>().Where(b=>(b.Tag as string??"").StartsWith(prefix)).Take(values.Length)){box.Text=values[i++]?.ToString("G12",CultureInfo.InvariantCulture)??"";invalid.Remove(box);box.BackColor=SystemColors.Window;}}finally{loading=false;}}
  TextBox Number(TableLayoutPanel p,string label,double? value,Action<double?> changed,bool optional=false){Note(p,label);var box=new TextBox{Text=value?.ToString("G12",CultureInfo.InvariantCulture)??"",Tag=label};Add(p,box);box.Disposed+=(s,e)=>invalid.Remove(box);Action check=()=>{if(loading)return;double v;if(optional&&string.IsNullOrWhiteSpace(box.Text)){changed(null);invalid.Remove(box);box.BackColor=SystemColors.Window;if(invalid.Count==0)status.Text="参数已更新。";Schedule();}else if(double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out v)&&!double.IsNaN(v)&&!double.IsInfinity(v)){changed(v);invalid.Remove(box);box.BackColor=SystemColors.Window;if(invalid.Count==0)status.Text="参数已更新。";Schedule();}else{invalid.Add(box);box.BackColor=Color.MistyRose;status.Text="请输入有限数字："+label;}};box.TextChanged+=(s,e)=>check();if(!value.HasValue&&!optional)invalid.Add(box);return box;}
  void Scalar(TableLayoutPanel p,string label,double? value,Action<double?> changed){Number(p,label,value,changed,true);}
  TableLayoutPanel Section(TableLayoutPanel parent,Func<bool> visible){var panel=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Add(parent,panel);refresh.Add(()=>{bool enabled=visible();panel.Visible=panel.Enabled=enabled;foreach(var box in SolverParametersControl.Children(panel).OfType<TextBox>()){if(!enabled)invalid.Remove(box);else if(box.Enabled){double number;bool blank=string.IsNullOrWhiteSpace(box.Text);bool optional=(box.Tag as string??"").StartsWith("限位提前量");bool valid=blank&&optional||double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&!double.IsNaN(number)&&!double.IsInfinity(number);if(valid)invalid.Remove(box);else invalid.Add(box);}}});return panel;}
  double DefaultPhysics(string key){bool driven=draft.actuators.Any(a=>!string.IsNullOrWhiteSpace(current.joint_id)?a.joint_id==current.joint_id:string.IsNullOrWhiteSpace(a.joint_id)&&a.joint==current.joint);return key=="armature"?(driven?.001:0):(driven?.01:.001);}
  void Physics(TableLayoutPanel p,string label,string key,Func<double?> read,Action<double?> write){var box=Number(p,label,read()??DefaultPhysics(key),v=>{write(v);RefreshFields();},true);var state=new Label{AutoSize=true,Tag="default"};Add(p,state);refresh.Add(()=>state.Text=read().HasValue?"手动值（留空恢复默认）":"默认值："+DefaultPhysics(key).ToString("G12",CultureInfo.InvariantCulture)+"（按 actuator 引用）");box.Leave+=(s,e)=>{if(string.IsNullOrWhiteSpace(box.Text)){loading=true;box.Text=DefaultPhysics(key).ToString("G12",CultureInfo.InvariantCulture);loading=false;}};}
  void Build(){
   loading=true;refresh.Clear();foreach(Control c in details.Controls.Cast<Control>().ToArray())c.Dispose();details.Controls.Clear();invalid.Clear();
   var p=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(4)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));details.Controls.Add(p);
   Choice(p,"根基座模式",new[]{"inherit","fixed","floating"},draft.base_mode,v=>draft.base_mode=v);Note(p,"inherit 沿用原转换；fixed 固定；floating 六自由度浮动，仅作用于根基座。");
   if(current==null){Note(p,"没有可配置的单自由度关节。仍可设置根基座模式。");loading=false;return;}
   Note(p,descriptor.parent+" → "+descriptor.child+"；URDF 类型："+descriptor.type);
   if(!new[]{"revolute","continuous","prismatic"}.Contains(descriptor.type)){Note(p,"固定、浮动和 planar 关节不支持单自由度参数覆盖，请在 URDF 配置中调整结构。");loading=false;return;}
   Choice(p,"类型",new[]{"inherit","hinge","slide"},current.type,v=>current.type=v);string unit=(current.type=="slide"||(current.type=="inherit"&&descriptor.type=="prismatic"))?"m":"rad";
   Note(p,"位置和轴向来自 URDF 树。阻尼、干摩擦、附加惯量按 actuator 引用提供默认值；手动值优先，留空恢复默认。其他参数留空沿用原配置。");
   Physics(p,"阻尼 damping（"+(unit=="rad"?"N·m·s/rad":"N·s/m")+"）","damping",()=>current.damping,v=>current.damping=v);
   Physics(p,"干摩擦 frictionloss（"+(unit=="rad"?"N·m":"N")+"）","frictionloss",()=>current.frictionloss,v=>current.frictionloss=v);
   Physics(p,"附加惯量 armature（"+(unit=="rad"?"kg·m²":"kg")+"）","armature",()=>current.armature,v=>current.armature=v);
   Choice(p,"限位：inherit 沿用 / none 无 / custom 自定义",new[]{"inherit","none","custom"},current.limit_mode,v=>{current.limit_mode=v;if(v=="custom"){current.lower=current.lower??descriptor.lower;current.upper=current.upper??descriptor.upper;}});
   var bounds=Section(p,()=>current.limit_mode=="custom");Number(bounds,"限位下限 "+unit,current.lower,v=>current.lower=v);Number(bounds,"限位上限 "+unit,current.upper,v=>current.upper=v);
   var limits=Section(p,()=>current.limit_mode=="custom"||(current.limit_mode=="inherit"&&descriptor.type!="continuous"));
   Scalar(limits,"限位提前量 margin（"+unit+"）",current.margin,v=>current.margin=v);Add(limits,new SolverParametersControl(current.limit_solver,JointConfiguration.LimitDefaults(),false,v=>current.limit_solver=v,invalid,true,"自定义限位求解参数",unit));
   var friction=Section(p,()=>(current.frictionloss??DefaultPhysics("frictionloss"))>0);Add(friction,new SolverParametersControl(current.friction_solver,new ConstraintSettings{timeconst=.02,dmin=.9,dmax=.95},false,v=>current.friction_solver=v,invalid,true,"自定义干摩擦求解参数",unit));
   Choice(p,"弹簧：inherit 沿用 / off 无 / custom 自定义",new[]{"inherit","off","custom"},current.spring_mode,v=>{current.spring_mode=v;current.stiffness=v=="inherit"?(double?)null:0;if(v=="custom")current.springref=current.springref??current.@ref??0;else current.springref=null;});
   var springFields=Section(p,()=>current.spring_mode=="custom");{Number(springFields,"弹簧刚度（"+(unit=="rad"?"N·m/rad":"N/m")+"）",current.stiffness,v=>current.stiffness=v);Number(springFields,"弹簧平衡位置 "+unit,current.springref??0,v=>current.springref=v);}
   Scalar(p,"参考坐标值 ref（"+unit+"，不改变装配姿态）",current.@ref,v=>current.@ref=v);
   var labels=SolverParametersControl.Children(p).OfType<Label>().Where(l=>(l.Tag as string)!="default").ToDictionary(l=>l,l=>l.Text);refresh.Add(()=>{string next=(current.type=="slide"||(current.type=="inherit"&&descriptor.type=="prismatic"))?"m":"rad";foreach(var pair in labels){string text=pair.Value;if(next!=unit){text=text.Replace("N·m·s/rad","{damping}").Replace("N·s/m","{damping}").Replace("N·m/rad","{stiffness}").Replace("N/m","{stiffness}").Replace("kg·m²","{armature}");if(unit=="rad")text=text.Replace("N·m","N").Replace("rad","m");else if(text.StartsWith("干摩擦"))text=text.Replace("（N）","（N·m）");else if(text.StartsWith("限位")||text.StartsWith("弹簧平衡")||text.StartsWith("参考坐标")||text.StartsWith("solimp 变化"))text=text.Replace(" m"," rad").Replace("（m","（rad");text=text.Replace("{damping}",next=="rad"?"N·m·s/rad":"N·s/m").Replace("{stiffness}",next=="rad"?"N·m/rad":"N/m").Replace("{armature}",next=="rad"?"kg·m²":"kg");if(next=="rad"&&text.StartsWith("附加惯量"))text=text.Replace("（kg）","（kg·m²）");}pair.Key.Text=text;}});
   Note(p,"预览展示轴向与限位示意，不移动装配。参数只影响 MJCF，保存后请保存装配并重新导出。");loading=false;RefreshFields();
  }
  void Schedule(){timer.Stop();timer.Start();}
  string previewKey;
  void Preview(){if(!show.Checked||current==null){preview.Clear();previewKey=null;return;}string key=service.CollisionRevision+new JavaScriptSerializer().Serialize(new{descriptor,current.joint,current.type,current.limit_mode,current.lower,current.upper,current.@ref});if(key==previewKey)return;Guard(()=>{preview.Show(service.JointPreview(descriptor,current),null);previewKey=key;status.Text="关节预览已更新。";});}
  void Guard(Action action){try{action();}catch(Exception e){status.Text=e.Message;}}
  public void Save(){if(invalid.Count>0)throw new InvalidOperationException("请填写并修正无效数字。");if(service.Model.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("Configuration 已切换。");draft.joint_defaults=true;draft.ValidateSolver();foreach(var j in draft.joints){var d=descriptors.FirstOrDefault(x=>x.name==j.joint);if(d==null)throw new InvalidOperationException("关节已失效："+j.joint);bool changed=j.type!="inherit"&&j.type!=(d.type=="prismatic"?"slide":"hinge");if(changed&&j.limit_mode=="inherit")throw new InvalidOperationException("改变关节类型时必须选择无限位或自定义限位。");}var previous=service.Project;service.Project=draft;try{service.Save();}catch{service.Project=previous;throw;}status.Text="配置已写入装配，请保存 .sldasm。";}
  protected override void Dispose(bool disposing){if(disposing){timer.Dispose();cadTimer.Dispose();preview.Dispose();}base.Dispose(disposing);}
 }
}
