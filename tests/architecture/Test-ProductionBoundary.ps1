param([string]$Payload="bin/SWSimTool/Release/net48")
$ErrorActionPreference='Stop'
$root=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $root ('build\'+$Payload+'\SWSimTool.dll')))
$type=$assembly.GetType('SWSimTool.Simulation.ProjectExport')
$constructors=$type.GetConstructors()
if($constructors.Count -ne 1 -or $constructors[0].GetParameters().Count -ne 3 -or ($constructors[0].GetParameters() | Where-Object IsOptional)) {throw 'Production constructor exposes a reference selector'}
'PASS: production constructor has no optional/reference selector'
if(!$type.GetMethod('ForReferenceTests')) {throw 'Explicit reference entry missing'}
'PASS: reference harness has explicit named entry'
$source=[IO.File]::ReadAllText((Join-Path $root 'SWSimTool\SW\SwAddin.cs'))
if($source.Contains('ForReferenceTests') -or $source.Contains('BuildReference')) {throw 'Production addin invokes reference entry'}
'PASS: production addin never calls reference entry'
$builder=$assembly.GetType('SWSimTool.Simulation.ProjectSourceBuilder')
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$entries=$builder.GetMethods($flags) | Where-Object {$_.IsAssembly}
if(($entries.Name -notcontains 'BuildNative') -or ($entries.Name -notcontains 'BuildReference') -or ($entries.Name -contains 'Build')) {throw 'Source builder exposes a mode switch'}
'PASS: source builder exposes separate native/reference methods'
