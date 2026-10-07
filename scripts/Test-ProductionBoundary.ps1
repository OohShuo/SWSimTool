param([string]$Payload='stable-audit')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root ('build\'+$Payload+'\SW2URDF.dll')))
$type=$assembly.GetType('SW2URDF.Simulation.ProjectExport')
$constructors=$type.GetConstructors()
if($constructors.Count -ne 1 -or $constructors[0].GetParameters().Count -ne 3 -or ($constructors[0].GetParameters() | Where-Object IsOptional)) {throw 'Production constructor exposes a reference selector'}
'PASS: production constructor has no optional/reference selector'
if(!$type.GetMethod('ForReferenceTests')) {throw 'Explicit reference entry missing'}
'PASS: reference harness has explicit named entry'
$source=[IO.File]::ReadAllText((Join-Path $root 'SW2URDF\SW\SwAddin.cs'))
if($source.Contains('ForReferenceTests') -or $source.Contains('BuildReference')) {throw 'Production addin invokes reference entry'}
'PASS: production addin never calls reference entry'
$builder=$assembly.GetType('SW2URDF.Simulation.ProjectSourceBuilder')
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$entries=$builder.GetMethods($flags) | Where-Object {$_.IsAssembly}
if(($entries.Name -notcontains 'BuildNative') -or ($entries.Name -notcontains 'BuildReference') -or ($entries.Name -contains 'Build')) {throw 'Source builder exposes a mode switch'}
'PASS: source builder exposes separate native/reference methods'
