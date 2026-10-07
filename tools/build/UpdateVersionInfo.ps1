param (
    [string]$filename
 )

$repoDirectory = $(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$CommitVersion = 'source-archive'
if (Test-Path (Join-Path $repoDirectory '.git')) {
    $gitVersion = git -c safe.directory=$repoDirectory -C $repoDirectory describe --tags --long --dirty --always
    if ($LASTEXITCODE -eq 0) { $CommitVersion = $gitVersion }
}
$FileContent = 'using System.Reflection;

[assembly: AssemblyInformationalVersion("{0}")]' -f $CommitVersion
New-Item -ItemType Directory -Path (Split-Path -Parent $filename) -Force | Out-Null
$FileContent | Set-Content -LiteralPath $filename -Encoding UTF8
