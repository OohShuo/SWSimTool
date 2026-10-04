param([string]$Payload='collision-navigation-final')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root ('build\'+$Payload)
$interop='D:\sw\sw2025\SOLIDWORKS'
$refs=@("$interop\SolidWorks.Interop.sldworks.dll","$interop\SolidWorks.Interop.swconst.dll","$interop\SolidWorks.Interop.swpublished.dll","$bin\SW2URDF.dll","$bin\MathNet.Numerics.dll","$bin\Moq.dll","$bin\Castle.Core.dll",'System.Windows.Forms','System.Drawing','System.Runtime.Serialization','System.Web.Extensions','System.Xml','System.Core')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
# This test uses mocked CAD interfaces only. It never connects to SolidWorks.
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using Moq;
using SolidWorks.Interop.sldworks;
using SW2URDF.Simulation;
using SW2URDF.UI;
using SW2URDF.URDFExport;
public static class CollisionNavigationProbe {
 static object Get(object o,string n){return o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);}
 static void Set(object o,string n,object v){o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(o,v);}
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
 public static void Run(){
  var config=new Mock<Configuration>();config.SetupGet(c=>c.Name).Returns("test");
  var manager=new Mock<ConfigurationManager>();manager.SetupGet(c=>c.ActiveConfiguration).Returns(config.Object);
  var model=new Mock<ModelDoc2>();model.SetupGet(m=>m.ConfigurationManager).Returns(manager.Object);model.Setup(m=>m.GetUpdateStamp()).Returns(0);
  var exporter=(ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));exporter.ActiveSWModel=model.Object;
  var service=(AttachmentService)FormatterServices.GetUninitializedObject(typeof(AttachmentService));Set(service,"exporter",exporter);
  service.Project=new SimulationProject();service.Project.collision=new CollisionConfiguration();
  var first=new CollisionGeometry{link="yaw",name="first",definition="frame",xyz=new[]{.1,.2,.3}};
  var second=new CollisionGeometry{link="yaw",name="second"};var other=new CollisionGeometry{link="pitch",name="other"};
  service.Project.collision.geometries.AddRange(new[]{first,second,other});service.Project.collision.link_modes["yaw"]="primitive";
  service.Project.collision.allowed_pairs.AddRange(new[]{new CollisionPair{link1="yaw",link2="pitch"},new CollisionPair{link1="yaw",link2="base"}});
  Set(service,"collisionTree",FormatterServices.GetUninitializedObject(typeof(SW2URDF.URDF.LinkNode)));
  Set(service,"cachedRevision","test:0");Set(service,"cachedFrames",new Dictionary<string,Matrix<double>>{{"yaw",DenseMatrix.CreateIdentity(4)},{"pitch",DenseMatrix.CreateIdentity(4)},{"base",DenseMatrix.CreateIdentity(4)}});
  using(var editor=new CollisionEditorControl(service,"yaw")){
   ((Timer)Get(editor,"timer")).Stop();((Timer)Get(editor,"cadTimer")).Stop();
   var link=(ComboBox)Get(editor,"link");var list=(ListBox)Get(editor,"list");var name=(TextBox)Get(editor,"name");var sizes=(TextBox[])Get(editor,"size");
   sizes[0].Text="77";list.SelectedIndex=1;
   Check(name.Text=="second"&&((CollisionGeometry)Get(editor,"current")).name=="second","unfinished reference allows geometry selection; form follows selection");
   list.SelectedIndex=0;Check(sizes[0].Text=="77","unfinished geometry retains edited dimensions");
   Check(((CollisionGeometry)Get(editor,"current")).xyz[0]==.1,"failed resolution retains last valid pose");
   link.SelectedItem="pitch";Check(list.Items.Count==1&&name.Text=="other","link change refreshes its geometry list and properties");
   link.SelectedItem="base";Check(list.Items.Count==0&&name.Text==""&&Get(editor,"current")==null,"empty link clears old selection and properties");
   link.SelectedItem="yaw";Check(list.Items.Count==2&&name.Text=="first","returning to link restores its own geometry");
   sizes[0].Text="invalid";link.SelectedItem="pitch";Check((string)link.SelectedItem=="yaw"&&name.Text=="first","invalid numeric entry restores link selection consistently");
   list.SelectedIndex=1;Check(list.SelectedIndex==0&&name.Text=="first","invalid numeric entry restores geometry selection consistently");
   sizes[0].Text="77";
   bool rejected=false;try{typeof(CollisionEditorControl).GetMethod("ReadCurrent",BindingFlags.Instance|BindingFlags.NonPublic,null,Type.EmptyTypes,null).Invoke(editor,null);}catch(TargetInvocationException ex){rejected=ex.InnerException.Message.Contains("\u53c2\u8003");}
   Check(rejected,"strict save/preview read still rejects incomplete references");
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(editor,"\u6dfb\u52a0"),new object[]{EventArgs.Empty});Check(list.Items.Count==3&&name.Text.StartsWith("collision_"),"adding geometry is allowed with unfinished references");
   Check(service.Project.collision.geometries.Count==3,"editing does not modify persisted project before save");
   var contactEditor=All(editor).OfType<SolverParametersControl>().Single();
   All(contactEditor).OfType<CheckBox>().Single().Checked=true;
   var margin=All(contactEditor).OfType<TextBox>().Single(b=>((PropertyInfo)b.Tag).Name=="margin");margin.Text="0.002";
   var collisionDraft=(SimulationProject)Get(editor,"draft");
   Check(collisionDraft.collision.allowed_pairs[0].solver.margin==.002&&service.Project.collision.allowed_pairs[0].solver==null,"pair margin override edits draft only");
   margin.Text="invalid";var pairList=(ListBox)Get(editor,"pairs");pairList.SelectedIndex=1;
   Check(pairList.SelectedIndex==0,"invalid contact value prevents silently switching pair");
   All(contactEditor).OfType<CheckBox>().Single().Checked=false;pairList.SelectedIndex=1;
   Check(pairList.SelectedIndex==1&&collisionDraft.collision.allowed_pairs[0].solver==null,"disabling local override restores inheritance and clears invalid draft input");
  }
  using(var solverControl=new SolverParametersControl(null,ConstraintSettings.Contact(),true,v=>{},new HashSet<TextBox>())){
   var toggle=All(solverControl).OfType<CheckBox>().Single();var values=All(solverControl).OfType<TextBox>().First().Parent;
   Check(!values.Visible,"inherited solver fields are hidden");toggle.Checked=true;Check(values.Visible,"local override displays solver fields");toggle.Checked=false;Check(!values.Visible,"disabling override hides solver fields again");
  }
  service.Project.attachments.AddRange(new[]{new Attachment{name="yaw_site",link="yaw",type="frame"},new Attachment{name="pitch_site",link="pitch",type="point"}});
  using(var editor=new SimulationEditorControl(service,"yaw",new Dictionary<string,string>{{"yaw_joint","yaw"},{"pitch_joint","pitch"}})){
   ((Timer)Get(editor,"timer")).Stop();((Timer)Get(editor,"cadTimer")).Stop();
   var link=(ComboBox)Get(editor,"link");var tabs=All(editor).OfType<TabControl>().Single();Check(tabs.TabPages.Count==4&&!tabs.TabPages.Cast<TabPage>().Any(p=>p.Text=="闭链约束"),"simulation keeps all tabs except constraints");
   var sites=All(tabs.TabPages[0]).OfType<ListBox>().Single();
   Check(sites.Items.Count==1&&((Attachment)sites.Items[0]).name=="yaw_site","simulation page filters attachments by link");
   link.SelectedItem="pitch";
   Check(sites.Items.Count==1&&((Attachment)sites.Items[0]).name=="pitch_site","simulation link change refreshes its own attachment");
   link.SelectedItem="base";Check(sites.Items.Count==0,"empty simulation link clears attachment list");
   link.SelectedItem="yaw";tabs.SelectedIndex=2;
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(tabs.TabPages[2],"\u6dfb\u52a0"),new object[]{EventArgs.Empty});
   var joints=All(tabs.TabPages[2]).OfType<ComboBox>().Single(c=>c.Items.Contains("yaw_joint"));
   Check(joints.Items.Count==1&&joints.SelectedItem.ToString()=="yaw_joint","actuator choices belong to selected link");
   Check(!All(tabs.TabPages[2]).OfType<Label>().Any(c=>c.Text.Contains("kp")||c.Text.Contains("kv")),"motor hides position and velocity gain fields");
   var kind=All(tabs.TabPages[2]).OfType<ComboBox>().Single(c=>c.Items.Contains("position"));kind.SelectedItem="position";
   Check(All(tabs.TabPages[2]).OfType<Label>().Any(c=>c.Text.Contains("kp")),"position actuator displays its gain field");
   kind=All(tabs.TabPages[2]).OfType<ComboBox>().Single(c=>c.Items.Contains("velocity"));kind.SelectedItem="velocity";
   Check(All(tabs.TabPages[2]).OfType<Label>().Any(c=>c.Text.Contains("kv"))&&!All(tabs.TabPages[2]).OfType<Label>().Any(c=>c.Text.Contains("kp")),"velocity displays kv only");
   tabs.SelectedIndex=1;
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(tabs.TabPages[1],"添加"),new object[]{EventArgs.Empty});
   var sensorType=All(tabs.TabPages[1]).OfType<ComboBox>().Single(c=>c.Items.Contains("camera"));sensorType.SelectedItem="camera";
   Check(All(tabs.TabPages[1]).OfType<Label>().Any(c=>c.Text.Contains("视场角"))&&!All(tabs.TabPages[1]).OfType<Label>().Any(c=>c.Text.Contains("裁剪")||c.Text.Contains("noise")),"camera displays fovy without sensor cutoff or noise note");
   sensorType=All(tabs.TabPages[1]).OfType<ComboBox>().Single(c=>c.Items.Contains("tof"));sensorType.SelectedItem="tof";
   Check(!All(tabs.TabPages[1]).OfType<Label>().Any(c=>c.Text.Contains("视场角"))&&All(tabs.TabPages[1]).OfType<Label>().Any(c=>c.Text.Contains("裁剪")),"tof displays cutoff without camera fovy");
   var pickup=Find(tabs.TabPages[1],"拾取坐标系");typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(pickup,new object[]{EventArgs.Empty});var pending=(Attachment)Get(editor,"armed");var simulation=(SimulationProject)Get(editor,"draft");Check(pending.type=="frame"&&pending.link=="yaw"&&!simulation.attachments.Contains(pending),"sensor pickup arms a frame without creating an orphan before success");
   var captured=new Attachment{name="picked_imu",link="yaw",type="frame",reference=new CollisionReference{pid="mock-coordinate",component_pid="mock-component"},source_name="mock coordinate"};typeof(SimulationEditorControl).GetMethod("AcceptCapturedAttachment",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{captured});Check(simulation.attachments.Contains(captured)&&simulation.sensors[0].site==captured.name,"successful sensor pickup creates and binds frame");int siteCount=simulation.attachments.Count;typeof(SimulationEditorControl).GetMethod("AcceptCapturedAttachment",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,new object[]{new Attachment{name="duplicate",link="yaw",type="frame",reference=captured.reference}});Check(simulation.attachments.Count==siteCount&&simulation.sensors[0].site==captured.name,"repeated sensor pickup reuses frame on same link");
   tabs.SelectedIndex=2;
   var total=All(tabs.TabPages[2]).OfType<CheckBox>().Single(c=>c.Text.Contains("合计驱动力"));total.Checked=true;
   var totalBoxes=All(tabs.TabPages[2]).OfType<TextBox>().Where(c=>c.Text=="").ToArray();Check(totalBoxes.Length==2,"joint total force limit requires explicit lower and upper values");totalBoxes[0].Text="-7";totalBoxes[1].Text="7";
   var forceDraft=(SimulationProject)Get(editor,"draft");Check(forceDraft.joint_force_limits.Count==1&&forceDraft.joint_force_limits[0].joint=="yaw_joint","joint total force limit is keyed by joint");
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(tabs.TabPages[2],"添加"),new object[]{EventArgs.Empty});
   Check(All(tabs.TabPages[2]).OfType<CheckBox>().Single(c=>c.Text.Contains("合计驱动力")).Checked&&forceDraft.joint_force_limits.Count==1,"two actuators on the same joint share one total force limit");
   Check(service.Project.actuators.Count==0,"simulation edits remain a draft until save");
   tabs.SelectedIndex=3;
   var globalFields=All(tabs.TabPages[3]).OfType<TextBox>().First().Parent.Parent;
   Check(!(bool)typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(globalFields,new object[]{2}),"disabled global solver fields are hidden");
   All(tabs.TabPages[3]).OfType<CheckBox>().Single().Checked=true;
   globalFields=All(tabs.TabPages[3]).OfType<TextBox>().First().Parent.Parent;
   Check((bool)typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(globalFields,new object[]{2}),"enabled global solver fields are displayed");
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(editor,"应用刚性机器人预设"),new object[]{EventArgs.Empty});
   var simulationDraft=(SimulationProject)Get(editor,"draft");
   Check(simulationDraft.solver.enabled&&simulationDraft.solver.timestep==.001&&simulationDraft.solver.contact.timeconst==.003,"rigid preset sets global and contact defaults");
   Check(service.Project.solver==null,"preset does not change persisted project before save");
   var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
   var reloaded=serializer.Deserialize<SimulationProject>(serializer.Serialize(simulationDraft));reloaded.ValidateSolver();
   Check(reloaded.solver.contact.margin==.001&&reloaded.solver.noslip_iterations==0,"solver parameters survive configuration serialization");
   var invalidSettings=new ConstraintSettings{timeconst=-1};bool invalidSolver=false;try{invalidSettings.Validate(false);}catch{invalidSolver=true;}Check(invalidSolver,"invalid solver range rejected");

  }
  using(var constraints=new SimulationEditorControl(service,"yaw",new Dictionary<string,string>{{"yaw_joint","yaw"},{"pitch_joint","pitch"}},true)){
   ((Timer)Get(constraints,"timer")).Stop();((Timer)Get(constraints,"cadTimer")).Stop();var tabs=All(constraints).OfType<TabControl>().Single();Check(tabs.TabPages.Count==1&&tabs.TabPages[0].Text=="约束","constraint entry is separate from simulation tabs");typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(constraints,"添加"),new object[]{EventArgs.Empty});var kinds=All(constraints).OfType<ComboBox>().Single(c=>c.Items.Contains("connect"));kinds.SelectedItem="joint";Check(All(constraints).OfType<Label>().Count(c=>c.Text.StartsWith("多项式系数"))==5&&!All(constraints).OfType<ComboBox>().Any(c=>c.Items.Contains("site")),"joint constraint shows five independent coefficients and no site binding");var draft=(SimulationProject)Get(constraints,"draft");Check(draft.equalities[0].polycoef[1]==1&&draft.equalities[0].active,"new joint constraint defaults to active identity coupling");kinds=All(constraints).OfType<ComboBox>().Single(c=>c.Items.Contains("connect"));kinds.SelectedItem="weld";Check(All(constraints).OfType<Label>().Any(c=>c.Text.Contains("torquescale")),"weld shows torque scale");var binding=All(constraints).OfType<ComboBox>().Single(c=>c.Items.Contains("body"));binding.SelectedItem="body";var pose=All(constraints).OfType<ComboBox>().Single(c=>c.Items.Contains("custom"));pose.SelectedItem="custom";Check(All(constraints).OfType<Label>().Count(c=>c.Text.StartsWith("body 二相对 body 一的位置"))==3,"body weld offers separate relative position fields");Check(service.Project.equalities.Count==0,"constraint edits stay in draft");using(var form=new Form()){form.ClientSize=new System.Drawing.Size(230,400);form.Controls.Add(constraints);form.Show();Application.DoEvents();((Timer)Get(constraints,"timer")).Stop();((Timer)Get(constraints,"cadTimer")).Stop();var local=All(constraints).OfType<CheckBox>().Single(c=>c.Text.StartsWith("单独设置求解参数"));local.Checked=true;Application.DoEvents();var scroll=tabs.TabPages[0];Check(scroll.VerticalScroll.Visible&&!scroll.HorizontalScroll.Visible,"narrow constraint page scrolls vertically without horizontal clipping");scroll.AutoScrollPosition=new System.Drawing.Point(0,100000);Application.DoEvents();var last=All(constraints).OfType<TextBox>().Last();Check(scroll.PointToClient(last.PointToScreen(new System.Drawing.Point(0,last.Height))).Y<=scroll.ClientSize.Height,"constraint bottom solver fields are reachable");using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"build","constraints-bottom.png"));}form.Controls.Remove(constraints);form.Close();}
  }
  service.Project.actuators.Add(new ActuatorConfig{joint="yaw_joint"});
  var jointDescriptors=new List<JointDescriptor>{new JointDescriptor{name="yaw_joint",parent="base",child="yaw",type="revolute",axis=new[]{0.0,0,1},lower=-1,upper=1},new JointDescriptor{name="pitch_joint",parent="yaw",child="pitch",type="continuous",axis=new[]{1.0,0,0}}};
  using(var editor=new JointEditorControl(service,jointDescriptors)){
   ((Timer)Get(editor,"timer")).Stop();((Timer)Get(editor,"cadTimer")).Stop();
   var selector=(ComboBox)Get(editor,"joint");var current=(JointConfiguration)Get(editor,"current");
   Check(current.damping==null&&current.armature==null&&current.limit_solver==null,"new joint settings preserve inherited physical properties");
   Check(!All(editor).OfType<CheckBox>().Any(c=>c.Text.StartsWith("覆盖")),"ordinary joint fields have no override checkboxes");
   var dampingBox=All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("阻尼 damping"));dampingBox.Text=".02";
   selector.SelectedItem="pitch_joint";var pitch=(JointConfiguration)Get(editor,"current");Check(pitch.damping==null,"different joints do not share damping values");Check(All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("阻尼 damping")).Text=="0.001","undriven joint displays lower default damping");
   selector.SelectedItem="yaw_joint";Check(((JointConfiguration)Get(editor,"current")).damping==.02,"returning to a joint restores its own draft");
   dampingBox=All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("阻尼 damping"));dampingBox.Text="invalid";selector.SelectedItem="pitch_joint";Check(selector.SelectedItem.ToString()=="yaw_joint","invalid joint numbers prevent switching joint");
   dampingBox.Text=".02";
   var mode=All(editor).OfType<ComboBox>().Single(c=>c.Items.Contains("none"));mode.SelectedItem="custom";
   Check(All(editor).OfType<TextBox>().Count(c=>(c.Tag as string??"").StartsWith("限位下限")||(c.Tag as string??"").StartsWith("限位上限"))==2,"custom limits display separate lower and upper inputs");
   var advanced=All(editor).OfType<CheckBox>().Single(c=>c.Text=="自定义限位求解参数");advanced.Checked=true;current=(JointConfiguration)Get(editor,"current");Check(current.limit_solver.timeconst==.003&&current.limit_solver.dmin==.99&&current.limit_solver.dmax==.995,"joint limit override uses harder defaults");
   mode.SelectedItem="none";Check(!LocalVisible(advanced.Parent.Parent.Parent),"no-limit mode hides limit solver controls");
   var spring=All(editor).OfType<ComboBox>().Single(c=>c.Items.Contains("off"));spring.SelectedItem="custom";current=(JointConfiguration)Get(editor,"current");Check(current.stiffness==0&&All(editor).OfType<TextBox>().Any(c=>(c.Tag as string??"").StartsWith("弹簧刚度")),"spring fields appear only when enabled with zero initial stiffness");
   spring=All(editor).OfType<ComboBox>().Single(c=>c.Items.Contains("off"));spring.SelectedItem="off";Check(!LocalVisible(All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("弹簧刚度")).Parent),"disabled spring hides stiffness and equilibrium fields");
   dampingBox.Text="";Check(((JointConfiguration)Get(editor,"current")).damping==null,"blank scalar restores inherited value");dampingBox.Text="0";Check(((JointConfiguration)Get(editor,"current")).damping==0,"explicit zero is an override");dampingBox.Text=".02";
   Check(!All(editor).OfType<TextBox>().Any(c=>(c.Tag as string??"").StartsWith("轴向")||(c.Tag as string??"").StartsWith("位置 m")),"joint position and axis override inputs removed");
   var frictionBox=All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("干摩擦 frictionloss"));var armatureBox=All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("附加惯量 armature"));Check(frictionBox.Text=="0.01"&&armatureBox.Text=="0.001","driven joint shows default friction and armature without persisting override");
   Check(((HashSet<TextBox>)Get(editor,"invalid")).Count==0,"corrected and hidden joint inputs leave no stale validation errors");
   using(var form=new Form()){form.ClientSize=new System.Drawing.Size(230,400);form.Controls.Add(editor);form.Show();Application.DoEvents();((Timer)Get(editor,"timer")).Stop();((Timer)Get(editor,"cadTimer")).Stop();var scroll=(Panel)Get(editor,"details");Check(scroll.VerticalScroll.Visible,"narrow joint panel exposes scrolling");Check(!scroll.HorizontalScroll.Visible,"narrow joint panel does not require horizontal scrolling");scroll.AutoScrollPosition=new System.Drawing.Point(0,600);int before=-scroll.AutoScrollPosition.Y;mode.SelectedItem="custom";advanced.Checked=false;advanced.Checked=true;Application.DoEvents();Check(Object.ReferenceEquals(dampingBox,All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("阻尼 damping"))),"conditional changes retain unrelated controls");Check(-scroll.AutoScrollPosition.Y==before,"conditional changes retain scroll position");scroll.AutoScrollPosition=new System.Drawing.Point(0,100000);Application.DoEvents();var reference=All(editor).OfType<TextBox>().Single(c=>(c.Tag as string??"").StartsWith("参考坐标"));var bottom=scroll.PointToClient(reference.PointToScreen(new System.Drawing.Point(0,reference.Height)));Check(bottom.Y<=scroll.ClientSize.Height,"reference input reachable at bottom of narrow panel");using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"build","joint-bottom.png"));}form.Controls.Remove(editor);form.Close();}
   var serialized=new System.Web.Script.Serialization.JavaScriptSerializer();var saved=serialized.Deserialize<SimulationProject>(serialized.Serialize(Get(editor,"draft")));saved.ValidateSolver();Check(saved.joints.Single(c=>c.joint=="yaw_joint").damping==.02&&saved.joints.Single(c=>c.joint=="pitch_joint").damping==null,"per-joint overrides survive configuration serialization independently");
   Check(service.Project.joints.Count==0,"joint edits remain a draft before save");
  }
 }
 static bool LocalVisible(Control c){return (bool)typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{2});}
 static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in All(c))yield return child;}}
 static Button Find(Control root,string text){foreach(Control c in root.Controls){var b=c as Button;if(b!=null&&b.Text==text)return b;var found=Find(c,text);if(found!=null)return found;}return null;}
}
'@
[CollisionNavigationProbe]::Run()
