param(
    [switch]$Test,[switch]$Package,[switch]$Installer,[switch]$Clean,
    [string]$Configuration='Release',
    [string]$SolidWorksDir=$env:SOLIDWORKS_DIR,
    [string]$Python='python', [string]$MSBuild='', [string]$ISCC=''
)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$build=Join-Path $root 'build'
if($Clean) {
    if($Test -or $Package -or $Installer) { throw '-Clean cannot be combined with build options' }
    if([IO.Path]::GetFullPath($build) -ne [IO.Path]::Combine([IO.Path]::GetFullPath($root),'build')) {throw 'Unsafe clean path'}
    if(Test-Path $build) {Remove-Item -LiteralPath $build -Recurse -Force}
    return
}
if(!$SolidWorksDir) {$SolidWorksDir='C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS'}
if(!(Test-Path "$SolidWorksDir/SolidWorks.Interop.sldworks.dll")) {throw 'Set -SolidWorksDir or SOLIDWORKS_DIR to the installed SolidWorks SDK directory'}
if(!$MSBuild) {
    $vswhere="${env:ProgramFiles(x86)}/Microsoft Visual Studio/Installer/vswhere.exe"
    if(Test-Path $vswhere) {$MSBuild=(& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1)}
    if(!$MSBuild) {$MSBuild=(Get-Command MSBuild.exe -ErrorAction Stop).Source}
}
New-Item -ItemType Directory -Path "$build/logs","$build/reports","$build/test-results","$build/test-work","$build/native-parity","$build/integration","$build/dist" -Force | Out-Null
$env:PYTHONDONTWRITEBYTECODE='1'
$env:SWSIMTOOL_TEST_CONFIGURATION=$Configuration
$env:TEMP="$build/test-work"
$env:TMP=$env:TEMP
function Invoke-Gate([string]$Name,[scriptblock]$Action) {
    Write-Host "Running $Name"
    $savedPreference=$ErrorActionPreference
    try {$ErrorActionPreference='Continue'; & $Action 2>&1 | ForEach-Object { $_.ToString() } | Set-Content "$build/logs/$Name.log"; $code=$LASTEXITCODE} finally {$ErrorActionPreference=$savedPreference}
    if($code -ne 0) {Get-Content "$build/logs/$Name.log" -Tail 30; throw "$Name failed (see build/logs/$Name.log)"}
}
Push-Location $root
try {
    Invoke-Gate 'restore' { & $MSBuild SWSimTool.sln /t:Restore /v:minimal }
    $projects=@('src/SWSimTool.SolidWorks/SWSimTool.SolidWorks.csproj')
    if($Test -or $Package -or $Installer) {$projects+=@('tests/core/CoreTests.csproj','tests/parity/CandidateRunner.csproj','tests/upstream/SWSimTool.Tests.csproj','tests/upstream/runner/TestRunner.csproj')}
    foreach($project in $projects) {
        $name=[IO.Path]::GetFileNameWithoutExtension($project)
        Invoke-Gate "build-$name" { & $MSBuild $project /restore /t:Build "/p:Configuration=$Configuration" /p:Platform=x64 "/p:SolidWorksDir=$SolidWorksDir" "/p:SolutionDir=$root\" /v:minimal }
    }
    if($Test -or $Package -or $Installer) {
        & "$root/tools/build/RunTests.ps1" -Configuration $Configuration -Python $Python
    }
    if($Package -or $Installer) {
        & "$root/tools/build/AssembleRelease.ps1" -Configuration $Configuration -Python $Python
        Invoke-Gate 'payload-validation' { & $Python -B tests/architecture/Test-ReleaseLayout.py }
    }
    if($Installer) {
        if(!$ISCC) {
            foreach($candidate in @("${env:ProgramFiles(x86)}/Inno Setup 6/ISCC.exe","$build/tools/InnoSetup/ISCC.exe")) {if(Test-Path $candidate) {$ISCC=$candidate;break}}
            if(!$ISCC) {$ISCC=(Get-Command ISCC.exe -ErrorAction Stop).Source}
        }
        Invoke-Gate 'installer' { & $ISCC INSTALL/install.iss }
        foreach($setup in Get-ChildItem "$build/dist/SWSimTool_*_Setup.exe") {
            if($setup.Length -lt 100000) {throw 'Invalid installer output'}
            $hash=(Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($setup.Name)" | Set-Content -LiteralPath ($setup.FullName+'.sha256') -Encoding ASCII
        }
    }
    Invoke-Gate 'workspace-cleanliness' { & $Python -B tests/architecture/Test-CleanWorkspace.py }
} finally {Pop-Location}
