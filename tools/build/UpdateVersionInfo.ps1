param (
    [string]$filename
 )

$repoDirectory = $(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SW2URDF.sln')) { $p; break } })
$CommitVersion = 'source-archive'
if (Test-Path (Join-Path $repoDirectory '.git')) {
    $gitVersion = git -c safe.directory=$repoDirectory -C $repoDirectory describe --tags --long --dirty --always
    if ($LASTEXITCODE -eq 0) { $CommitVersion = $gitVersion }
}
$FileContent = 'using System.Reflection;

[assembly: AssemblyInformationalVersion("{0}")]' -f $CommitVersion
$FileContent | Out-File $filename
