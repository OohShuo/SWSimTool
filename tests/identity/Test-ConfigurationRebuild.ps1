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
   live=ConfigurationSession.Capture(null,m.Object);ConfigurationSession.Invalidate(m.Object,true);invalid=false;try{live.RequireCurrent();}catch(InvalidDataException){invalid=true;}if(!invalid)throw new Exception("Closed session still valid");
   Console.WriteLine("PASS: normal save retains IDs; old revisions, configuration switch and close invalidate leases");
  }
  var duplicate=tree();var extra=new Link(duplicate.Link);extra.Name="other";extra.Joint.Name="pitch1_joint";duplicate.Nodes.Add(new LinkNode(extra));bool duplicateRejected=false;
  try{JointDescriptor.FromTree(duplicate).ToDictionary(x=>x.name);}catch(ArgumentException e){duplicateRejected=true;Console.WriteLine("REPRODUCED independent duplicate joint-name key: "+e.Message);}
  if(!duplicateRejected)throw new Exception("Duplicate joint-name case did not fail");
 }
}
'@
[ConfigurationRebuildTest]::Run($ExpectKnownFailure.IsPresent)
