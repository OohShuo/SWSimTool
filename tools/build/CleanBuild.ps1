param([Parameter(Mandatory=$true)][string]$BuildDirectory)
$ErrorActionPreference='Stop'
$cleanRoot=[IO.Path]::GetFullPath($BuildDirectory).TrimEnd('\','/')
if([IO.Path]::GetFileName($cleanRoot) -ne 'build') {throw 'Clean target must be a build directory'}
if(!(Test-Path -LiteralPath $cleanRoot)) {return}
if((Get-Item -LiteralPath $cleanRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {throw 'Refusing to clean a linked build directory'}
# Only known outputs are disposable. Tools, worktrees, caches and unclassified
# historical files remain untouched; never infer ownership from a name prefix.
$outputs=@('bin','obj','logs','reports','test-results','test-work','native-parity','native-parity-obj','integration','dist','runtime-release','docs')
$failures=New-Object 'System.Collections.Generic.List[string]'
$residuals=New-Object 'System.Collections.Generic.List[string]'
$temporaryRoot=Join-Path $cleanRoot 'test-work'
function Remove-Output([string]$Path) {
    $resolved=[IO.Path]::GetFullPath($Path)
    if(!$resolved.StartsWith($cleanRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {throw 'Output escaped build directory'}
    try {
        $item=Get-Item -LiteralPath $resolved -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            $failures.Add("Linked output preserved: $resolved")
            return
        }
        if($item.PSIsContainer) {
            if(Test-Path -LiteralPath (Join-Path $resolved '.git')) {
                $failures.Add("Git checkout preserved: $resolved")
                return
            }
            foreach($child in @(Get-ChildItem -LiteralPath $resolved -Force)) {Remove-Output $child.FullName}
            # No -Recurse: a failed child or link must remain intact.
            if(@(Get-ChildItem -LiteralPath $resolved -Force).Count -eq 0) {Remove-Item -LiteralPath $resolved -Force}
        } else {Remove-Item -LiteralPath $resolved -Force}
    } catch {
        $temporary=($resolved -eq $temporaryRoot) -or $resolved.StartsWith($temporaryRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
        $accessFailure=$_.CategoryInfo.Category -in @('PermissionDenied','SecurityError')
        $exception=$_.Exception
        while($exception) {
            if($exception -is [UnauthorizedAccessException] -or $exception -is [Security.SecurityException] -or $exception -is [IO.IOException]) {$accessFailure=$true}
            $exception=$exception.InnerException
        }
        $message="$resolved : $($_.Exception.Message)"
        if($temporary -and $accessFailure) {$residuals.Add($message)} else {$failures.Add($message)}
    }
}
foreach($name in $outputs) {
    $target=Join-Path $cleanRoot $name
    if(Test-Path -LiteralPath $target) {Remove-Output $target}
}
foreach($residual in $residuals) {Write-Warning "Temporary test output retained: $residual"}
if($failures.Count) {
    foreach($failure in $failures) {Write-Warning $failure}
    throw 'Clean incomplete: some outputs were inaccessible or protected. No permissions were changed; see warnings.'
}
if($residuals.Count) {
    Write-Host 'Build outputs cleaned with warnings; inaccessible temporary test files remain.'
} else {Write-Host 'Cleaned known build outputs; tools, worktrees and other files preserved.'}
