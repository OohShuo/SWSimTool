param([ValidateSet('Fast','Medium','SolidWorksIntegration','Release')][string]$Tier='Fast',
      [string]$Python='python',[string]$SolidWorksDir=$env:SOLIDWORKS_DIR,
      [string]$Configuration='Release',[int[]]$OwnedStaleProcessIds=@())
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Push-Location $root
try {
    New-Item -ItemType Directory -Path build/logs,build/reports,build/test-work -Force | Out-Null
    $env:PYTHONDONTWRITEBYTECODE='1';$env:SWSIMTOOL_TEST_CONFIGURATION=$Configuration
    $env:TEMP=Join-Path $root 'build/test-work';$env:TMP=$env:TEMP
    $env:SWSIMTOOL_CACHE=Join-Path $root 'build/test-work/suite-backend-cache'
    $env:SWSIMTOOL_MESH_CACHE=Join-Path $root 'build/test-work/suite-backend-mesh-cache'
    function Gate([string]$name,[scriptblock]$run) {
        Write-Host "Testing $Tier/$name"
        $saved=$ErrorActionPreference
        try {$ErrorActionPreference='Continue'; & $run 2>&1 | ForEach-Object {$_.ToString()} | Set-Content "build/logs/$Tier-$name.log";$code=$LASTEXITCODE}finally{$ErrorActionPreference=$saved}
        if($code -ne 0){Get-Content "build/logs/$Tier-$name.log" -Tail 20;throw "$Tier/$name failed"}
    }
    if($Tier -in 'Fast','Medium') {
        Gate 'build' { & dotnet build tests/parity/CandidateRunner.csproj -c $Configuration }
        $candidate=Join-Path $root "build/bin/CandidateRunner/$Configuration/net48/SWSimTool.CandidateRunner.exe"
        Gate 'domain' { & $candidate --selftest }
        Gate 'domain-net8' { & dotnet "$root/build/bin/CandidateRunner/$Configuration/net8.0/SWSimTool.CandidateRunner.dll" --selftest }
        Gate 'architecture' { & $Python -B tests/architecture/Test-LayeredArchitecture.py }
        Gate 'sdk' { & $Python -B tests/architecture/Test-SdkBuild.py }
        Gate 'publication-receipt' { & $Python -B tests/architecture/Test-ValidationReceipt.py }
        if($Tier -eq 'Medium') {
            Gate 'modern-host' { & $Python -B tests/targets/Test-ModernHost.py }
            Gate 'cross-target-parity' { & $Python -B -c "import os,subprocess,sys;sys.exit(subprocess.call([sys.executable,'-B','tests/parity/run.py'],env=dict(os.environ,SWSIMTOOL_TEST_FRAMEWORK='net8.0',SWSIMTOOL_CROSS_TARGET='1')))" }
            Gate 'backend' { & $Python -B -m unittest discover -s tests/backend -v }
            Gate 'parity' { & $Python -B tests/parity/run.py }
            Gate 'tools' { & $candidate --tooltest $Python tests/tools/fake_tool.py }
        }
    } else {
        & ./build.ps1 -Test -SolidWorksDir $SolidWorksDir -Python $Python -Configuration $Configuration
        Gate 'native-cad' { & powershell -NoProfile -File tests/incremental/Verify-NativeIncrementalV1.ps1 -Payload "bin/SWSimTool.SolidWorks/$Configuration/net48" -NativeOnly -IdentityLifecycle -CadReferenceLifecycle }
        Gate 'native-physics' { & $Python -B tests/incremental/Compare-NativeIncremental.py }
        # Invoke in a fresh process to avoid loading different copies of the COM host in one AppDomain.
        & powershell -NoProfile -Command "& './tests/compatibility/Test-V2Cad.ps1' -OwnedLegacyFixture 'tests/fixtures/sw2mujoco-v2/cad' -Payload 'bin/SWSimTool.SolidWorks/$Configuration/net48' -OwnedStaleProcessIds @($($OwnedStaleProcessIds -join ','))"
        if($LASTEXITCODE -ne 0){throw 'Frozen CAD compatibility failed'}
        if($Tier -eq 'Release') { & "$root/tools/build/ReleaseSmoke.ps1" -Python $Python -SolidWorksDir $SolidWorksDir -Configuration $Configuration }
    }
    @{status='PASS';tier=$Tier;configuration=$Configuration} | ConvertTo-Json | Set-Content "build/reports/suite-$Tier.json"
} finally {Pop-Location}
