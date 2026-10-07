param([string]$Payload="")
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
. "$root/tools/build/CadProcessGuard.ps1"
foreach($case in @(
    @{Threads=0;Handles=0;Expected=$false},
    @{Threads=1;Handles=0;Expected=$true},
    @{Threads=0;Handles=1;Expected=$true},
    @{Threads=$null;Handles=0;Expected=$true},
    @{Threads=0;Handles=$null;Expected=$true}
)) {
    $active=Test-SolidWorksProcessActive ([pscustomobject]@{ThreadCount=$case.Threads;HandleCount=$case.Handles})
    if($active -ne $case.Expected){throw 'CAD process guard did not fail closed'}
}
$installer=[IO.File]::ReadAllText("$root/INSTALL/install.iss")
if(!$installer.Contains('ThreadCount > 0 OR HandleCount > 0 OR ThreadCount IS NULL OR HandleCount IS NULL')){throw 'Installer process guard differs from tested predicate'}
'PASS: active and unknown CAD processes block; threadless handleless remnants do not'
