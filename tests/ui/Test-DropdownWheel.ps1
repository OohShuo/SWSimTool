param([string]$Payload='bin/SWSimTool.SolidWorks/Release/net48')
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$bin=Join-Path $root ('build/'+$Payload)
$sdk=$env:SOLIDWORKS_DIR
if(!$sdk){$sdk='D:/sw/sw2025/SOLIDWORKS'}
$packageRoot=$env:NUGET_PACKAGES
if(!$packageRoot){$packageRoot=Join-Path $env:USERPROFILE '.nuget/packages'}
$moq=Get-ChildItem "$packageRoot/moq/*/lib/net45/Moq.dll" | Select-Object -First 1 -ExpandProperty FullName
$castle=Get-ChildItem "$packageRoot/castle.core/*/lib/net45/Castle.Core.dll" | Select-Object -First 1 -ExpandProperty FullName
$tasks=Get-ChildItem "$packageRoot/system.threading.tasks.extensions/*/lib/portable-net45*/System.Threading.Tasks.Extensions.dll" | Select-Object -First 1 -ExpandProperty FullName
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/log4net.dll","$bin/MathNet.Numerics.dll",$moq,$castle,$tasks,"$sdk/SolidWorks.Interop.sldworks.dll","$sdk/SolidWorks.Interop.swconst.dll",'System.Core','System.Xml','System.Runtime.Serialization','System.Windows.Forms','System.Drawing')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.Windows.Forms;using SWSimTool.UI;
public class WheelProbe : NoWheelComboBox {
 public void Wheel(int delta,bool horizontal=false){var message=Message.Create(Handle,horizontal?0x020E:0x020A,new IntPtr((long)(ushort)(short)delta<<16),IntPtr.Zero);base.WndProc(ref message);}
}
public static class DropdownWheelTest {
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 public static void Run(){
  using(var box=new WheelProbe()){
   Check(box.DropDownStyle==ComboBoxStyle.DropDown,"WinForms editable combo behavior changed");box.Items.AddRange(new[]{"one","two","three"});box.SelectedIndex=1;
   foreach(var delta in new[]{120,-120,240,-240,60,-60}){box.Wheel(delta);box.Wheel(delta,true);Check(box.SelectedIndex==1,"Wheel changed WinForms selection");}
   box.SelectedIndex=2;Check(box.SelectedIndex==2,"Ordinary WinForms selection was disabled");
  }
  Console.WriteLine("PASS: existing WinForms wheel behavior and ordinary selection preserved; no native wheel interception");
 }
}
'@
[DropdownWheelTest]::Run()
