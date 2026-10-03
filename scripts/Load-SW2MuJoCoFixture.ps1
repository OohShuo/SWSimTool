param([Parameter(Mandatory=$true)][int]$ProcessId,[Parameter(Mandatory=$true)][string]$FixtureDirectory)
$ErrorActionPreference='Stop'
$directory=(Resolve-Path -LiteralPath $FixtureDirectory).Path
$owned=[IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) 'build'))+[IO.Path]::DirectorySeparatorChar
if(-not $directory.StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)){throw 'Only generated fixtures allowed'}
if([int](Get-Content -LiteralPath (Join-Path $directory 'solidworks-process.txt')) -ne $ProcessId){throw 'Fixture process mismatch'}
$interop='D:\sw\sw2025\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
[Reflection.Assembly]::LoadFrom($interop)|Out-Null
Add-Type -ReferencedAssemblies $interop -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
public static class LoadFixturePlugin {
 public static void Run(int pid,string directory){
  var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");if(sw.GetProcessID()!=pid)throw new Exception("Unexpected SW instance");
  var model=(ModelDoc2)sw.ActiveDoc;if(model==null||!string.Equals(model.GetPathName(),Path.Combine(directory,"isolated_fixture.SLDASM"),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected document");
  Console.WriteLine("LoadAddIn result: "+sw.LoadAddIn(@"C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter\SW2URDF.dll"));
 }
}
'@
[LoadFixturePlugin]::Run($ProcessId,$directory)
