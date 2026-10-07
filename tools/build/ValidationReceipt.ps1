param([ValidateSet('Write','Verify')][string]$Mode='Verify',[string]$Configuration='Release',
      [string]$Root=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($Root)
$receipt=Join-Path $root "build/reports/validation-$Configuration.json"
function Hash([string]$Path) {
    $stream=[IO.File]::OpenRead($Path);$algorithm=[Security.Cryptography.SHA256]::Create()
    try {[BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$algorithm.Dispose()}
}
$inputs=@()
foreach($directory in @("build/bin/SWSimTool.SolidWorks/$Configuration/net48",'runtime/python')) {
    foreach($file in Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File | Where-Object {$_.Extension -in '.dll','.py','.txt'}) {
        $inputs+=@{path=$file.FullName.Substring($root.Length+1);sha256=(Hash $file.FullName)}
    }
}
if(!$inputs.Count){throw 'No production inputs to validate'}
if($Mode -eq 'Write') {
    @{status='PASS';configuration=$Configuration;inputs=$inputs} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receipt
} else {
    if(!(Test-Path -LiteralPath $receipt)){throw 'Mandatory tests have not passed for this configuration'}
    $data=Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
    if($data.status -ne 'PASS' -or $data.inputs.Count -ne $inputs.Count){throw 'Invalid validation receipt'}
    foreach($validatedInput in $inputs){$entry=@($data.inputs | Where-Object path -eq $validatedInput.path);if($entry.Count -ne 1 -or $entry[0].sha256 -ne $validatedInput.sha256){throw ('Production inputs changed after validation: '+$validatedInput.path)}}
}
