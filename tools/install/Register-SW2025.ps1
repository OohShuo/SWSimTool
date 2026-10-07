param([switch]$Rollback,[string]$Payload="runtime-release")
$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated 64-bit PowerShell window.'
}
$root = $(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$regasm = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\RegAsm.exe'
$dll = Join-Path $root ('build\'+$Payload+'\SWSimTool.dll')
if ($Rollback) { $dll = 'C:\Program Files\SWSimTool\SWSimTool.dll' }
if (-not (Test-Path -LiteralPath $dll)) { throw "DLL missing: $dll" }
& $regasm /codebase $dll
if ($LASTEXITCODE -ne 0) { throw "RegAsm failed: $LASTEXITCODE" }
$registered = Get-ItemProperty 'Registry::HKEY_CLASSES_ROOT\CLSID\{974a302b-3966-4e45-a5a5-1c24c26b7faa}\InprocServer32'
if ([Uri]::new($registered.CodeBase).LocalPath -ine $dll) {
    throw "Unexpected registered CodeBase: $($registered.CodeBase)"
}
Write-Host "Registered SWSimTool: $dll"
