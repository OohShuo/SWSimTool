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
  service.Project.attachments.AddRange(new[]{new Attachment{name="yaw_site",link="yaw",type="frame"},new Attachment{name="pitch_site",link="pitch",type="point"}});
  using(var editor=new SimulationEditorControl(service,"yaw",new Dictionary<string,string>{{"yaw_joint","yaw"},{"pitch_joint","pitch"}})){
   ((Timer)Get(editor,"timer")).Stop();((Timer)Get(editor,"cadTimer")).Stop();
   var link=(ComboBox)Get(editor,"link");var tabs=All(editor).OfType<TabControl>().Single();
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
   Check(service.Project.actuators.Count==0,"simulation edits remain a draft until save");
   typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Find(editor,"应用刚性机器人预设"),new object[]{EventArgs.Empty});
   var simulationDraft=(SimulationProject)Get(editor,"draft");
   Check(simulationDraft.solver.enabled&&simulationDraft.solver.timestep==.001&&simulationDraft.solver.contact.timeconst==.003,"rigid preset sets global and contact defaults");
   Check(service.Project.solver==null,"preset does not change persisted project before save");
   var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
   var reloaded=serializer.Deserialize<SimulationProject>(serializer.Serialize(simulationDraft));reloaded.ValidateSolver();
   Check(reloaded.solver.contact.margin==.001&&reloaded.solver.noslip_iterations==0,"solver parameters survive configuration serialization");
   var invalidSettings=new ConstraintSettings{timeconst=-1};bool invalidSolver=false;try{invalidSettings.Validate(false);}catch{invalidSolver=true;}Check(invalidSolver,"invalid solver range rejected");

  }
 }
 static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in All(c))yield return child;}}
 static Button Find(Control root,string text){foreach(Control c in root.Controls){var b=c as Button;if(b!=null&&b.Text==text)return b;var found=Find(c,text);if(found!=null)return found;}return null;}
}
'@
[CollisionNavigationProbe]::Run()
