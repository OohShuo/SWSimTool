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
using System;using System.Collections.Generic;using System.Reflection;using System.Runtime.InteropServices;using System.Windows.Forms;using Moq;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;using SWSimTool.UI;
public class WheelProbe : NoWheelComboBox {
 public bool SawObservedWheel,SawKeyWithoutWheel;
 [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
 public void PostWheel(){if(!PostMessage(Handle,0x020A,new IntPtr(120L<<16),IntPtr.Zero))throw new Exception("Cannot enqueue test wheel");}
 public void PostKey(){if(!PostMessage(Handle,0x0100,new IntPtr(40),IntPtr.Zero))throw new Exception("Cannot enqueue test key");}
 protected override void WndProc(ref Message message){if(message.Msg==0x0100)SawKeyWithoutWheel=!DropdownWheelGuard.IsWheelInput;if(message.Msg==0x020A)SawObservedWheel=DropdownWheelGuard.IsWheelInput;base.WndProc(ref message);}
 public void Wheel(int delta,bool horizontal=false){var message=Message.Create(Handle,horizontal?0x020E:0x020A,new IntPtr((long)(ushort)(short)delta<<16),IntPtr.Zero);base.WndProc(ref message);}
}
public static class DropdownWheelTest {
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static void WheelFlag(DropdownWheelGuard guard,bool value){var state=typeof(DropdownWheelGuard).GetField("owned",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(guard);state.GetType().GetField("Wheel").SetValue(state,value);}
 public static void Run(){
  using(var box=new WheelProbe()){
   Check(box.DropDownStyle==ComboBoxStyle.DropDown,"General wheel guard changed editable combo behavior");box.Items.AddRange(new[]{"one","two","three"});box.SelectedIndex=1;
   foreach(var delta in new[]{120,-120,240,-240,60,-60}){box.Wheel(delta);box.Wheel(delta,true);Check(box.SelectedIndex==1,"Wheel changed WinForms selection");}
   box.SelectedIndex=2;Check(box.SelectedIndex==2,"Ordinary selection was disabled");
  }
  using(var guard=new DropdownWheelGuard()){
   using(var posted=new WheelProbe()){posted.Items.AddRange(new[]{"a","b"});posted.SelectedIndex=0;posted.PostWheel();Application.DoEvents();Check(posted.SawObservedWheel&&posted.SelectedIndex==0,"UI-thread hook did not identify queued wheel before dispatch");posted.PostKey();Application.DoEvents();Check(posted.SawKeyWithoutWheel,"Key message did not clear wheel context");}
   var box=new Mock<IPropertyManagerPageCombobox>();box.SetupAllProperties();box.Object.CurrentSelection=0;guard.Register(77,box.Object);
   box.Object.CurrentSelection=1;Check(!guard.RejectSelection(77),"Normal selection was rejected");
   WheelFlag(guard,true);box.Object.CurrentSelection=2;Check(guard.RejectSelection(77)&&box.Object.CurrentSelection==1,"Wheel did not restore prior native selection");
   Check(!guard.RejectSelection(900),"Unregistered SW control was altered");
   using(var second=new DropdownWheelGuard()){WheelFlag(guard,true);Check(DropdownWheelGuard.IsWheelInput,"Nested guard lost thread observer");}
   Check(DropdownWheelGuard.IsWheelInput,"Closing nested guard disabled active observer");WheelFlag(guard,false);
   int writes=0;string value="a";Mock<IPropertyManagerPageCombobox> choice=null;
   var group=new Mock<IPropertyManagerPageGroup>();group.Setup(g=>g.AddControl2(It.IsAny<int>(),It.IsAny<short>(),It.IsAny<string>(),It.IsAny<short>(),It.IsAny<int>(),It.IsAny<string>())).Returns((int id,short type,string caption,short align,int options,string tip)=>{
    if(type==(short)swPropertyManagerPageControlType_e.swControlType_Label){var label=new Mock<IPropertyManagerPageLabel>();label.SetupAllProperties();label.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)label.Object;}
    choice=new Mock<IPropertyManagerPageCombobox>();choice.SetupAllProperties();choice.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)choice.Object;
   });
   using(var fields=new NativeParameterFields(group.Object,()=>{})){
    fields.Choice("type",()=>"Type",new[]{"a","b"},()=>value,v=>{value=v;writes++;});fields.LoadValues();WheelFlag(guard,true);choice.Object.CurrentSelection=1;fields.OnChoice(1001,1);
    Check(value=="a"&&writes==0&&choice.Object.CurrentSelection==0,"Native wheel changed business draft");
    WheelFlag(guard,false);fields.OnChoice(1001,1);Check(value=="b"&&writes==1,"Click/key selection did not update model");
   }
   WheelFlag(guard,false);
  }
  Check(!DropdownWheelGuard.IsWheelInput,"Closed guard left active wheel state");
  Console.WriteLine("PASS: WinForms vertical/horizontal wheel suppression, preserved editing/selection, native rollback, unchanged draft and observer disposal");
 }
}
'@
[DropdownWheelTest]::Run()
