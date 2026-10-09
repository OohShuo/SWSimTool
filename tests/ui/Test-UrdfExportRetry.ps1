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
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/log4net.dll","$bin/MathNet.Numerics.dll",$moq,$castle,$tasks,"$sdk/SolidWorks.Interop.sldworks.dll",'System.Core','System.Xml','System.Runtime.Serialization','System.Windows.Forms','System.Drawing')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.Collections.Generic;using System.Reflection;using System.Runtime.Serialization;using System.IO;using System.Xml;using System.Windows.Forms;using Moq;using SolidWorks.Interop.sldworks;using SWSimTool.UI;using SWSimTool.URDF;using SWSimTool.URDFExport;
public static class UrdfExportRetryTest {
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static T Field<T>(AssemblyExportForm form,string name){return (T)typeof(AssemblyExportForm).GetField(name,flags).GetValue(form);}
 static object Call(AssemblyExportForm form,string name,params object[] args){return typeof(AssemblyExportForm).GetMethod(name,flags).Invoke(form,args);}
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static void Text(AssemblyExportForm f,string name,string text){Field<TextBox>(f,name).Text=text;}
 static bool Next(AssemblyExportForm f,out string errors){object[] args={null};bool ok=(bool)Call(f,"TryAdvanceJointPage",args);errors=(string)args[0];return ok;}
 static AssemblyExportForm Fixture(int count){
  var doc=new Mock<ModelDoc2>();var manager=new Mock<SelectionMgr>();var data=new Mock<SelectData>();manager.Setup(x=>x.CreateSelectData()).Returns(data.Object);doc.SetupGet(x=>x.SelectionManager).Returns(manager.Object);
  var app=new Mock<SldWorks>();app.SetupGet(x=>x.ActiveDoc).Returns(doc.Object);
  var root=new Link(null);root.Name="base";
  for(int i=0;i<count;i++){var child=new Link(root);child.Name=i==0?"yaw":"pitch";child.Joint.Name=child.Name+"_joint";child.Joint.Type="revolute";child.Joint.Parent.Name="base";child.Joint.Child.Name=child.Name;child.Joint.Axis.SetXYZ(new[]{0.0,0,-1});child.Joint.Origin.SetXYZ(new[]{0.0,0,.019});child.Joint.Limit.SetInputs("-1",i==0?null:"1",null,"20.94");root.Children.Add(child);}
  var helper=(ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));helper.ActiveSWModel=doc.Object;helper.URDFRobot=new Robot();helper.URDFRobot.Name="fixture";helper.URDFRobot.SetBaseLink(root);
  typeof(ExportHelper).GetField("ReferenceCoordinateSystemNames",flags).SetValue(helper,new List<string>());
  typeof(ExportHelper).GetField("ReferenceAxesNames",flags).SetValue(helper,new List<string>());
  var form=new AssemblyExportForm(app.Object,new LinkNode(root),helper);
  var tree=Field<TreeView>(form,"treeViewJointTree");var handle=tree.Handle;form.FillJointTree();tree.SelectedNode=tree.Nodes[0];
  Check(Field<LinkNode>(form,"previouslySelectedNode")==tree.SelectedNode,"Fixture must select through real AfterSelect callback");
  return form;
 }
 static string Xml(Joint joint){var output=new StringWriter();using(var writer=XmlWriter.Create(output,new XmlWriterSettings{OmitXmlDeclaration=true})){joint.WriteURDF(writer);}return output.ToString();}
 public static void Run(){
  using(var f=Fixture(1)){
   var tree=Field<TreeView>(f,"treeViewJointTree");var node=(LinkNode)tree.SelectedNode;string error;
   for(int i=0;i<3;i++){Check(!Next(f,out error)&&error.Contains("yaw_joint")&&error.Contains("limit.upper"),"Missing upper bound must fail with field diagnostic");Check(Field<LinkNode>(f,"previouslySelectedNode")==node&&tree.SelectedNode==node,"Failed Next lost editor/selection");Check(Field<TextBox>(f,"textBoxLimitVelocity").Text=="20.94","Failed Next discarded input");}
   Text(f,"textBoxLimitEffort","7");Text(f,"textBoxLimitVelocity","");Check(!Next(f,out error)&&error.Contains("limit.upper"),"Repeated failure after modifying another field must save current input");Check(node.Link.Joint.Limit.Effort==7,"Effort input did not reach model after failed Next");
   Text(f,"textBoxLimitUpper","1");Text(f,"textBoxLimitVelocity","20.94");Text(f,"textBoxJointName","yaw_updated");Text(f,"textBoxJointZ","0.03");Text(f,"textBoxDamping","0.02");
   Check(Next(f,out error),"Retry without selection change failed: "+error);Check(Field<LinkNode>(f,"previouslySelectedNode")==null,"Successful page transition kept old joint editor");
   var xml=new XmlDocument();xml.LoadXml(Xml(node.Link.Joint));Check(xml.DocumentElement.GetAttribute("name")=="yaw_updated","Joint rename not exported");Check(xml.SelectSingleNode("/joint/limit").Attributes["effort"].Value=="7","Exported URDF lost new effort");Check(xml.SelectSingleNode("/joint/origin").Attributes["xyz"].Value=="0 0 0.03","Exported URDF lost new origin");Check(xml.SelectSingleNode("/joint/dynamics").Attributes["damping"].Value=="0.02","Exported URDF lost new dynamics");
   Call(f,"ButtonLinksPreviousClick",null,EventArgs.Empty);Check(Field<LinkNode>(f,"previouslySelectedNode")==tree.SelectedNode,"Previous did not restore active joint editor");Text(f,"textBoxLimitEffort","9");Check(Next(f,out error),"Next after Previous failed: "+error);Check(node.Link.Joint.Limit.Effort==9,"Returned page lost edits");
   Console.WriteLine("PASS: repeated failed Next, direct retry, other fields, URDF values and Previous");
  }
  using(var f=Fixture(2)){
   var tree=Field<TreeView>(f,"treeViewJointTree");var first=(LinkNode)tree.Nodes[0];string error;Check(!Next(f,out error),"Missing effort should fail");Text(f,"textBoxLimitUpper","1");Text(f,"textBoxLimitEffort","8");tree.SelectedNode=tree.Nodes[1];Check(first.Link.Joint.Limit.Effort==8,"Joint switch failed to flush previous edits");tree.SelectedNode=first;Check(Field<TextBox>(f,"textBoxLimitEffort").Text=="8","Switch back lost input");Check(Next(f,out error),"Next after switching failed: "+error);Console.WriteLine("PASS: joint switch commits to the correct joint");
  }
  using(var f=Fixture(1)){
   typeof(AssemblyExportForm).GetField("previouslySelectedNode",flags).SetValue(f,null);Text(f,"textBoxLimitEffort","7");string error;Check(!Next(f,out error)&&!string.IsNullOrWhiteSpace(error),"Missing editor silently skipped save");var node=(LinkNode)Field<TreeView>(f,"treeViewJointTree").SelectedNode;Check(node.Link.Joint.GetMissingRequiredFields().Contains("limit.upper"),"Missing-editor path modified model");Console.WriteLine("PASS: missing editor is diagnosed without a silent save skip");
  }
 }
}
'@
try{[UrdfExportRetryTest]::Run()}catch{Write-Host $_.Exception.ToString();throw}
