param([switch]$ExpectKnownFailure,[string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
. "$root/tools/build/CadProcessGuard.ps1"
if(Get-ActiveSolidWorksProcesses){throw 'Refusing lifecycle test while a SolidWorks session exists'}
$folder=Join-Path $root ('build/lifecycle-cad-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
Get-ChildItem "$root/tests/fixtures/sw2mujoco-v2/cad" -File | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $folder}
$bin=Join-Path $root ('build/'+$Payload);$sdk='D:/sw/sw2025/SOLIDWORKS'
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/MathNet.Numerics.dll","$sdk/SolidWorks.Interop.sldworks.dll",'System.Core','System.Xml','System.Web.Extensions','System.Runtime.Serialization','System.Windows.Forms')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Reflection;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.URDF;using SWSimTool.URDFExport;
public static class LifecycleCadTest {
 static void Check(bool b,string message){if(!b)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 public static void Run(string folder,bool knownFailure){
  var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));Check(sw.GetDocumentCount()==0,"Fresh session has no user documents");sw.Visible=true;sw.UserControl=false;ModelDoc2 m=null,guard=null;
  try{
   int e=0,w=0;string path=Path.Combine(folder,"fixture.SLDASM");m=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref e,ref w);Check(m!=null,"Owned fixture copy opened");
   foreach(Component2 c in (object[])((AssemblyDoc)m).GetComponents(false))Check(c.GetPathName().StartsWith(folder+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Component stays in isolated copy");
   var entry=SimulationStorage.LoadEntry(m);bool error;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(m,out error);var child=(LinkNode)tree.Nodes[0];
   var project=new SimulationProject();project.attachments.Add(new Attachment{name="site_pitch1",link=child.Name,type="point"});SimulationStorage.Save(sw,m,project);
   var old=new ExportHelper(sw).GetSimulation();string oldId=old.Project.attachments[0].link_id;
   SolidWorks.Interop.sldworks.Attribute node=null;foreach(Feature f in (object[])m.FeatureManager.GetFeatures(true))if(f.GetTypeName2()=="Attribute"){var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;if(a!=null&&a.GetName()==SimulationStorage.NodeName)node=a;}
   Check(node!=null&&node.Delete(false),"Entire plugin node deleted");Check(SimulationStorage.Load(m)==null,"Absent node does not load old project");
   Action<LinkNode> renew=null;renew=n=>{typeof(Link).GetField("stableId",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(n.Link,null);foreach(LinkNode sub in n.Nodes)renew(sub);};renew(tree);
   ConfigurationSerialization.SaveConfigTreeXML(sw,m,tree,false);string before=SimulationStorage.LoadEntry(m).urdf_xml;bool rejected=false;try{old.Save();}catch(InvalidDataException){rejected=true;}
   var loaded=SimulationStorage.Load(m);bool polluted=loaded!=null&&loaded.attachments.Any(a=>a.link_id==oldId);
   if(knownFailure){Check(!rejected&&polluted,"Baseline reproduces stale draft writing old parent ID");return;}
   Check(rejected&&!polluted&&SimulationStorage.LoadEntry(m).urdf_xml==before,"Old page rejected; rebuilt tree untouched");
   var fresh=new ExportHelper(sw).GetSimulation();var source=fresh.Sources().First();fresh.Project.attachments.Add(fresh.Capture(source,child.Name,"new_site"));fresh.Save();var saved=SimulationStorage.LoadEntry(m);string id=saved.simulation.attachments[0].link_id;Check(id!=oldId,"New site belongs to new link ID");
   fresh.Project.attachments.Add(fresh.Capture(source,child.Name,"discard_saved_site"));fresh.Save();
   var draft=ExportFingerprint.Serializer().Deserialize<SimulationProject>(ExportFingerprint.Serializer().Serialize(fresh.Project));draft.attachments.RemoveAll(x=>x.name=="discard_saved_site");draft.attachments[0].name="unsaved_site_name";draft.solver=new SolverSettings{enabled=true,timestep=.002};
   fresh.RebuildCurrentDraft(draft,tree);saved=SimulationStorage.LoadEntry(m);Check(saved.simulation.attachments.Count==1&&saved.simulation.attachments[0].name=="unsaved_site_name"&&saved.simulation.solver.timestep==.002,"Full UI-style draft replaces persistence without resurrecting removed site");Check(saved.simulation.attachments[0].link_id==id,"Rebuild preserves site parent identity");
   var reset=ConfigurationSession.Capture(sw,m);string backup=SimulationStorage.ResetCurrent(sw,m,reset,Path.Combine(folder,"backups"));Check(SimulationStorage.Load(m)==null,"Scoped reset enters unconfigured state");SimulationStorage.RestoreCurrent(sw,m,ConfigurationSession.Capture(sw,m),backup,Path.Combine(folder,"backups"));saved=SimulationStorage.LoadEntry(m);Check(saved.simulation.attachments[0].name=="unsaved_site_name","Verified backup restores complete configuration");
   Check(m.Save3(1,ref e,ref w),"Save rebuilt assembly");guard=(ModelDoc2)sw.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\gb_assembly.asmdot",0,0,0);sw.CloseDoc(m.GetTitle());m=null;
   m=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref e,ref w);var reopened=SimulationStorage.LoadEntry(m);Check(reopened.simulation.attachments[0].link_id==id,"CAD close/reopen preserves rebuilt references");Check(reopened.urdf_xml==saved.urdf_xml,"CAD close/reopen preserves all tree IDs");
   using(var export=new ProjectExport(sw,m,m.ConfigurationManager.ActiveConfiguration.Name)){
    var output=Path.Combine(folder,"export","fixture.xml");int code=NativeBackend.RunAsync(@"D:\Softwaves\python\python.exe",export.NativeModel(),output,false,Console.WriteLine).GetAwaiter().GetResult();Check(code==0,"Rebuilt CAD exports and passes MuJoCo validation");
   }
  }finally{if(m!=null)sw.CloseDoc(m.GetTitle());if(guard!=null)sw.CloseDoc(guard.GetTitle());try{sw.ExitApp();}catch(System.Runtime.InteropServices.COMException ex){if(ex.HResult!=unchecked((int)0x80010108))throw;}}
 }
}
'@
[LifecycleCadTest]::Run($folder,$ExpectKnownFailure.IsPresent)
$folder | Set-Content "$root/build/lifecycle-cad-directory.txt"
