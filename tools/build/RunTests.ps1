param([string]$Configuration='Release',[string]$Python='python')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$env:PYTHONDONTWRITEBYTECODE='1'
$env:SWSIMTOOL_TEST_CONFIGURATION=$Configuration
function Run([string]$name,[scriptblock]$command) {
    Write-Host "Testing $name"
    $savedPreference=$ErrorActionPreference
    try {$ErrorActionPreference='Continue'; & $command 2>&1 | ForEach-Object { $_.ToString() } | Set-Content "$root/build/logs/test-$name.log"; $code=$LASTEXITCODE} finally {$ErrorActionPreference=$savedPreference}
    if($code -ne 0) {Get-Content "$root/build/logs/test-$name.log" -Tail 40; throw "$name failed"}
}
Run 'backend' { & $Python -B -m unittest discover -s tests/backend -v }
Run 'parity' { & $Python -B tests/parity/run.py }
Run 'publication-receipt' { & $Python -B tests/architecture/Test-ValidationReceipt.py }
Run 'sdk-build' { & $Python -B tests/architecture/Test-SdkBuild.py }
Run 'architecture' { & $Python -B tests/architecture/Test-LayeredArchitecture.py }
Run 'core' { & "$root/build/bin/CoreTests/$Configuration/net48/CoreTests.exe" }
Run 'tools' { & $Python -B -c "import os,subprocess,sys;sys.exit(subprocess.call(sys.argv[1:],env=dict(os.environ)))" "$root/build/bin/CandidateRunner/$Configuration/net48/SWSimTool.CandidateRunner.exe" --tooltest $Python "$root/tests/tools/fake_tool.py" }
foreach($path in @('integration/install/Test-CadProcessGuard.ps1','architecture/Test-ProductionBoundary.ps1','compatibility/Test-V2Persistence.ps1','identity/Test-StableReferenceAudit.ps1','identity/Test-LinkModeIdentity.ps1','model/Test-ResolvedCadModel.ps1','model/Test-SimulationConfigBuilder.ps1','ui/Test-CollisionNavigation.ps1','ui/Test-ToolFormLifecycle.ps1')) {
    Run ([IO.Path]::GetFileNameWithoutExtension($path)) { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$root/tests/$path" -Payload "bin/SWSimTool.SolidWorks/$Configuration/net48" }
}

& "$root/tools/build/ValidationReceipt.ps1" -Mode Write -Configuration $Configuration
