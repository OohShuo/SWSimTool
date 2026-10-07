param([string]$Python='python',[string]$SolidWorksDir=$env:SOLIDWORKS_DIR,[string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
 . "$root/tools/build/CadProcessGuard.ps1"
if(Test-Path (Join-Path $root '.git')) {
    $status=& git -c "safe.directory=$root" -C $root status --porcelain
    if($LASTEXITCODE -ne 0 -or $status){throw 'Release smoke requires a clean committed checkout'}
}
if(Get-ActiveSolidWorksProcesses){throw 'Close SolidWorks before release installation smoke; existing processes are never terminated'}
& "$root/build.ps1" -Installer -Python $Python -SolidWorksDir $SolidWorksDir -Configuration $Configuration
$setup=Get-ChildItem "$root/build/dist/SWSimTool_*_Setup.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$process=Start-Process -FilePath $setup.FullName -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-' -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0){throw "Setup failed: $($process.ExitCode)"}
foreach($file in Get-ChildItem "$root/build/runtime-release/*.dll") {
    if((Get-FileHash $file.FullName).Hash -ne (Get-FileHash (Join-Path 'C:/Program Files/SWSimTool' $file.Name)).Hash){throw "Installed payload differs: $($file.Name)"}
}
& powershell -NoProfile -File "$root/tests/integration/install/Test-InstalledPlugin.ps1"
if($LASTEXITCODE -ne 0){throw 'Installed COM load failed'}
for($attempt=0;$attempt -lt 30 -and (Get-ActiveSolidWorksProcesses);$attempt++){Start-Sleep -Milliseconds 500}
if(Get-ActiveSolidWorksProcesses){throw 'COM server is still running; uninstall smoke cannot proceed safely'}
$process=Start-Process -FilePath 'C:/Program Files/SWSimTool/unins000.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -WindowStyle Hidden -Wait -PassThru
if($process.ExitCode -ne 0 -or (Test-Path 'C:/Program Files/SWSimTool/SWSimTool.dll')){throw 'Uninstall cleanup failed'}
if(Test-Path 'Registry::HKEY_LOCAL_MACHINE/SOFTWARE/SolidWorks/Addins/{974a302b-3966-4e45-a5a5-1c24c26b7faa}'){throw 'Addin registration remains after uninstall'}
@{status='PASS';install=$true;payload=$true;COM=$true;uninstall=$true} | ConvertTo-Json | Set-Content "$root/build/reports/release-smoke.json"
