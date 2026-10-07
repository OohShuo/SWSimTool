param([Parameter(Mandatory=$true)][int]$ProcessId,[Parameter(Mandatory=$true)][string]$FixtureDirectory)
$ErrorActionPreference='Stop'
$fixturePath=(Resolve-Path -LiteralPath $FixtureDirectory).Path+[IO.Path]::DirectorySeparatorChar
$ownedRoot=[IO.Path]::GetFullPath((Join-Path ($(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SW2URDF.sln')) { $p; break } })) 'build'))+[IO.Path]::DirectorySeparatorChar
if(-not $fixturePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Only generated workspace fixtures are permitted'}
if([int](Get-Content -LiteralPath (Join-Path $fixturePath 'solidworks-process.txt')) -ne $ProcessId){throw 'Fixture process marker mismatch'}
$interop='D:\sw\sw2025\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
[Reflection.Assembly]::LoadFrom($interop)|Out-Null
Add-Type -ReferencedAssemblies $interop -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
public static class OwnedFixtureClose {
 public static void Run(int pid,string directory){
  var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
  if(sw.GetProcessID()!=pid)throw new Exception("Unexpected SolidWorks process");
  var doc=(ModelDoc2)sw.GetFirstDocument();
  while(doc!=null){
   if(!doc.GetPathName().StartsWith(directory,StringComparison.OrdinalIgnoreCase))throw new Exception("Non-fixture document; refusing to close SolidWorks");
   doc=(ModelDoc2)doc.GetNext();
  }
  sw.ExitApp();
 }
}
'@
[OwnedFixtureClose]::Run($ProcessId,$fixturePath)
