param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48',[switch]$ExpectKnownFailure)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$sdk='D:/sw/sw2025/SOLIDWORKS'
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/MathNet.Numerics.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Moq.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Castle.Core.dll","$sdk/SolidWorks.Interop.sldworks.dll",'System.Core','System.Xml','System.Runtime.Serialization','System.Web.Extensions','System.Windows.Forms')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Reflection;using System.Runtime.Serialization;using System.Linq;using Moq;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.URDF;using SWSimTool.URDFExport;
public static class ConfigurationRebuildTest {
 public static void Run(bool knownFailure) {
  var c=new Mock<Configuration>();c.SetupGet(x=>x.Name).Returns("test");var cm=new Mock<ConfigurationManager>();cm.SetupGet(x=>x.ActiveConfiguration).Returns(c.Object);
  string raw=null;bool present=true;
  var p=new Mock<Parameter>();p.Setup(x=>x.GetStringValue()).Returns(()=>raw);p.Setup(x=>x.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Callback<string,int,string>((v,s,n)=>raw=v).Returns(true);
  var a=new Mock<SolidWorks.Interop.sldworks.Attribute>();a.Setup(x=>x.GetName()).Returns(SimulationStorage.NodeName);a.Setup(x=>x.GetParameter("data")).Returns(p.Object);
  var f=new Mock<Feature>();f.Setup(x=>x.GetTypeName2()).Returns("Attribute");f.Setup(x=>x.GetSpecificFeature2()).Returns(a.Object);
  var fm=new Mock<FeatureManager>();fm.Setup(x=>x.GetFeatures(true)).Returns(()=>present?new object[]{f.Object}:new object[0]);
  var m=new Mock<ModelDoc2>();m.SetupGet(x=>x.FeatureManager).Returns(fm.Object);m.SetupGet(x=>x.ConfigurationManager).Returns(cm.Object);m.Setup(x=>x.GetConfigurationNames()).Returns(new[]{"test"});m.Setup(x=>x.GetConfigurationByName("test")).Returns(c.Object);m.Setup(x=>x.GetPathName()).Returns("isolated.SLDASM");
  Func<LinkNode> tree=()=>{var r=new Link(null);r.Name="base";var n=new LinkNode(r);var l=new Link(r);l.Name="pitch1";l.Joint.Name="pitch1_joint";n.Nodes.Add(new LinkNode(l));return n;};
  var oldTree=tree();var oldProject=new SimulationProject();oldProject.attachments.Add(new Attachment{name="site_pitch1",link="pitch1",type="point"});StableReferences.Normalize(oldProject,oldTree);
  var d=new SimulationStorage.Document();d.configurations["test"]=new SimulationStorage.Entry{urdf_xml=ConfigurationSerialization.WriteTree(oldTree),simulation=oldProject};raw=ExportFingerprint.Serializer().Serialize(d);
  var helper=(ExportHelper)FormatterServices.GetUninitializedObject(typeof(ExportHelper));helper.ActiveSWModel=m.Object;
  var oldPage=new AttachmentService(helper);
  present=false;if(SimulationStorage.Load(m.Object)!=null)throw new Exception("Deleted node still loaded");
  var newTree=tree();d=new SimulationStorage.Document();d.configurations["test"]=new SimulationStorage.Entry{urdf_xml=ConfigurationSerialization.WriteTree(newTree),simulation=new SimulationProject()};present=true;raw=ExportFingerprint.Serializer().Serialize(d);
  var savedBefore=raw;bool rejected=false;try{oldPage.Save();}catch(InvalidDataException){rejected=true;}
  var now=SimulationStorage.Load(m.Object);bool contaminated=now.attachments.Any(x=>x.link_id==oldProject.attachments[0].link_id);
  if(knownFailure){if(!contaminated||rejected)throw new Exception("Known failure did not reproduce");Console.WriteLine("REPRODUCED: old page writes deleted link ID into rebuilt configuration");}
  else {if(!rejected||raw!=savedBefore||contaminated)throw new Exception("Old page contaminated replacement node");Console.WriteLine("PASS: stale save rejected without modifying replacement configuration");}
  if(now.attachments.Count>0){if(StableReferences.LinkNames(newTree).ContainsKey(now.attachments[0].link_id))throw new Exception("Dangling ID unexpectedly matched");Console.WriteLine("Unknown stable reference: "+now.attachments[0].link_id+" (site_pitch1 parent link pitch1)");}
  if(!knownFailure){
   var live=ConfigurationSession.Capture(null,m.Object);live.RequireCurrent();
   var opened=SimulationStorage.LoadEntry(m.Object);var stable=ConfigurationSerialization.ReadTree(opened.urdf_xml,1.4).Link.StableId;
   SimulationStorage.Save(null,m.Object,now);var reopened=SimulationStorage.LoadEntry(m.Object);
   if(reopened.instance_id!=opened.instance_id||ConfigurationSerialization.ReadTree(reopened.urdf_xml,1.4).Link.StableId!=stable)throw new Exception("Normal save changed identity");
   bool invalid=false;try{live.RequireCurrent();}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Previous revision still valid");
   live=ConfigurationSession.Capture(null,m.Object);ConfigurationSession.Invalidate(m.Object);invalid=false;try{live.RequireCurrent();}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Switch event did not invalidate lease");
   var resetDocument=ExportFingerprint.Serializer().Deserialize<SimulationStorage.Document>(raw);resetDocument.configurations["other"]=new SimulationStorage.Entry{configuration_id="other-id",configuration_name="other",simulation=new SimulationProject()};raw=ExportFingerprint.Serializer().Serialize(resetDocument);
   live=ConfigurationSession.Capture(null,m.Object);var backup=SimulationStorage.ResetCurrent(null,m.Object,live,Path.Combine(Directory.GetCurrentDirectory(),"build/test-work/reset-backups"));
   if(SimulationStorage.Load(m.Object)!=null||!raw.Contains("other-id"))throw new Exception("Reset changed other configuration or retained target");
   invalid=false;try{live.RequireCurrent();}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Reset kept old page valid");
   var restore=ConfigurationSession.Capture(null,m.Object);SimulationStorage.RestoreCurrent(null,m.Object,restore,backup,Path.Combine(Directory.GetCurrentDirectory(),"build/test-work/reset-backups"));
   if(SimulationStorage.LoadEntry(m.Object).instance_id==reopened.instance_id||!raw.Contains("other-id"))throw new Exception("Restore reused old session instance or removed other configuration");
   Console.WriteLine("PASS: scoped reset/restore retains other configurations, creates new instance and invalidates old pages");
   live=ConfigurationSession.Capture(null,m.Object);ConfigurationSession.Invalidate(m.Object,true);invalid=false;try{live.RequireCurrent();}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Closed session still valid");
   Console.WriteLine("PASS: normal save retains IDs; old revisions, configuration switch and close invalidate leases");
  }
  var duplicate=tree();var extra=new Link(duplicate.Link);extra.Name="other";extra.Joint.Name="pitch1_joint";duplicate.Nodes.Add(new LinkNode(extra));bool duplicateRejected=false;
  try{JointDescriptor.FromTree(duplicate).ToDictionary(x=>x.name);}catch(ArgumentException e){if(!knownFailure)throw;duplicateRejected=true;Console.WriteLine("REPRODUCED independent duplicate joint-name key: "+e.Message);}catch(InvalidDataException e){if(!e.Message.Contains("pitch1_joint")||!e.Message.Contains("/base/pitch1")||!e.Message.Contains("/base/other"))throw;duplicateRejected=true;Console.WriteLine("PASS: duplicate joint name has both object paths: "+e.Message);}
  if(!duplicateRejected)throw new Exception("Duplicate joint-name case did not fail");
  if(!knownFailure){
   var editable=ConfigurationSerialization.ReadTree(ConfigurationSerialization.WriteTree(duplicate),1.4);((LinkNode)editable.Nodes[1]).Link.Joint.Name="other_joint";StableReferences.ValidateIdentities(editable);Console.WriteLine("PASS: duplicate names can be loaded into URDF editor and explicitly corrected");
   duplicate=tree();var copy=((LinkNode)duplicate.Nodes[0]).Link.Clone();copy.Name="renamed_copy";duplicate.Nodes.Add(new LinkNode(copy));bool caught=false;
   try{StableReferences.ValidateIdentities(duplicate);}catch(InvalidDataException e){caught=e.Message.Contains("link ID")&&e.Message.Contains("renamed_copy");}
   if(!caught)throw new Exception("Duplicate ID silently accepted");Console.WriteLine("PASS: duplicate link ID rejected with object paths");
   var conflict=new SimulationProject();conflict.attachments.Add(new Attachment{id="same",name="one"});conflict.attachments.Add(new Attachment{id="same",name="two"});caught=false;
   try{conflict.NormalizeSiteReferences();}catch(InvalidDataException e){caught=e.Message.Contains("same")&&e.Message.Contains("site[0]")&&e.Message.Contains("site[1]");}if(!caught)throw new Exception("Site duplicate diagnostic missing paths");Console.WriteLine("PASS: duplicate configuration ID includes both collection paths");
   var deps=new SimulationProject{collision=new CollisionConfiguration()};deps.attachments.Add(new Attachment{id="site-a",name="sa",link_id="link-a"});deps.attachments.Add(new Attachment{id="site-b",name="sb",link_id="link-b"});
   deps.sensors.Add(new SensorConfig{id="sensor",name="imu",site_id="site-a"});deps.equalities.Add(new EqualityConfig{id="eq",name="loop",type="connect",binding="site",site1_id="site-a",site2_id="site-b"});deps.site_forces.Add(new SiteForceConfig{id="force",name="spring",type="spring",site1_id="site-a",site2_id="site-b"});
   deps.actuators.Add(new ActuatorConfig{id="motor",name="motor",joint_id="joint-a"});deps.joints.Add(new JointConfiguration{joint_id="joint-a"});deps.joint_force_limits.Add(new JointForceLimit{joint_id="joint-a"});deps.collision.geometries.Add(new CollisionGeometry{link_id="link-a",name="proxy"});deps.collision.allowed_pairs.Add(new CollisionPair{link1_id="link-a",link2_id="link-b"});deps.collision.SetMode("link-a","primitive");
   var delete=ConfigurationDependencies.Plan(deps,new[]{"link-a"},new[]{"joint-a"});
   if(deps.attachments.Count!=2||deps.sensors.Count!=1||deps.collision.geometries.Count!=1)throw new Exception("Planning/cancel modified draft");
   if(delete.Result.attachments.Count!=1||delete.Result.sensors.Count!=0||delete.Result.equalities.Count!=0||delete.Result.site_forces.Count!=0||delete.Result.actuators.Count!=0||delete.Result.joints.Count!=0||delete.Result.joint_force_limits.Count!=0||delete.Result.collision.allowed_pairs.Count!=0||delete.Result.collision.link_modes_by_id.Count!=0)throw new Exception("Deletion dependencies incomplete");
   var sensors=deps.sensors;delete.ApplyTo(deps);if(!ReferenceEquals(sensors,deps.sensors)||deps.attachments[0].id!="site-b")throw new Exception("Deletion did not retain unaffected objects and UI collection binding");Console.WriteLine("PASS: dependency planning is non-mutating and cleanup covers all supported bindings");
   var unrelated=new SimulationProject();unrelated.attachments.Add(new Attachment{id="new",name="same"});unrelated.sensors.Add(new SensorConfig{name="orphan",site_id="deleted",site="same"});var sameName=ConfigurationDependencies.Plan(unrelated,sites:new[]{"new"});if(sameName.Result.sensors.Count!=1)throw new Exception("Deletion guessed same-name identity");Console.WriteLine("PASS: cleanup does not guess same-name replacement for dangling ID");
   var prior=raw;int writes=0;p.Setup(x=>x.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Callback<string,int,string>((v,scope,n)=>raw=v).Returns(()=>++writes>1);bool failed=false;
   try{SimulationStorage.SaveTree(null,m.Object,ConfigurationSerialization.WriteTree(newTree),1.4,delete.Result);}catch(IOException){failed=true;}
   if(!failed||raw!=prior||deps.attachments.Count!=1)throw new Exception("Failed deletion save lost saved data or draft");Console.WriteLine("PASS: failed combined tree/config write rolls back saved data and keeps edited draft");
  }
 }
}
'@
[ConfigurationRebuildTest]::Run($ExpectKnownFailure.IsPresent)
