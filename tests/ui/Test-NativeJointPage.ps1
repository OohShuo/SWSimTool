param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$sdk=$env:SOLIDWORKS_DIR
if(!$sdk){$sdk='D:/sw/sw2025/SOLIDWORKS'}
$packageRoot=$env:NUGET_PACKAGES
if(!$packageRoot){$packageRoot=Join-Path $env:USERPROFILE '.nuget/packages'}
$moq=Get-ChildItem "$packageRoot/moq/*/lib/net45/Moq.dll" | Select-Object -First 1 -ExpandProperty FullName
$castle=Get-ChildItem "$packageRoot/castle.core/*/lib/net45/Castle.Core.dll" | Select-Object -First 1 -ExpandProperty FullName
$tasks=Get-ChildItem "$packageRoot/system.threading.tasks.extensions/*/lib/portable-net45*/System.Threading.Tasks.Extensions.dll" | Select-Object -First 1 -ExpandProperty FullName
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/log4net.dll","$bin/MathNet.Numerics.dll",$moq,$castle,$tasks,"$sdk/SolidWorks.Interop.sldworks.dll","$sdk/SolidWorks.Interop.swconst.dll",'System.Core','System.Xml','System.Web.Extensions','System.Runtime.Serialization','System.Windows.Forms','System.Drawing')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Runtime.Serialization;using Moq;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;using SWSimTool.UI;using SWSimTool.Simulation;using SWSimTool.URDF;using SWSimTool.URDFExport;
public static class NativeJointPageTest {
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static object Field(NativeParameterFields fields,string key){return ((IEnumerable)typeof(NativeParameterFields).GetField("fields",flags).GetValue(fields)).Cast<object>().Single(x=>(string)x.GetType().GetField("Key").GetValue(x)==key);}
 static int Id(NativeParameterFields fields,string key){var f=Field(fields,key);return (int)f.GetType().GetField("Id").GetValue(f);}
 static IPropertyManagerPageControl Control(NativeParameterFields fields,string key){var f=Field(fields,key);return (IPropertyManagerPageControl)f.GetType().GetField("Control").GetValue(f);}
 static void Text(NativeParameterFields fields,string key,string value){fields.OnText(Id(fields,key),value);}
 static void Choice(NativeParameterFields fields,string key,int value){((IPropertyManagerPageCombobox)Control(fields,key)).CurrentSelection=(short)value;fields.OnChoice(Id(fields,key),value);}
 static void Toggle(NativeParameterFields fields,string key,bool value){fields.OnCheck(Id(fields,key),value);}
 static Mock<IPropertyManagerPageGroup> Group(out HashSet<int> ids){var allocated=new HashSet<int>();ids=allocated;var group=new Mock<IPropertyManagerPageGroup>();group.Setup(g=>g.AddControl2(It.IsAny<int>(),It.IsAny<short>(),It.IsAny<string>(),It.IsAny<short>(),It.IsAny<int>(),It.IsAny<string>())).Returns((int id,short type,string caption,short align,int options,string tip)=>{
  Check(allocated.Add(id),"Native IDs must be unique");
  switch((swPropertyManagerPageControlType_e)type){
   case swPropertyManagerPageControlType_e.swControlType_Label:{var m=new Mock<IPropertyManagerPageLabel>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
   case swPropertyManagerPageControlType_e.swControlType_Textbox:{var m=new Mock<IPropertyManagerPageTextbox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
   case swPropertyManagerPageControlType_e.swControlType_Combobox:{var m=new Mock<IPropertyManagerPageCombobox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
   case swPropertyManagerPageControlType_e.swControlType_Checkbox:{var m=new Mock<IPropertyManagerPageCheckbox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
   case swPropertyManagerPageControlType_e.swControlType_Button:{var m=new Mock<IPropertyManagerPageButton>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
   default:throw new Exception("Joint page requested a non-native control");
  }
 });return group;}
 public static void Run(){
  var config=new Mock<Configuration>();config.SetupGet(c=>c.Name).Returns("test");var cm=new Mock<ConfigurationManager>();cm.SetupGet(c=>c.ActiveConfiguration).Returns(config.Object);
  var model=new Mock<ModelDoc2>();model.SetupGet(m=>m.ConfigurationManager).Returns(cm.Object);model.Setup(m=>m.GetPathName()).Returns("isolated-native.SLDASM");
  var extension=new Mock<ModelDocExtension>();extension.Setup(e=>e.GetPersistReference3(It.IsAny<object>())).Returns(new byte[]{4,5,6});model.SetupGet(m=>m.Extension).Returns(extension.Object);
  string raw=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new SimulationStorage.Document{configurations=new Dictionary<string,SimulationStorage.Entry>{{"test",new SimulationStorage.Entry{simulation=new SimulationProject()}}}});
  bool fail=false;var parameter=new Mock<Parameter>();parameter.Setup(p=>p.GetStringValue()).Returns(()=>raw);parameter.Setup(p=>p.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Returns((string value,int scope,string name)=>{if(fail){fail=false;return false;}raw=value;return true;});
  var attribute=new Mock<SolidWorks.Interop.sldworks.Attribute>();attribute.Setup(a=>a.GetName()).Returns(SimulationStorage.NodeName);attribute.Setup(a=>a.GetParameter("data")).Returns(parameter.Object);
  var feature=new Mock<Feature>();feature.Setup(f=>f.GetTypeName2()).Returns("Attribute");feature.Setup(f=>f.GetSpecificFeature2()).Returns(attribute.Object);
  var fm=new Mock<FeatureManager>();fm.Setup(f=>f.GetFeatures(true)).Returns(new object[]{feature.Object});model.SetupGet(m=>m.FeatureManager).Returns(fm.Object);
  var helper=(ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));helper.ActiveSWModel=model.Object;
  var service=new AttachmentService(helper);var root=new Link(null){Name="base"};foreach(var name in new[]{"yaw","pitch","fixed"}){var child=new Link(root){Name=name};child.Joint.Name=name+"_joint";child.Joint.Type=name=="fixed"?"fixed":"revolute";child.Joint.Parent.Name="base";child.Joint.Child.Name=name;child.Joint.Limit.SetInputs("-1","1",null,null);root.Children.Add(child);}service.DraftTree=root;
  var descriptors=JointDescriptor.FromTree(new LinkNode(root));service.Project.actuators.Add(new ActuatorConfig{joint="yaw_joint",joint_id=descriptors[0].id,name="yaw_act"});
  HashSet<int> ids;var group=Group(out ids);string baseline=raw;
  using(var editor=new NativeJointEditor(service,descriptors,group.Object)){
   var fields=editor.Fields;int count=ids.Count;var savedControl=Control(fields,"damping");
   Check(((IPropertyManagerPageTextbox)savedControl).Text=="0.01","Driven joint default damping");
   Check(!Control(fields,"lower").Visible&&!Control(fields,"limit-timeconst").Visible,"Conditional fields must start hidden");
   var dampingBox=(IPropertyManagerPageTextbox)savedControl;string rawDamping=dampingBox.Text;int callbacks=0;Mock.Get(dampingBox).SetupGet(b=>b.Text).Returns(()=>rawDamping);Mock.Get(dampingBox).SetupSet(b=>b.Text=It.IsAny<string>()).Callback<string>(value=>{rawDamping=value;if(++callbacks>100)throw new Exception("Native textbox callback recursion");fields.OnText(Id(fields,"damping"),value);});
   Text(fields,"damping","0.02");Check(callbacks==1,"Native writes must suppress callback reentry");Choice(fields,"limit-mode",2);Check(Control(fields,"lower").Visible,"Custom bounds not visible");
   Toggle(fields,"limit-local",true);Text(fields,"limit-timeconst","0.002");Toggle(fields,"limit-local",false);Toggle(fields,"limit-local",true);
   Check(((IPropertyManagerPageTextbox)Control(fields,"limit-timeconst")).Text=="0.002","Solver toggle lost input");
   Choice(fields,"spring-mode",2);Text(fields,"stiffness","15");Text(fields,"springref","0.3");
   Text(fields,"damping","oops");bool blocked=false;try{Choice(fields,"joint",1);}catch(InvalidOperationException){blocked=true;}
   Check(blocked&&((IPropertyManagerPageTextbox)Control(fields,"damping")).Text=="oops","Invalid switch must retain raw input");
   Text(fields,"damping","0.02");Choice(fields,"joint",1);Check(((IPropertyManagerPageTextbox)Control(fields,"damping")).Text=="0.001","Undriven defaults differ");
   Text(fields,"frictionloss","0");Check(!Control(fields,"friction-local").Visible,"Friction zero must hide solver override");
   Choice(fields,"joint",0);Check(((IPropertyManagerPageTextbox)Control(fields,"damping")).Text=="0.02","Switch back lost independent edits");
   Check(ids.Count==count&&Object.ReferenceEquals(savedControl,Control(fields,"damping")),"Type/entity changes must not recreate native controls");
   Check(service.Project.joints[0].stiffness==15&&service.Project.joints[0].springref==.3,"Spring fields lost model values");
   Choice(fields,"type",1);fields.Refresh();Check(service.Project.joints[0].type=="hinge"&&((IPropertyManagerPageCombobox)Control(fields,"type")).CurrentSelection==1,"Second native type did not persist in draft");
   Choice(fields,"type",2);fields.Refresh();Check(service.Project.joints[0].type=="slide"&&((IPropertyManagerPageCombobox)Control(fields,"type")).CurrentSelection==2,"Third native type snapped to first");
   service.PageDraft.Collect();Check(raw==baseline,"Collect wrote persistent data");
   fail=true;bool saveFailed=false;try{editor.Save();}catch{saveFailed=true;}Check(saveFailed&&raw==baseline&&service.Project.joints[0].damping==.02,"Failed save changed old storage or lost input");
   var result=editor.Save();Check(result.Committed&&raw!=baseline,"Valid native page save failed");
   string committed=raw;Choice(fields,"type",1);Text(fields,"damping","0.03");Check(raw==committed,"Unconfirmed input leaked to persistence");
  }
  Check(service.Editing.Project.joints.First(j=>j.joint=="yaw_joint").damping==.02&&service.Editing.Project.joints.First(j=>j.joint=="yaw_joint").type=="slide","Cancel after saved edit changed confirmed draft");
  service=new AttachmentService(helper);using(var editor=new NativeJointEditor(service,descriptors,Group(out ids).Object)){
   Check(service.Project.joints.First(j=>j.joint=="yaw_joint").damping==.02&&service.Project.joints.First(j=>j.joint=="yaw_joint").type=="slide"&&((IPropertyManagerPageCombobox)Control(editor.Fields,"type")).CurrentSelection==2,"Reopen lost saved third-item native selection");
   Choice(editor.Fields,"joint",2);Check(!Control(editor.Fields,"damping").Visible,"Fixed joint must hide physical overrides");
   ConfigurationSession.Invalidate(model.Object);bool stale=false;try{Text(editor.Fields,"damping","1");}catch{stale=true;}Check(stale,"Stale native callbacks accepted input");
  }
  Check(ConfigurationPageDraft.Active(model.Object)==null,"Native page leaked ownership");
  Console.WriteLine("PASS: native joint fields, defaults, conditionals, toggles, independent joints, raw invalid input, save failure/retry, cancel, reopen and stale callbacks");
 }
}
'@
[NativeJointPageTest]::Run()
