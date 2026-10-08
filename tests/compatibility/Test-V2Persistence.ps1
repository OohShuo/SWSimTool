param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=$(for($p=$PSScriptRoot;$p;$p=Split-Path -Parent $p){if(Test-Path (Join-Path $p 'SWSimTool.sln')){$p;break}})
$bin=Join-Path $root ('build/'+$Payload)
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/MathNet.Numerics.dll",'System.Core','System.Xml','System.Xml.Linq','System.Web.Extensions','System.Runtime.Serialization','System.Windows.Forms')
Get-ChildItem $bin -Filter *.dll | ForEach-Object {[Reflection.Assembly]::LoadFrom($_.FullName)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Xml.Linq;using System.Web.Script.Serialization;using SWSimTool.Simulation;using SWSimTool.URDFExport;using SWSimTool.URDF;using SWSimTool.Persistence;
public static class V2Compatibility {
 static int checks;static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("PASS: "+text);}
 public static void Run(string folder,string output){
  var raw=File.ReadAllText(Path.Combine(folder,"legacy_v2_document.json"));var document=SimulationStorage.Parse(raw);var original=document.configurations["Default"];
  Check(document.version==2&&original.configuration_id=="swcfg:7","Original v2 JSON/configuration identity load");
  var tree=ConfigurationSerialization.ReadTree(original.urdf_xml,original.urdf_version);var arm=(LinkNode)tree.Nodes[0];
  Check(arm.Link.Joint.CoordinateReference.FeatureId=="fixed-cad-reference","Original JointCadReference XML namespace loads");
  var id=arm.Link.StableId;var jointId=arm.Link.Joint.StableId;var before=ExportFingerprint.Hash(original.simulation);
  StableReferences.Normalize(original.simulation,tree);
  Check(before==ExportFingerprint.Hash(original.simulation),"All simulation fields and stable references unchanged (canonical JSON)");
  var xml=ConfigurationSerialization.WriteTree(tree);
  Check(XNode.DeepEquals(XElement.Parse(original.urdf_xml),XElement.Parse(xml)),"URDF persistence XML semantic equality");
  original.urdf_xml=xml;var saved=new JavaScriptSerializer{MaxJsonLength=16000000}.Serialize(document);var reopened=SimulationStorage.Parse(saved).configurations["Default"];var reopenedTree=ConfigurationSerialization.ReadTree(reopened.urdf_xml,reopened.urdf_version);var reopenedArm=(LinkNode)reopenedTree.Nodes[0];
  Check(reopenedArm.Link.StableId==id&&reopenedArm.Link.Joint.StableId==jointId&&reopened.configuration_id==original.configuration_id,"IDs survive save/reopen");
  Check(before==ExportFingerprint.Hash(reopened.simulation),"Simulation survives save/reopen (canonical JSON)");
  Check(DocumentStorageSchema.AttributeName=="SWSimTool Configuration"&&DocumentStorageSchema.LegacyAttributeName=="SW2MuJoCo Configuration","Storage identifier retained");
  foreach(int version in new[]{1,3}){bool rejected=false;try{SimulationStorage.Parse(raw.Replace("\"version\":2","\"version\":"+version));}catch(Exception){rejected=true;}Check(rejected,"Unsupported version "+version+" rejected");}
  foreach(string bad in new[]{"{}",raw.Replace("\"version\":2,", ""),"{\"version\":2}"}){bool rejected=false;try{SimulationStorage.Parse(bad);}catch(Exception){rejected=true;}Check(rejected,"Missing explicit storage envelope rejected");}
  bool invalid=false;try{ConfigurationSerialization.ReadTree("<broken/>",1.4);}catch(InvalidDataException){invalid=true;}Check(invalid,"Invalid XML explicitly rejected");
  arm.Name=arm.Text=arm.Link.Name="renamed_arm";StableReferences.Normalize(original.simulation,tree);Check(arm.Link.StableId==id&&original.simulation.actuators[0].joint_id==jointId,"Rename retains identity");
  Check(MuJoCoSettings.DefaultPath.Contains("SWSimTool")&&MeshExportSettings.DefaultPath.Contains("SWSimTool"),"Local preferences isolated");
  // A pre-Stable-ID tree is read separately by configuration loading and export.
  // Those reads must share the migrated identities without requiring a CAD save.
  var legacy=new JavaScriptSerializer{MaxJsonLength=16000000}.Deserialize<SimulationStorage.Document>(raw);
  var legacyEntry=legacy.configurations["Default"];var legacyXml=XElement.Parse(legacyEntry.urdf_xml);
  foreach(var element in System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(legacyXml.Descendants(),x=>x.Name.LocalName=="stableId")))element.Remove();
  legacyEntry.urdf_xml=legacyXml.ToString();legacyEntry.simulation=new SimulationProject();
  Directory.CreateDirectory(output);var legacyRaw=new JavaScriptSerializer{MaxJsonLength=16000000}.Serialize(legacy);var sourceFingerprint=ExportFingerprint.Hash(legacy);
  var migrated=SimulationStorage.Parse(legacyRaw).configurations["Default"];
  var loadedTree=ConfigurationSerialization.ReadTree(migrated.urdf_xml,1.4);
  var exportedTree=ConfigurationSerialization.ReadTree(migrated.urdf_xml,1.4);
  Check(loadedTree.Link.StableId==exportedTree.Link.StableId,"Legacy root identity shared by independent load/export reads");
  Check(((LinkNode)loadedTree.Nodes[0]).Link.StableId==((LinkNode)exportedTree.Nodes[0]).Link.StableId,"Legacy child identity shared by independent reads");
  Check(((LinkNode)loadedTree.Nodes[0]).Link.Joint.StableId==((LinkNode)exportedTree.Nodes[0]).Link.Joint.StableId,"Legacy joint identity shared by independent reads");
  Check(sourceFingerprint==ExportFingerprint.Hash(legacy),"Read migration does not overwrite source document");
  Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"roundtrip.json"),saved);Console.WriteLine("Compatibility checks: "+checks);
 }
}
'@
[V2Compatibility]::Run((Join-Path $PSScriptRoot 'sw2mujoco_v2'),(Join-Path $root 'build/swsimtool-compatibility'))
