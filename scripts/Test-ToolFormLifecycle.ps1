param([string]$Payload='stable-audit')
$ErrorActionPreference='Stop'
$workspacePath=Split-Path -Parent $PSScriptRoot
$payloadPath=Join-Path $workspacePath ('build\'+$Payload)
[Reflection.Assembly]::LoadFrom((Join-Path $payloadPath 'SW2URDF.dll')) | Out-Null
Add-Type -AssemblyName System.Windows.Forms
$taskFolder=Join-Path $workspacePath ('build\tool-form-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskFolder | Out-Null
$form=[SW2URDF.UI.MuJoCoToolsForm]::new((Join-Path $taskFolder 'preferences.json'))
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$formType=$form.GetType()
$stop=[Threading.CancellationTokenSource]::new()
try {
 $formType.GetField('operation',$flags).SetValue($form,$stop)
 $formType.GetField('busy',$flags).SetValue($form,$true)
 $event=[Windows.Forms.FormClosingEventArgs]::new([Windows.Forms.CloseReason]::UserClosing,$false)
 [Windows.Forms.Form].GetMethod('OnFormClosing',$flags).Invoke($form,@($event)) | Out-Null
 if(!$event.Cancel -or !$stop.IsCancellationRequested -or !$formType.GetField('closeWhenStopped',$flags).GetValue($form)) {throw 'Busy form did not defer close and cancel backend'}
 'PASS: close requests backend cancellation and defers disposal'
 $formType.GetField('busy',$flags).SetValue($form,$false)
 $event=[Windows.Forms.FormClosingEventArgs]::new([Windows.Forms.CloseReason]::UserClosing,$false)
 [Windows.Forms.Form].GetMethod('OnFormClosing',$flags).Invoke($form,@($event)) | Out-Null
 if($event.Cancel) {throw 'Idle form refuses close'}
 'PASS: idle form closes and persists preferences'
 if(!(Test-Path -LiteralPath (Join-Path $taskFolder 'preferences.json'))) {throw 'Preferences missing'}
} finally {$form.Dispose();$stop.Dispose()}
