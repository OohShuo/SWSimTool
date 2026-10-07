param([string]$Payload='stable-audit')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$folder=Join-Path $root ('build\configuration-identity-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
$bin=Join-Path $root ('build\'+$Payload)
$interop='D:\sw\sw2025\SOLIDWORKS'
$refs=@("$interop\SolidWorks.Interop.sldworks.dll","$interop\SolidWorks.Interop.swconst.dll","$bin\SW2URDF.dll",'System.Core','System.Runtime.Serialization','System.Xml','System.Xml.Linq','System.Web.Extensions','System.Windows.Forms')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Collections.Generic;
using SolidWorks.Interop.sldworks;using SW2URDF.Simulation;using SW2URDF.URDF;using SW2URDF.URDFExport;
public static class CadConfigurationIdentity {
 static int checks;static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
 static ModelDoc2 Reopen(SldWorks sw,ModelDoc2 model,string folder){var path=Path.GetFullPath(model.GetPathName());if(!path.StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Fixture containment check");int errors=0,warnings=0;Check(model.Save3(1,ref errors,ref warnings),"owned configuration fixture saved");sw.CloseDoc(model.GetTitle());var result=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref errors,ref warnings);Check(result!=null&&errors==0,"owned configuration fixture reopened");return result;}
 public static void Run(string folder){var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));if(sw.GetDocumentCount()!=0)throw new Exception("Refusing existing documents");try{sw.Visible=false;sw.UserControl=false;var model=(ModelDoc2)sw.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\gb_assembly.asmdot",0,0,0);if(model==null)throw new Exception("Template missing");string original=model.ConfigurationManager.ActiveConfiguration.Name;int errors=0,warnings=0;Check(model.Extension.SaveAs(Path.Combine(folder,"fixture.SLDASM"),0,1,null,ref errors,ref warnings),"owned assembly saved");
  var a=model.ConfigurationManager.AddConfiguration2("audit_config","","",0,null,"",true);Check(a!=null&&(model.ConfigurationManager.ActiveConfiguration.Name=="audit_config"||model.ShowConfiguration2("audit_config")),"owned configuration A created");int id=a.GetID();
  var root=new Link(null);root.Name="base";SimulationStorage.SaveTree(sw,model,ConfigurationSerialization.WriteTree(new LinkNode(root)),1.4);SimulationStorage.Save(sw,model,new SimulationProject{solver=new SolverSettings{enabled=true,timestep=.002}});model=Reopen(sw,model,folder);
  a=model.ConfigurationManager.ActiveConfiguration;Check(a.GetID()==id&&SimulationStorage.Load(model).solver.timestep==.002,"configuration A ID and settings survive CAD save/reopen");
  a.Name="renamed_config";Check(SimulationStorage.Load(model).solver.timestep==.002,"configuration rename keeps settings by ID");model=Reopen(sw,model,folder);Check(model.ConfigurationManager.ActiveConfiguration.GetID()==id&&SimulationStorage.Load(model).solver.timestep==.002,"configuration rename/save/reopen keeps ID and settings");
  Check(model.ShowConfiguration2(original)&&model.DeleteConfiguration2("renamed_config"),"owned configuration A deleted");var b=model.ConfigurationManager.AddConfiguration2("renamed_config","","",0,null,"",true);Check(b!=null&&b.GetID()!=id&&(model.ConfigurationManager.ActiveConfiguration.Name=="renamed_config"||model.ShowConfiguration2("renamed_config")),"same-name configuration B has a different SW ID");
  int replacementId=b.GetID(); Check(SimulationStorage.Load(model)==null&&SimulationStorage.LoadEntry(model).simulation==null,"same-name B never inherits A tree or simulation");
  SimulationStorage.Save(sw,model,new SimulationProject{solver=new SolverSettings{enabled=true,timestep=.003}});model=Reopen(sw,model,folder);Check(SimulationStorage.Load(model).solver.timestep==.003&&SimulationStorage.LoadEntry(model).urdf_xml==null,"B save/reopen keeps only its own settings");
  var old=new SimulationStorage.Document();old.configurations["old"]=new SimulationStorage.Entry{configuration_id="A",simulation=new SimulationProject{solver=new SolverSettings{timestep=.002}}};SimulationStorage.RemapConfigurations(old,new Dictionary<string,string>{{"B","old"}});Check(!old.configurations.ContainsKey("old")&&old.configurations.Values.Single().configuration_id=="A","deleted configuration stays unresolved rather than name-bound");
  File.WriteAllText(Path.Combine(folder,"report.json"),ExportFingerprint.Serializer().Serialize(new{status="passed",checks,originalId=id,replacementId=replacementId}));Console.WriteLine("CAD configuration identity checks: "+checks);
 }finally{var docs=sw.GetDocuments() as object[];if(docs!=null)foreach(ModelDoc2 doc in docs){string path=doc.GetPathName();if(!string.IsNullOrEmpty(path)&&Path.GetFullPath(path).StartsWith(Path.GetFullPath(folder)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))sw.CloseDoc(doc.GetTitle());}if(sw.GetDocumentCount()==0)sw.ExitApp();}}
}
'@
[CadConfigurationIdentity]::Run($folder)
$folder | Set-Content -LiteralPath (Join-Path $root 'build\configuration-identity-directory.txt')
