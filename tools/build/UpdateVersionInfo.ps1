param (
    [string]$filename,
    [string]$Version
 )

$repoDirectory = $(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$CommitVersion = 'source-archive'
if (Test-Path (Join-Path $repoDirectory '.git')) {
    $gitVersion = git -c ("safe.directory=" + $repoDirectory.Replace('\','/')) -C $repoDirectory describe --tags --long --dirty --always
    if ($LASTEXITCODE -eq 0) { $CommitVersion = $gitVersion }
}
$FileContent = 'using System.Reflection;

[assembly: AssemblyInformationalVersion("{0}")]' -f ($Version + '+' + $CommitVersion)
New-Item -ItemType Directory -Path (Split-Path -Parent $filename) -Force | Out-Null
# Preserve timestamps on unchanged inputs so normal SDK incremental builds do
# not rebuild every dependent assembly merely to rewrite identical metadata.
if (!(Test-Path -LiteralPath $filename) -or [IO.File]::ReadAllText($filename) -ne ($FileContent + [Environment]::NewLine)) {
    $FileContent | Set-Content -LiteralPath $filename -Encoding UTF8
}
