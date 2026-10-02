param([switch]$Rollback)
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated 64-bit PowerShell window.'
}
$root = Split-Path -Parent $PSScriptRoot
$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
$dll = Join-Path $root 'SW2URDF\bin\x64\Debug\SW2URDF.dll'
if ($Rollback) { $dll = 'C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter\SW2URDF.dll' }
if (-not (Test-Path -LiteralPath $dll)) { throw "DLL missing: $dll" }
& $regasm /codebase $dll
if ($LASTEXITCODE -ne 0) { throw "RegAsm failed: $LASTEXITCODE" }
$registered = Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\CLSID\{65c9fc17-6a74-45a3-8f84-55185900275d}\InprocServer32'
if ([Uri]::new($registered.CodeBase).LocalPath -ine $dll) {
    throw "Unexpected registered CodeBase: $($registered.CodeBase)"
}
Write-Host "Registered SW2URDF: $dll"
