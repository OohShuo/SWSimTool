param([Parameter(Mandatory=$true)][string]$OwnedLegacyFixture,[string]$Payload='bin/SWSimTool/Release/net48',[int[]]$OwnedStaleProcessIds=@())
$ErrorActionPreference='Stop'
if(Get-Process SLDWORKS -ErrorAction SilentlyContinue | Where-Object {$_.Id -notin $OwnedStaleProcessIds}){throw 'Refusing compatibility CAD test with an existing SolidWorks session'}
$root=$(for($p=$PSScriptRoot;$p;$p=Split-Path -Parent $p){if(Test-Path (Join-Path $p 'SWSimTool.sln')){$p;break}})
$source=(Resolve-Path -LiteralPath $OwnedLegacyFixture).Path
$allowed=(Join-Path $root 'build')+[IO.Path]::DirectorySeparatorChar
if(-not $source.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase) -and $source -ne [IO.Path]::GetFullPath((Join-Path $root 'tests/fixtures/sw2mujoco-v2/cad'))){throw 'Only owned build fixtures or the frozen compatibility fixture are accepted'}
$folder=Join-Path $root ('build/swsimtool-v2-cad-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $folder | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object {$_.Extension -in '.SLDASM','.SLDPRT'} | ForEach-Object {Copy-Item -LiteralPath $_.FullName -Destination $folder}
$bin=Join-Path $root ('build/'+$Payload)
$refs=@("$bin/SWSimTool.dll","$bin/MathNet.Numerics.dll",'D:/sw/sw2025/SOLIDWORKS/SolidWorks.Interop.sldworks.dll','D:/sw/sw2025/SOLIDWORKS/SolidWorks.Interop.swconst.dll','System.Core','System.Xml','System.Web.Extensions','System.Runtime.Serialization','System.Windows.Forms')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Web.Script.Serialization;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.URDFExport;
public static class V2CadCompatibility {
 static void Check(bool value,string text){if(!value)throw new Exception(text);Console.WriteLine("PASS: "+text);}
 public static void Run(string folder,string python){
  var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));if(sw.GetDocumentCount()!=0)throw new Exception("Refusing existing documents");ModelDoc2 model=null,keepAlive=null;
  try {
   sw.Visible=false;sw.UserControl=false;int errors=0,warnings=0;string path=Path.Combine(folder,"fixture.SLDASM");model=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref errors,ref warnings);Check(model!=null&&errors==0,"Copied old owned fixture opens");
   bool dirty=model.GetSaveFlag();var entry=SimulationStorage.LoadEntry(model);Check(entry!=null&&!string.IsNullOrWhiteSpace(entry.urdf_xml),"Existing v2 CAD attribute loaded");Check(model.GetSaveFlag()==dirty,"Read does not mark CAD dirty");
   string oldXml=entry.urdf_xml;string configId=entry.configuration_id;string simulation=new JavaScriptSerializer().Serialize(entry.simulation);var project=SimulationStorage.Load(model);SimulationStorage.Save(sw,model,project);Check(model.Save3(1,ref errors,ref warnings),"New product saves owned fixture");keepAlive=(ModelDoc2)sw.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\gb_assembly.asmdot",0,0,0);Check(keepAlive!=null,"Owned guard document keeps COM server alive during reopen");sw.CloseDoc(model.GetTitle());model=null;
   model=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref errors,ref warnings);var reopened=SimulationStorage.LoadEntry(model);Check(reopened.configuration_id==configId,"Configuration stable ID retained after CAD reopen");Check(oldXml==reopened.urdf_xml,"URDF tree and IDs retained after CAD save/reopen");Check(simulation==new JavaScriptSerializer().Serialize(reopened.simulation),"All simulation fields retained after CAD reopen");
   using(var export=new ProjectExport(sw,model,model.ConfigurationManager.ActiveConfiguration.Name)){var settings=Path.Combine(folder,"mesh-settings.json");File.WriteAllText(settings,"{\"Enabled\":false}");int code=NativeBackend.RunAsync(python,export.NativeModel(),Path.Combine(folder,"export","fixture.xml"),false,Console.WriteLine,settings).GetAwaiter().GetResult();Check(code==0,"Old CAD v2 exported with new product and compiled by MuJoCo");}
  } finally {if(model!=null)sw.CloseDoc(model.GetTitle());if(keepAlive!=null)sw.CloseDoc(keepAlive.GetTitle());try{sw.ExitApp();}catch(System.Runtime.InteropServices.COMException e){if(e.HResult!=unchecked((int)0x80010108))throw;}}
 }
}
'@
[V2CadCompatibility]::Run($folder,'D:/Softwaves/python/python.exe')
$folder | Set-Content (Join-Path $root 'build/swsimtool-v2-cad-directory.txt')
