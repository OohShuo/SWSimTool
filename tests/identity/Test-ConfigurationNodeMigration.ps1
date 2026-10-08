param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload);$sdk='D:/sw/sw2025/SOLIDWORKS'
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/MathNet.Numerics.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Moq.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Castle.Core.dll","$sdk/SolidWorks.Interop.sldworks.dll",'System.Core','System.Xml','System.Runtime.Serialization','System.Web.Extensions','System.Windows.Forms')
$refs|Where-Object {$_ -like '*.dll'}|ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using Moq;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.Persistence;
public static class NodeMigrationTest {
 public static void Run(){
  var c=new Mock<Configuration>();c.SetupGet(x=>x.Name).Returns("Default");var cm=new Mock<ConfigurationManager>();cm.SetupGet(x=>x.ActiveConfiguration).Returns(c.Object);
  var document=new SimulationStorage.Document();document.configurations["Default"]=new SimulationStorage.Entry{instance_id="retained-instance",simulation=new SimulationProject()};string raw=ExportFingerprint.Serializer().Serialize(document);int writes=0;bool fail=false;
  var p=new Mock<Parameter>();p.Setup(x=>x.GetStringValue()).Returns(()=>raw);p.Setup(x=>x.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Callback<string,int,string>((v,s,n)=>{raw=v;writes++;}).Returns(()=>!fail||writes>1);
  var f=new Mock<Feature>();f.SetupProperty(x=>x.Name,DocumentStorageSchema.LegacyAttributeName);f.Setup(x=>x.GetTypeName2()).Returns("Attribute");
  var a=new Mock<SolidWorks.Interop.sldworks.Attribute>();a.Setup(x=>x.GetName()).Returns(()=>f.Object.Name);a.Setup(x=>x.GetParameter("data")).Returns(p.Object);f.Setup(x=>x.GetSpecificFeature2()).Returns(a.Object);
  object[] nodes=new object[]{f.Object};var fm=new Mock<FeatureManager>();fm.Setup(x=>x.GetFeatures(true)).Returns(()=>nodes);
  var m=new Mock<ModelDoc2>();m.SetupGet(x=>x.FeatureManager).Returns(fm.Object);m.SetupGet(x=>x.ConfigurationManager).Returns(cm.Object);m.Setup(x=>x.GetConfigurationNames()).Returns(new[]{"Default"});m.Setup(x=>x.GetConfigurationByName("Default")).Returns(c.Object);m.Setup(x=>x.GetPathName()).Returns(Path.Combine(Directory.GetCurrentDirectory(),"build/test-work/legacy-isolated.SLDASM"));
  var original=raw;var entry=SimulationStorage.LoadEntry(m.Object);if(entry.instance_id!="retained-instance"||raw!=original||!SimulationStorage.RequiresNameMigration(m.Object))throw new Exception("Legacy load modified persistence");
  SimulationStorage.Save(null,m.Object,new SimulationProject());if(f.Object.Name!=SimulationStorage.NodeName||SimulationStorage.LoadEntry(m.Object).instance_id!="retained-instance")throw new Exception("Migration changed instance or failed name");
  f.Object.Name=DocumentStorageSchema.LegacyAttributeName;original=raw;writes=0;fail=true;bool rejected=false;try{SimulationStorage.Save(null,m.Object,new SimulationProject());}catch(IOException){rejected=true;}if(!rejected||raw!=original||f.Object.Name!=DocumentStorageSchema.LegacyAttributeName)throw new Exception("Migration write failure did not preserve data and name");fail=false;
  var other=new Mock<Feature>();other.SetupGet(x=>x.Name).Returns(SimulationStorage.NodeName);other.Setup(x=>x.GetTypeName2()).Returns("Attribute");var otherAttribute=new Mock<SolidWorks.Interop.sldworks.Attribute>();otherAttribute.Setup(x=>x.GetName()).Returns(SimulationStorage.NodeName);other.Setup(x=>x.GetSpecificFeature2()).Returns(otherAttribute.Object);nodes=new object[]{f.Object,other.Object};writes=0;rejected=false;
  try{SimulationStorage.Save(null,m.Object,new SimulationProject());}catch(InvalidDataException e){rejected=e.Message.Contains(DocumentStorageSchema.LegacyAttributeName)&&e.Message.Contains(SimulationStorage.NodeName);}if(!rejected||writes!=0||raw!=original)throw new Exception("Conflicting nodes were not rejected before mutation");
  Console.WriteLine("PASS: legacy read is non-mutating; migration retains instance; failed write preserves name/data; new+old conflict stops before writes");
 }
}
'@
[NodeMigrationTest]::Run()
