# A process object can remain enumerable after its last thread and handle have gone.
# Unknown counts remain blocking; this does not terminate any process.
function Test-SolidWorksProcessActive($Process) {
    return $null -eq $Process.ThreadCount -or $null -eq $Process.HandleCount -or $Process.ThreadCount -gt 0 -or $Process.HandleCount -gt 0
}
function Get-ActiveSolidWorksProcesses {
    Get-CimInstance Win32_Process -Filter "Name='SLDWORKS.exe'" -ErrorAction Stop | Where-Object {Test-SolidWorksProcessActive $_}
}
