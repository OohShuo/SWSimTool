param([Parameter(Mandatory=$true)][string]$Path)
$stream=[IO.File]::OpenRead($Path)
$algorithm=[Security.Cryptography.SHA256]::Create()
try {
    [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','').ToLowerInvariant()
} finally {$stream.Dispose();$algorithm.Dispose()}
