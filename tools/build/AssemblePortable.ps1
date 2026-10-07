param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
& "$root/tools/build/ValidationReceipt.ps1" -Mode Verify -Configuration $Configuration
& dotnet publish "$root/src/SWSimTool.Cli/SWSimTool.Cli.csproj" --no-build --no-restore -c $Configuration -f net8.0 -o "$root/build/cli-release"
if($LASTEXITCODE -ne 0){throw 'Portable CLI assembly failed'}
