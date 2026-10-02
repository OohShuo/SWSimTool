param (
    [string]$filename
 )

$repoDirectory = Split-Path -Parent $PSScriptRoot
$CommitVersion = 'source-archive'
if (Test-Path (Join-Path $repoDirectory '.git')) {
    $gitVersion = git -C $repoDirectory describe --tags --long --dirty --always
    if ($LASTEXITCODE -eq 0) { $CommitVersion = $gitVersion }
}
$FileContent = 'using System.Reflection;

[assembly: AssemblyInformationalVersion("{0}")]' -f $CommitVersion
$FileContent | Out-File $filename
