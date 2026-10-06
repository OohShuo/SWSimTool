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
   Check(!All(tabs.TabPages[2]).OfType<Label>().Any(c=>Shown(c)&&(c.Text.Contains("kp")||c.Text.Contains("kv"))),"motor hides position and velocity gain fields");
   var kind=All(tabs.TabPages[2]).OfType<ComboBox>().Single(c=>c.Items.Contains("position"));kind.SelectedItem="position";
   Check(All(tabs.TabPages[2]).OfType<Label>().Any(c=>Shown(c)&&c.Text.Contains("kp")),"position actuator displays its gain field");
   kind=All(tabs.TabPages[2]).OfType<ComboBox>().Single(c=>c.Items.Contains("velocity"));kind.SelectedItem="velocity";
   Check(All(tabs.TabPages[2]).OfType<Label>().Any(c=>c.Text.Contains("kv"))&&!All(tabs.TabPages[2]).OfType<Label>().Any(c=>Shown(c)&&c.Text.Contains("kp")),"velocity displays kv only");
   var gearControl=All(tabs.TabPages[2]).OfType<TextBox>().First(c=>c.Text=="1");kind.SelectedItem="motor";kind.SelectedItem="position";Check(Object.ReferenceEquals(gearControl,All(tabs.TabPages[2]).OfType<TextBox>().First(c=>c.Text=="1")),"actuator type switches preserve unrelated input controls");
   tabs.SelectedIndex=1;
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(tabs.TabPages[1],"添加"),new object[]{EventArgs.Empty});
   var sensorType=All(tabs.TabPages[1]).OfType<ComboBox>().Single(c=>c.Items.Contains("camera"));sensorType.SelectedItem="camera";
   Check(All(tabs.TabPages[1]).OfType<Label>().Any(c=>Shown(c)&&c.Text.Contains("视场角"))&&!All(tabs.TabPages[1]).OfType<Label>().Any(c=>Shown(c)&&(c.Text.Contains("裁剪")||c.Text.Contains("noise"))),"camera displays fovy without sensor cutoff or noise note");
   sensorType=All(tabs.TabPages[1]).OfType<ComboBox>().Single(c=>c.Items.Contains("tof"));sensorType.SelectedItem="tof";
   Check(!All(tabs.TabPages[1]).OfType<Label>().Any(c=>Shown(c)&&c.Text.Contains("视场角"))&&All(tabs.TabPages[1]).OfType<Label>().Any(c=>Shown(c)&&c.Text.Contains("裁剪")),"tof displays cutoff without camera fovy");
   Check(!All(tabs.TabPages[1]).OfType<Button>().Any(c=>c.Text.Contains("拾取")),"sensor selects existing sites without CAD pickup");var simulation=(SimulationProject)Get(editor,"draft");var original=simulation.attachments.Single(c=>c.name=="yaw_site");string stableId=original.id;simulation.RenameSite(original,"renamed_yaw");typeof(SimulationEditorControl).GetMethod("UpdateSites",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(editor,null);Check(simulation.sensors[0].site=="renamed_yaw"&&simulation.sensors[0].site_id==stableId,"site rename synchronizes sensor reference without changing identity");Check(All(tabs.TabPages[1]).OfType<ComboBox>().Any(c=>c.Items.Contains("renamed_yaw")),"site choices update in place after rename");simulation.equalities.Add(new EqualityConfig{name="rename_check",site1="renamed_yaw",site2="pitch_site"});simulation.NormalizeSiteReferences();simulation.RenameSite(original,"final_yaw");Check(simulation.equalities[0].site1=="final_yaw"&&simulation.equalities[0].site1_id==stableId,"site rename updates equality endpoint by identity");simulation.RenameSite(original,"renamed_yaw");bool rejected=false;try{simulation.RenameSite(original,"");}catch{rejected=true;}Check(rejected&&original.name=="renamed_yaw","blank site name rejected without changing references");
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
   Check(Object.ReferenceEquals(globalFields,All(tabs.TabPages[3]).OfType<TextBox>().First().Parent.Parent),"solver preset retains existing field controls");
   var simulationDraft=(SimulationProject)Get(editor,"draft");
   Check(simulationDraft.solver.enabled&&simulationDraft.solver.timestep==.001&&simulationDraft.solver.contact.timeconst==.003,"rigid preset sets global and contact defaults");
   using(var form=new Form()){form.ClientSize=new System.Drawing.Size(230,270);form.Controls.Add(editor);form.Show();Application.DoEvents();((Timer)Get(editor,"timer")).Stop();((Timer)Get(editor,"cadTimer")).Stop();foreach(int pageIndex in new[]{0,1,2,3}){tabs.SelectedIndex=pageIndex;Application.DoEvents();var page=tabs.TabPages[pageIndex];page.AutoScrollPosition=new System.Drawing.Point(0,100000);Application.DoEvents();var last=All(page).Where(c=>Shown(c)&&(c is TextBox||c is Label||c is Button)).LastOrDefault();if(last!=null)Check(page.PointToClient(last.PointToScreen(new System.Drawing.Point(0,last.Height))).Y<=page.ClientSize.Height,"simulation tab bottom reachable at 230x270: "+pageIndex);}tabs.SelectedIndex=2;Application.DoEvents();var actuatorPage=tabs.TabPages[2];actuatorPage.AutoScrollPosition=new System.Drawing.Point(0,120);int position=-actuatorPage.AutoScrollPosition.Y;var input=All(actuatorPage).OfType<TextBox>().First();var actuatorType=All(actuatorPage).OfType<ComboBox>().Single(c=>c.Items.Contains("motor"));actuatorType.SelectedItem="velocity";actuatorType.SelectedItem="position";Application.DoEvents();Check(Object.ReferenceEquals(input,All(actuatorPage).OfType<TextBox>().First())&&-actuatorPage.AutoScrollPosition.Y==position,"type changes retain input identity and simulation scroll position");form.Controls.Remove(editor);form.Close();}
   Check(service.Project.solver==null,"preset does not change persisted project before save");
   var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
   var reloaded=serializer.Deserialize<SimulationProject>(serializer.Serialize(simulationDraft));reloaded.ValidateSolver();
   Check(reloaded.solver.contact.margin==.001&&reloaded.solver.noslip_iterations==0,"solver parameters survive configuration serialization");
   var invalidSettings=new ConstraintSettings{timeconst=-1};bool invalidSolver=false;try{invalidSettings.Validate(false);}catch{invalidSolver=true;}Check(invalidSolver,"invalid solver range rejected");

  }
  using(var constraints=new SimulationEditorControl(service,"yaw",new Dictionary<string,string>{{"yaw_joint","yaw"},{"pitch_joint","pitch"}},true)){
   ((Timer)Get(constraints,"timer")).Stop();((Timer)Get(constraints,"cadTimer")).Stop();var tabs=All(constraints).OfType<TabControl>().Single();Check(tabs.TabPages.Count==1&&tabs.TabPages[0].Text=="约束","constraint entry is separate from simulation tabs");typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(constraints,"添加"),new object[]{EventArgs.Empty});var kinds=All(constraints).OfType<ComboBox>().Single(c=>c.Items.Contains("connect"));kinds.SelectedItem="joint";Check(All(constraints).OfType<Label>().Count(c=>c.Text.StartsWith("多项式系数"))==5&&!All(constraints).OfType<ComboBox>().Any(c=>c.Items.Contains("site")),"joint constraint shows five independent coefficients and no site binding");var draft=(SimulationProject)Get(constraints,"draft");Check(draft.equalities[0].polycoef[1]==1&&draft.equalities[0].active,"new joint constraint defaults to active identity coupling");kinds=All(constraints).OfType<ComboBox>().Single(c=>c.Items.Contains("connect"));kinds.SelectedItem="weld";Check(All(constraints).OfType<Label>().Any(c=>c.Text.Contains("torquescale")),"weld shows torque scale");Check(!All(constraints).OfType<Label>().Any(c=>c.Text=="所属 link")&&!All(constraints).OfType<Button>().Any(c=>c.Text.Contains("拾取")),"constraint page has no link selector or pickup");Check(!All(constraints).OfType<ComboBox>().Any(c=>c.Items.Contains("body")),"new constraints choose sites or joints only");Check(service.Project.equalities.Count==0,"constraint edits stay in draft");using(var form=new Form()){form.ClientSize=new System.Drawing.Size(230,400);form.Controls.Add(constraints);form.Show();Application.DoEvents();((Timer)Get(constraints,"timer")).Stop();((Timer)Get(constraints,"cadTimer")).Stop();var local=All(constraints).OfType<CheckBox>().Single(c=>c.Text.StartsWith("单独设置求解参数"));local.Checked=true;Application.DoEvents();var scroll=tabs.TabPages[0];Check(scroll.VerticalScroll.Visible&&!scroll.HorizontalScroll.Visible,"narrow constraint page scrolls vertically without horizontal clipping");scroll.AutoScrollPosition=new System.Drawing.Point(0,100000);Application.DoEvents();var last=All(constraints).OfType<TextBox>().Last();Check(scroll.PointToClient(last.PointToScreen(new System.Drawing.Point(0,last.Height))).Y<=scroll.ClientSize.Height,"constraint bottom solver fields are reachable");using(var bitmap=new System.Drawing.Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,form.Width,form.Height));bitmap.Save(System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"build","constraints-bottom.png"));}form.Controls.Remove(constraints);form.Close();}
  }
  var storageData=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new SimulationStorage.Document{configurations=new Dictionary<string,SimulationStorage.Entry>{{"test",new SimulationStorage.Entry{simulation=new SimulationProject()}}}});var parameter=new Mock<Parameter>();parameter.Setup(p=>p.GetStringValue()).Returns(()=>storageData);bool rejectWrite=false;parameter.Setup(p=>p.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Returns((string data,int option,string name)=>{if(rejectWrite){rejectWrite=false;return false;}storageData=data;return true;});var attribute=new Mock<SolidWorks.Interop.sldworks.Attribute>();attribute.Setup(a=>a.GetName()).Returns(SimulationStorage.NodeName);attribute.Setup(a=>a.GetParameter("data")).Returns(parameter.Object);var feature=new Mock<Feature>();feature.Setup(f=>f.GetTypeName2()).Returns("Attribute");feature.Setup(f=>f.GetSpecificFeature2()).Returns(attribute.Object);var fm=new Mock<FeatureManager>();fm.Setup(f=>f.GetFeatures(true)).Returns(new object[]{feature.Object});model.SetupGet(m=>m.FeatureManager).Returns(fm.Object);int parses=SimulationStorage.ParseCount;var stored=SimulationStorage.Load(model.Object);stored.base_mode="floating";Check(SimulationStorage.Load(model.Object).base_mode=="inherit"&&SimulationStorage.ParseCount==parses+1,"storage cache skips repeated tree validation and isolates mutable drafts");SimulationStorage.Save(null,model.Object,stored);Check(SimulationStorage.Load(model.Object).base_mode=="floating"&&SimulationStorage.ParseCount==parses+1,"storage save preserves verified cache without serialize-parse loops");string previousStorage=storageData;stored.base_mode="fixed";rejectWrite=true;bool saveRejected=false;try{SimulationStorage.Save(null,model.Object,stored);}catch{saveRejected=true;}Check(saveRejected&&storageData==previousStorage&&SimulationStorage.Load(model.Object).base_mode=="floating","failed storage write rolls back and preserves cache");storageData=storageData.Replace("floating","fixed");Check(SimulationStorage.Load(model.Object).base_mode=="fixed"&&SimulationStorage.ParseCount==parses+2,"external attribute edits invalidate storage validation cache");
  service.Project.actuators.Add(new ActuatorConfig{joint="yaw_joint"});
  var jointDescriptors=new List<JointDescriptor>{new JointDescriptor{name="yaw_joint",parent="base",child="yaw",type="revolute",axis=new[]{0.0,0,1},lower=-1,upper=1},new JointDescriptor{name="pitch_joint",parent="yaw",child="pitch",type="continuous",axis=new[]{1.0,0,0}}};
  var identityProject=new SimulationProject();var identitySite=new Attachment{name="identity",link="yaw",type="frame"};identityProject.attachments.Add(identitySite);identityProject.attachments.Add(new Attachment{name="other",link="pitch"});identityProject.sensors.Add(new SensorConfig{name="identity_sensor",site="identity"});identityProject.NormalizeSiteReferences();bool duplicateRejected=false;try{identityProject.RenameSite(identitySite,"other");}catch{duplicateRejected=true;}Check(duplicateRejected&&identitySite.name=="identity","duplicate site rename rejected atomically");identityProject.attachments.Remove(identitySite);identityProject.attachments.Add(new Attachment{name="identity",link="pitch",type="frame"});identityProject.NormalizeSiteReferences();Check(identityProject.sensors[0].site_id==identitySite.id,"deleted site reference is not rebound to new same-name site");
  service.Project.equalities.Add(new EqualityConfig{name="legacy_body",binding="body",body1="yaw",body2="pitch",type="weld",torquescale=.4});using(var legacy=new SimulationEditorControl(service,"yaw",new Dictionary<string,string>{{"yaw_joint","yaw"}},true)){((Timer)Get(legacy,"timer")).Stop();((Timer)Get(legacy,"cadTimer")).Stop();Check(Shown(Find(legacy,"改用 site 定义"))&&!All(legacy).OfType<ComboBox>().Any(c=>c.Items.Contains("body")),"legacy body constraint retained with explicit site migration");typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(legacy,"改用 site 定义"),new object[]{EventArgs.Empty});var legacyDraft=(SimulationProject)Get(legacy,"draft");Check(legacyDraft.equalities[0].binding=="site"&&legacyDraft.equalities[0].torquescale==.4&&legacyDraft.equalities[0].body1=="yaw","explicit migration preserves prior data and solver properties");}service.Project.equalities.Clear();
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
  var geometryCacheTest=new CollisionGeometry{name="cached",link="yaw",definition="manual"};int resolutions=service.GeometryResolutionCount;service.ResolveCollision(geometryCacheTest);service.ResolveCollision(geometryCacheTest);Check(service.GeometryResolutionCount==resolutions+1,"unchanged collision geometry resolves once");geometryCacheTest.size[0]=.12;service.ResolveCollision(geometryCacheTest);Check(service.GeometryResolutionCount==resolutions+2,"changed geometry dimensions invalidate resolution cache");
  int scans=0;var sourceFeature=new Mock<Feature>();sourceFeature.Setup(f=>f.GetTypeName2()).Returns("CoordSys");sourceFeature.SetupGet(f=>f.Name).Returns("cached_coordinate");model.Setup(m=>m.FirstFeature()).Returns(()=>{scans++;return sourceFeature.Object;});service.Sources();service.Sources();Check(scans==1,"source candidates scan once at unchanged CAD revision");string oldRevision=service.CollisionRevision;service.InvalidateCADCache();service.Sources();Check(scans==2&&oldRevision!=service.CollisionRevision&&Get(service,"resolvedGeometry")==null,"explicit CAD refresh invalidates source and geometry caches");model.Setup(m=>m.GetUpdateStamp()).Returns(1);service.Sources();Check(scans==3,"CAD update stamp invalidates source candidates");
 }
 static bool Shown(Control c){for(var item=c;item!=null&&!(item is TabPage)&&!(item is UserControl);item=item.Parent)if(!LocalVisible(item))return false;return true;}
 static bool LocalVisible(Control c){return (bool)typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(c,new object[]{2});}
 static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in All(c))yield return child;}}
 static Button Find(Control root,string text){foreach(Control c in root.Controls){var b=c as Button;if(b!=null&&b.Text==text)return b;var found=Find(c,text);if(found!=null)return found;}return null;}
}
'@
[CollisionNavigationProbe]::Run()
