param([string]$Payload="bin/SWSimTool.SolidWorks/Release/net48")
$ErrorActionPreference='Stop'
$root=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$bin=Join-Path $root ('build\'+$Payload)
$folder=Join-Path $root ('build\link-mode-lifecycle-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
@("$bin\SWSimTool.dll","$bin\SWSimTool.Core.dll","$bin\SWSimTool.Application.dll","$bin\SWSimTool.Infrastructure.dll") | ForEach-Object {[Reflection.Assembly]::LoadFrom($_) | Out-Null}
$refs=@("$bin\SWSimTool.dll","$bin\SWSimTool.Core.dll","$bin\SWSimTool.Application.dll","$bin\SWSimTool.Infrastructure.dll",'System.Core','System.Xml','System.Xml.Linq','System.Web.Extensions','System.Runtime.Serialization','System.Windows.Forms')
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using SWSimTool.URDF;using SWSimTool.URDFExport;using SWSimTool.Simulation;using SWSimTool.RobotModel;
public static class LinkModeIdentityProbe {
 static void Check(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 static SimulationStorage.Entry RoundTrip(LinkNode tree,SimulationProject project,string folder,string stage){
  var document=new SimulationStorage.Document();document.configurations["test"]=new SimulationStorage.Entry{urdf_xml=ConfigurationSerialization.WriteTree(tree),simulation=project};
  var path=Path.Combine(folder,stage+".json");File.WriteAllText(path,ExportFingerprint.Serializer().Serialize(document));return SimulationStorage.Parse(File.ReadAllText(path)).configurations["test"];
 }
 static RobotCoreSnapshot Core(LinkNode tree){var nodes=tree.Nodes.Cast<LinkNode>().ToArray();var inertia=new InertialSnapshot(1,RigidTransform.Identity,new SymmetricInertia(.1,.1,.1,0,0,0));return new RobotCoreSnapshot("identity",new[]{new LinkSnapshot(tree.Link.StableId,tree.Name,inertia,new GeometrySnapshot[0])}.Concat(nodes.Select(n=>new LinkSnapshot(n.Link.StableId,n.Name,inertia,new GeometrySnapshot[0]))),nodes.Select(n=>new JointSnapshot(n.Link.Joint.StableId,n.Link.Joint.Name,tree.Link.StableId,n.Link.StableId,JointKind.Continuous,RigidTransform.Identity,new Vector3d(0,0,1),null,null,0,0)));}
 static string Xml(LinkNode tree,SimulationProject project){var core=Core(tree);var geometry=new ResolvedSimulationGeometry(new SiteSnapshot[0],new CollisionGeometrySnapshot[0]);return MjcfExporter.Generate(new RobotModel(core,SimulationConfigBuilder.Build(project,core,geometry)),new PreparedAssets(new PreparedMeshAsset[0]),new ExportContext("identity"));}
 public static void Run(string folder){
  var root=new Robot().BaseLink;root.Name="base";var arm=new Link(root);arm.Name="arm";arm.Joint.Name="hinge";root.Children.Add(arm);var tree=new LinkNode(root);var p=new SimulationProject{collision=new CollisionConfiguration()};p.collision.link_modes["arm"]="none";
  StableReferences.MigrateLegacyLinkModesByName(p,StableReferences.LinkNames(tree));var id=arm.StableId;
  Check(p.collision.Mode(id)=="none"&&p.collision.link_modes.Count==0,"Legacy name migrates once to stable ID");
  var entry=RoundTrip(tree,p,folder,"created");tree=ConfigurationSerialization.ReadTree(entry.urdf_xml,1.4);p=entry.simulation;
  Check(p.collision.Mode(id)=="none","Create/save/reopen preserves A");
  var node=(LinkNode)tree.Nodes[0];node.Name=node.Link.Name="left_arm";StableReferences.RemapLinkModesByStableId(p);StableReferences.Normalize(p,tree);
  Check(p.collision.Mode(id)=="none","Rename preserves collision mode by A");
  entry=RoundTrip(tree,p,folder,"renamed");tree=ConfigurationSerialization.ReadTree(entry.urdf_xml,1.4);p=entry.simulation;Check(p.collision.Mode(id)=="none","Rename/save/reopen preserves A");
  var warm=Xml(tree,p);File.WriteAllText(Path.Combine(folder,"warm.xml"),warm);
  var coldEntry=RoundTrip(tree,p,folder,"cold");var cold=Xml(ConfigurationSerialization.ReadTree(coldEntry.urdf_xml,1.4),coldEntry.simulation);File.WriteAllText(Path.Combine(folder,"cold.xml"),cold);Check(warm==cold,"Warm and reloaded cold snapshot produce identical XML");
  tree.Nodes.Clear();tree.Link.Children.Clear();StableReferences.RemapLinkModesByStableId(p);Check(p.collision.link_modes_by_id.ContainsKey(id),"Deleted A remains unresolved instead of transferring configuration");
  var replacement=new Link(tree.Link);replacement.Name="left_arm";replacement.Joint.Name="hinge";tree.Link.Children.Add(replacement);tree.Nodes.Add(new LinkNode(replacement));var newId=replacement.StableId;
  StableReferences.Normalize(p,tree);Check(newId!=id&&p.collision.Mode(newId)=="mesh"&&!p.collision.link_modes_by_id.ContainsKey(newId),"Same-name recreated B never inherits A mode");
  entry=RoundTrip(tree,p,folder,"recreated");tree=ConfigurationSerialization.ReadTree(entry.urdf_xml,1.4);p=entry.simulation;
  Check(p.collision.Mode(newId)=="mesh"&&p.collision.link_modes_by_id.ContainsKey(id),"Delete/recreate/save/reopen preserves unresolved A and default B");
  bool rejected=false;try{Xml(tree,p);}catch(InvalidDataException){rejected=true;}Check(rejected,"Cold rebuild rejects deleted stable ID without name fallback");
  var projection=new SimulationProject{collision=new CollisionConfiguration()};projection.collision.link_modes_migrated=true;projection.collision.link_modes_by_id[id]="none";projection.collision.link_modes["left_arm"]="none";
  rejected=false;try{LegacySimulationConfigImporter.Import(ExportFingerprint.Serializer().Serialize(projection),Core(tree));}catch(InvalidDataException){rejected=true;}Check(rejected,"Sidecar adapter rejects deleted ID despite matching display name");
  projection.collision.link_modes_by_id.Clear();Check(LegacySimulationConfigImporter.Import(ExportFingerprint.Serializer().Serialize(projection),Core(tree)).Collision.LinkModes.Count==0,"Explicit empty ID map never falls back to legacy projection");
  var ambiguous=new SimulationProject{collision=new CollisionConfiguration()};ambiguous.collision.link_modes["same"]="none";rejected=false;try{StableReferences.MigrateLegacyLinkModesByName(ambiguous,new Dictionary<string,string>{{"A","same"},{"B","same"}});}catch(InvalidDataException){rejected=true;}Check(rejected,"Ambiguous legacy mode migration rejected");
 }
}
'@
[LinkModeIdentityProbe]::Run($folder)
$folder | Set-Content -LiteralPath (Join-Path $root 'build\link-mode-lifecycle-directory.txt')
