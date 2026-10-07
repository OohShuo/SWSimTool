param([string]$Installed='C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter\SW2URDF.dll')
$ErrorActionPreference='Stop'
if(Get-Process SLDWORKS -ErrorAction SilentlyContinue){throw 'Refusing installed addin test with an existing SolidWorks session'}
$workspacePath=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SW2URDF.sln')) { $p; break } })
$fixture=Join-Path $workspacePath ('build\installed-plugin-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$interop='D:\sw\sw2025\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
[Reflection.Assembly]::LoadFrom($interop) | Out-Null
Add-Type -ReferencedAssemblies @($interop) -TypeDefinition @'
using System;
using System.IO;
using SolidWorks.Interop.sldworks;
public static class InstalledPluginTest {
 public static void Run(string dll,string folder) {
  var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
  if(sw.GetDocumentCount()!=0)throw new Exception("Refusing server with existing documents");
  ModelDoc2 model=null;
  try {
   sw.Visible=false;sw.UserControl=false;
   int result=sw.LoadAddIn(dll);if(result!=0)throw new Exception("Installed LoadAddIn failed: "+result);
   Console.WriteLine("PASS: installed addin loads through SolidWorks COM");
   model=(ModelDoc2)sw.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\gb_assembly.asmdot",0,0,0);
   if(model==null)throw new Exception("Cannot create isolated assembly");
   int errors=0,warnings=0;
   if(!model.Extension.SaveAs(Path.Combine(folder,"empty_fixture.SLDASM"),0,1,null,ref errors,ref warnings))throw new Exception("Cannot save isolated assembly: "+errors);
   Console.WriteLine("PASS: installed addin permits new assembly creation/save");
  }finally{
   if(model!=null)sw.CloseDoc(model.GetTitle());
   if(sw.GetDocumentCount()==0){sw.UnloadAddIn(dll);sw.ExitApp();}
  }
 }
}
'@
[InstalledPluginTest]::Run($Installed,$fixture)
