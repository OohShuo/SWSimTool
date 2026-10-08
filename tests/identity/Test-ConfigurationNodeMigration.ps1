param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/MathNet.Numerics.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Moq.dll","$root/build/bin/SWSimTool.Tests/Release/net48/Castle.Core.dll",'D:/sw/sw2025/SOLIDWORKS/SolidWorks.Interop.sldworks.dll','System.Core','System.Web.Extensions','System.Xml','System.Runtime.Serialization','System.Windows.Forms')
$refs|Where-Object {$_ -like '*.dll'}|ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Reflection;
using Moq;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.Persistence;
public static class ConfigurationNodeMigrationTest {
 sealed class Node {public string Name,Data;public Mock<SolidWorks.Interop.sldworks.Attribute> Attribute;public Mock<Feature> Feature;}
 sealed class Fixture {
  public List<Node> Nodes=new List<Node>();public Mock<ModelDoc2> Model=new Mock<ModelDoc2>();public Mock<SldWorks> App=new Mock<SldWorks>();
  public bool DeleteFails,WriteFails;public string Folder;
  public Fixture(string folder){
   var nodeExtension=new Mock<ModelDocExtension>();nodeExtension.Setup(x=>x.GetPersistReference3(It.IsAny<object>())).Returns((object feature)=>System.Text.Encoding.UTF8.GetBytes(Nodes.Single(n=>Object.ReferenceEquals(n.Feature.Object,feature)).Name));Model.SetupGet(x=>x.Extension).Returns(nodeExtension.Object);Folder=folder;var c=new Mock<Configuration>();c.SetupGet(x=>x.Name).Returns("Default");var cm=new Mock<ConfigurationManager>();cm.SetupGet(x=>x.ActiveConfiguration).Returns(c.Object);Model.SetupGet(x=>x.ConfigurationManager).Returns(cm.Object);Model.Setup(x=>x.GetPathName()).Returns("isolated.SLDASM");
   var fm=new Mock<FeatureManager>();fm.Setup(x=>x.GetFeatures(true)).Returns(()=>Nodes.Select(n=>(object)n.Feature.Object).ToArray());Model.SetupGet(x=>x.FeatureManager).Returns(fm.Object);
   App.Setup(x=>x.DefineAttribute(It.IsAny<string>())).Returns((string name)=>{var def=new Mock<AttributeDef>();def.Setup(x=>x.Register()).Returns(true);def.Setup(x=>x.CreateInstance5(It.IsAny<ModelDoc2>(),It.IsAny<object>(),It.IsAny<string>(),It.IsAny<int>(),It.IsAny<int>())).Returns((ModelDoc2 m,object owner,string label,int flags,int config)=>Add(name,null).Attribute.Object);return def.Object;});
  }
  public Node Add(string name,string data){
   var n=new Node{Name=name,Data=data,Attribute=new Mock<SolidWorks.Interop.sldworks.Attribute>(),Feature=new Mock<Feature>()};var p=new Mock<Parameter>();p.Setup(x=>x.GetStringValue()).Returns(()=>n.Data);
   p.Setup(x=>x.SetStringValue2(It.IsAny<string>(),It.IsAny<int>(),It.IsAny<string>())).Returns((string value,int scope,string config)=>{if(WriteFails&&n.Name==DocumentStorageSchema.AttributeName)return false;n.Data=value;return true;});
   n.Attribute.Setup(x=>x.GetName()).Returns(name);n.Attribute.Setup(x=>x.GetParameter("data")).Returns(p.Object);n.Attribute.Setup(x=>x.Delete(false)).Returns(()=>{if(DeleteFails&&name==DocumentStorageSchema.LegacyAttributeName)return false;Nodes.Remove(n);return true;});
   n.Feature.Setup(x=>x.GetTypeName2()).Returns("Attribute");n.Feature.Setup(x=>x.GetSpecificFeature2()).Returns(n.Attribute.Object);Nodes.Add(n);return n;
  }
  public IConfigurationTransactionStore Store(){var type=typeof(SimulationStorage).Assembly.GetType("SWSimTool.Simulation.SolidWorksAttributeDocumentStore");return (IConfigurationTransactionStore)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{App.Object,Model.Object,null,Folder},null);}
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 public static void Run(string folder){
  var f=new Fixture(folder);f.Add(DocumentStorageSchema.LegacyAttributeName,"old raw");var store=f.Store();Check(store.ReadConfiguration()=="old raw","legacy node loads without prior save/export");store.WriteConfiguration("complete draft");Check(f.Nodes.Count==1&&f.Nodes[0].Name==DocumentStorageSchema.AttributeName&&store.ReadConfiguration()=="complete draft","candidate verified then old node deleted");
  store.RestoreConfiguration("old raw");Check(f.Nodes.Count==1&&f.Nodes[0].Name==DocumentStorageSchema.LegacyAttributeName&&store.ReadConfiguration()=="old raw","rollback restores legacy identity and raw data");
  foreach(bool writeFailure in new[]{true,false}){f=new Fixture(folder){WriteFails=writeFailure,DeleteFails=!writeFailure};f.Add(DocumentStorageSchema.LegacyAttributeName,"original");bool rejected=false;try{f.Store().WriteConfiguration("candidate");}catch(IOException){rejected=true;}Check(rejected&&f.Nodes.Count==1&&f.Nodes[0].Name==DocumentStorageSchema.LegacyAttributeName&&f.Nodes[0].Data=="original","write/delete failure preserves single original node");}
  f=new Fixture(folder);f.Add(DocumentStorageSchema.LegacyAttributeName,"old");f.Add(DocumentStorageSchema.AttributeName,"new");bool conflict=false;try{f.Store().ReadConfiguration();}catch(InvalidDataException){conflict=true;}Check(conflict&&f.Nodes.Count==2,"new/legacy conflict rejected without modifying either node");
  var backups=Directory.GetFiles(folder,"*.swsimtool-backup.json");Check(backups.Any(p=>ConfigurationBackup.Read(p).node_name==DocumentStorageSchema.LegacyAttributeName),"backup records raw data and legacy node type");
 }
}
'@
[ConfigurationNodeMigrationTest]::Run((Join-Path $root 'build/test-work/node-migration-backups'))
