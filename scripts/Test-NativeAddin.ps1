param([Parameter(Mandatory=$true)][int]$ProcessId,[switch]$Fresh)
$ErrorActionPreference='Stop'
$workspacePath=Split-Path -Parent $PSScriptRoot
$dll=Join-Path $workspacePath 'build\simulation-final\SW2URDF.dll'
$interop='D:\sw\sw2025\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
[Reflection.Assembly]::LoadFrom($interop) | Out-Null
Add-Type -ReferencedAssemblies @($interop) -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
public static class NativeAddinValidation {
 public static void Load(int pid,string dll,string fixture,bool fresh) {
  if(pid==0) {
   var testSw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
   if(testSw.GetDocumentCount()!=0) throw new Exception("Refusing server with existing documents");
   testSw.Visible=true; testSw.UserControl=true;
   Console.WriteLine("New test PID: "+testSw.GetProcessID());
   Console.WriteLine("LoadAddIn result: "+testSw.LoadAddIn(dll));
   int err=0,warn=0;
   if(testSw.OpenDoc6(fixture,2,1,"",ref err,ref warn)==null) throw new Exception("Fixture open failed");
   return;
  }
  var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
  if(sw.GetProcessID()!=pid) throw new Exception("Different SolidWorks process");
  var model=(ModelDoc2)sw.ActiveDoc;
  if(!string.Equals(model.GetPathName(),fixture,StringComparison.OrdinalIgnoreCase)) throw new Exception("Different model");
  if(fresh) {
   int errors=0,warnings=0; model.Save3(1,ref errors,ref warnings); sw.ExitApp();
   // The exact process was verified above against the generated fixture. COM
   // test hosts can keep its server alive after ExitApp; terminate only that PID.
   if(!System.Diagnostics.Process.GetProcessById(pid).WaitForExit(3000)) System.Diagnostics.Process.GetProcessById(pid).Kill();
   System.Threading.Thread.Sleep(3000);
   sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
   if(sw.GetDocumentCount()!=0) throw new Exception("Refusing server with existing documents");
   sw.Visible=true; sw.UserControl=true;
   Console.WriteLine("New test PID: "+sw.GetProcessID());
   sw.LoadAddIn(dll);
   if(sw.OpenDoc6(fixture,2,1,"",ref errors,ref warnings)==null) throw new Exception("Fixture open failed");
   return;
  }
  sw.UnloadAddIn(@"C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter\SW2URDF.dll");
  Console.WriteLine("LoadAddIn result: "+sw.LoadAddIn(dll));
 }
}
'@
$overrideKey='HKCU:\Software\Classes\CLSID\{65c9fc17-6a74-45a3-8f84-55185900275d}'
if(Test-Path $overrideKey) {throw 'Existing per-user override; refusing overwrite'}
$assemblyName=[Reflection.AssemblyName]::GetAssemblyName($dll)
$registration=New-Item -Path ($overrideKey+'\InprocServer32') -Force
$registration.SetValue('', 'mscoree.dll')
$registration.SetValue('ThreadingModel','Both')
$registration.SetValue('Class','SW2URDF.SW.SwAddin')
$registration.SetValue('Assembly',$assemblyName.FullName)
$registration.SetValue('RuntimeVersion','v4.0.30319')
$registration.SetValue('CodeBase',([Uri]$dll).AbsoluteUri)
$versionRegistration=New-Item -Path ($overrideKey+'\InprocServer32\'+$assemblyName.Version.ToString()) -Force
foreach($valueName in @('Class','Assembly','RuntimeVersion','CodeBase')) {
 $versionRegistration.SetValue($valueName,$registration.GetValue($valueName))
}
try {
 [NativeAddinValidation]::Load($ProcessId,$dll,(Join-Path $workspacePath 'build\isolated-validation-20261002-203254\isolated_fixture.SLDASM'),[bool]$Fresh)
} finally {
 # Only this explicitly created, previously absent override is removed.
 Remove-Item -LiteralPath $overrideKey -Recurse -Force
}
