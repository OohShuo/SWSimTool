param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48',[string]$Python='python')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll",'System.Core','System.Xml','System.Xml.Linq','System.Runtime.Serialization','System.Windows.Forms')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.Collections.Generic;using System.IO;using System.Xml;using System.Xml.Linq;using SWSimTool.RobotModel;using SWSimTool.URDF;
public static class UrdfLimitMappingTest {
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static string Xml(Joint joint){var output=new StringWriter();using(var writer=XmlWriter.Create(output,new XmlWriterSettings{OmitXmlDeclaration=true})){joint.WriteURDF(writer);}return output.ToString();}
 public static void Run(string folder){
  var robot=new Robot();robot.Name="limits";var root=robot.BaseLink;root.Name="base";var child=new Link(root);child.Name="arm";root.Children.Add(child);
  foreach(var link in new[]{root,child}){link.Inertial.Mass.Value=1;link.Inertial.Inertia.Ixx=.01;link.Inertial.Inertia.Iyy=.01;link.Inertial.Inertia.Izz=.01;}
  var joint=child.Joint;joint.Name="yaw_joint";joint.Type="continuous";joint.Parent.Name="base";joint.Child.Name="arm";joint.Axis.SetXYZ(new[]{0.0,0,1});
  foreach(string type in new[]{"continuous","revolute","prismatic"}){
   joint.Type=type;
   foreach(string effort in new[]{null,"0","7"}){
    joint.Limit.SetInputs(type=="continuous"?null:"-1",type=="continuous"?null:"1",effort,"6.28");
    Check(joint.AreRequiredFieldsSatisfied(),"Optional effort rejected: "+type);Check(joint.GetMissingRequiredFields().Count==0,"Optional effort reported missing");
    string before=Xml(joint);var core=SolidWorksRobotModelBuilder.FromResolvedRobot(robot,new Dictionary<string,MeshSource>());
    var candidate=MjcfExporter.Generate(new RobotModel(core,new SimulationConfigSnapshot()),new PreparedAssets(new PreparedMeshAsset[0]),new ExportContext("limits"));
    var mj=XDocument.Parse(candidate);var node=mj.Descendants("joint").First();
    Check((string)node.Attribute("limited")== (type=="continuous"?"false":"true"),"Position limit not carried from tree");
    Check(type=="continuous"?node.Attribute("range")==null:(string)node.Attribute("range")=="-1 1","Incorrect MJCF range");
    Check(effort=="7"?(string)node.Attribute("actuatorfrcrange")=="-7 7":node.Attribute("actuatorfrcrange")==null,"Effort clamping mismatch");
    Check(before==Xml(joint),"Model building or export mutated the joint");
    File.WriteAllText(Path.Combine(folder,type+"_"+(effort??"blank")+".xml"),candidate);
   }
  }
  joint.Type="continuous";joint.Limit.SetInputs(null,null,null,null);Check(joint.AreRequiredFieldsSatisfied(),"Entire optional limit group rejected");Check(!Xml(joint).Contains("<limit"),"Empty optional group should be omitted");
  joint.Limit.SetInputs(null,null,"7",null);Check(joint.AreRequiredFieldsSatisfied(),"Optional velocity rejected");Check(Xml(joint).Contains("velocity=\"0\""),"Legacy URDF velocity placeholder lost");
  var boxes=new[]{new System.Windows.Forms.TextBox(),new System.Windows.Forms.TextBox(),new System.Windows.Forms.TextBox(),new System.Windows.Forms.TextBox()};joint.Limit.FillBoxes(boxes[0],boxes[1],boxes[2],boxes[3],"G17");Check(boxes[3].Text=="","Export filled blank UI value");foreach(var box in boxes)box.Dispose();
  Console.WriteLine("PASS: optional limits, legacy URDF placeholders, immutable export and tree-to-MJCF position/effort mapping");
 }
}
'@.Replace('using System;','using System;using System.Linq;')
$folder=Join-Path $root ('build/test-work/limit-mapping-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder -Force | Out-Null
[UrdfLimitMappingTest]::Run($folder)
& $Python -B "$root/tests/model/verify_limit_mapping.py" $folder
if($LASTEXITCODE -ne 0){throw 'MuJoCo limit mapping validation failed'}
