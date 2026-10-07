param([string]$Payload='native-candidate')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$bin=Join-Path $root ('build\'+$Payload)
$refs=@("$bin\SW2URDF.dll",'System.Xml','System.Xml.Linq','System.Core','System.Runtime.Serialization','System.Windows.Forms')
[Reflection.Assembly]::LoadFrom("$bin\SW2URDF.dll") | Out-Null
# Pure resolved-data adapter test. Does not instantiate SolidWorks or inspect documents.
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using SW2URDF.URDF;
using SW2URDF.RobotModel;
using SW2URDF.Simulation;
public static class ResolvedCadModelProbe {
 static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
 public static void Run(){
  var robot=new Robot();robot.Name="pure_resolved_cad";var root=robot.BaseLink;root.Name="base";
  root.Inertial.Mass.Value=1;root.Inertial.Inertia.Ixx=.01;root.Inertial.Inertia.Iyy=.01;root.Inertial.Inertia.Izz=.01;
  var child=new Link(root);child.Name="arm";root.Children.Add(child);child.Inertial.Mass.Value=2;child.Inertial.Inertia.Ixx=.02;child.Inertial.Inertia.Iyy=.03;child.Inertial.Inertia.Izz=.04;
  child.Joint.Name="slide";child.Joint.Type="prismatic";child.Joint.Origin.SetXYZ(new[]{1.0,2,3});child.Joint.Axis.SetXYZ(new[]{2.0,0,0});child.Joint.Limit.Lower=-1;child.Joint.Limit.Upper=1;
  var tree=new LinkNode(root);var project=new SimulationProject();project.attachments.Add(new Attachment{name="frame",link="arm",type="frame"});project.actuators.Add(new ActuatorConfig{joint="slide"});project.collision=new CollisionConfiguration();project.collision.link_modes["arm"]="primitive";
  StableReferences.Normalize(project,tree);string linkId=project.attachments[0].link_id,jointId=project.actuators[0].joint_id;
  var copy=root.Clone();Check(copy.StableId==root.StableId&&copy.Children[0].StableId==linkId&&copy.Children[0].Joint.StableId==jointId,"clone retains link and joint identities");Check(Object.ReferenceEquals(copy.Children[0].Parent,copy),"cloned ancestry points into the new tree");
  using(var stream=new MemoryStream()){var serializer=new DataContractSerializer(typeof(Link));serializer.WriteObject(stream,root);stream.Position=0;var loaded=(Link)serializer.ReadObject(stream);Check(loaded.Clone().Children[0].StableId==linkId&&loaded.Clone().Children[0].Joint.StableId==jointId,"persistent identities survive serialization and reload");}
  copy.Children[0].Name="renamed_arm";copy.Children[0].Joint.Name="renamed_slide";var renamed=new LinkNode(copy);StableReferences.RemapLinkModesByStableId(project);StableReferences.Normalize(project,renamed);
  Check(project.attachments[0].link=="renamed_arm"&&project.attachments[0].link_id==linkId&&project.actuators[0].joint=="renamed_slide"&&project.actuators[0].joint_id==jointId&&project.collision.Mode(linkId)=="primitive","rename updates bindings and collision modes without changing identity");
  project.actuators[0].joint="slide";copy.Children.Add(new Link(copy));copy.Children[1].Name="other";copy.Children[1].Joint.Name="slide";var conflicting=new LinkNode(copy);StableReferences.Normalize(project,conflicting);
  Check(project.actuators[0].joint_id==jointId&&project.actuators[0].joint=="renamed_slide","existing joint ID wins over a conflicting editable name");copy.Children.RemoveAt(1);
  var sources=new Dictionary<string,MeshSource>{{"base",new MeshSource("base_mesh","base.stl",new Vector3d(1,1,1))},{"arm",new MeshSource("arm_mesh","arm.stl",new Vector3d(1,1,1))}};
  int g=ExportInstrumentation.GeometryQueries,s=ExportInstrumentation.StlExports;
  var core=SolidWorksRobotModelBuilder.FromResolvedRobot(robot,sources);Check(core.Links.Count==2&&core.Joints.Count==1,"resolved CAD topology extracted directly without URDF serialization");
  Check(core.Joints[0].AxisInJointFrame.X==1&&core.Joints[0].ParentLinkFromJoint.Translation.Y==2,"resolved CAD frame and axis retained");
  var incomplete=child.Clone();incomplete.Parent=root;incomplete.Name="incomplete";incomplete.Joint=new Joint();incomplete.Joint.Name="missing_range";incomplete.Joint.Type="revolute";incomplete.Joint.Axis.SetXYZ(new[]{0.0,0,1});root.Children.Add(incomplete);
  try{SolidWorksRobotModelBuilder.FromResolvedRobot(robot,sources);throw new Exception("Missing range accepted");}catch(InvalidDataException error){Check(error.Message.Contains("missing_range")&&error.Message.Contains("URDF"),"missing CAD limit reports the joint and corrective action instead of a null reference");}finally{root.Children.Remove(incomplete);}
  child.Joint.Origin.Y=99;child.Inertial.Mass.Value=99;root.Children.Clear();sources.Clear();Check(core.Links.Count==2&&core.Links[1].Inertial.Mass==2&&core.Joints[0].ParentLinkFromJoint.Translation.Y==2,"resolved snapshot does not retain mutable CAD/URDF objects");
  Check(ExportInstrumentation.GeometryQueries==g&&ExportInstrumentation.StlExports==s,"resolved model adapter makes zero CAD queries and STL exports");
 }
}
'@
[ResolvedCadModelProbe]::Run()
