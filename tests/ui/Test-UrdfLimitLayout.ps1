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
$refs=@("$bin/SWSimTool.dll","$bin/SWSimTool.Core.dll","$bin/SWSimTool.Application.dll","$bin/SWSimTool.Infrastructure.dll","$bin/log4net.dll","$bin/MathNet.Numerics.dll",$moq,$castle,$tasks,"$sdk/SolidWorks.Interop.sldworks.dll",'System.Core','System.Xml','System.Runtime.Serialization','System.Windows.Forms','System.Drawing')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
[SWSimTool.Utilities.Logger].GetField('Initialized',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.Drawing;using System.Reflection;using System.Windows.Forms;using SWSimTool.UI;using SWSimTool.URDF;
public static class UrdfLimitLayoutTest {
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 public static void Run(){
  using(var control=new UrdfJointLimitControl()){
   var inputs=(TextBox[])typeof(UrdfJointLimitControl).GetField("inputs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(control);
   var labels=(Label[])typeof(UrdfJointLimitControl).GetField("labels",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(control);
   var joint=new Joint{Type="revolute"};joint.Limit.SetInputs("-1","1","7","2.5");
   control.Width=190;control.LoadJoint(joint,false);int four=control.ContentHeight;
   Check(four>0&&control.Height==four,"Control must fit visible rows");
   Check(control.PageHeight>four,"Native host must reserve all four rows plus bottom spacing");
   Check(Math.Max(inputs[3].Bottom,labels[3].Bottom)==four,"Blank space remains below last input");
   int events=0;control.ContentHeightChanged+=(s,e)=>events++;
   control.SetType("continuous");int two=control.ContentHeight;
   Check(control.PageHeight>two,"Native host must reserve both continuous rows");
   Check(two<four&&inputs[2].Top<10&&Math.Max(inputs[3].Bottom,labels[3].Bottom)==two,"Hidden bounds must not reserve rows");
   control.SetType("fixed");Check(control.ContentHeight==0&&!control.HasParameters&&control.PageHeight==1,"Fixed joint reserves parameter area");
   control.SetType("prismatic");control.Commit(joint);
   Check(joint.Limit.Lower==-1&&joint.Limit.Upper==1&&joint.Limit.Effort==7&&joint.Limit.Velocity==2.5,"Type change lost input");
   Check(labels[0].Text.Contains("m")&&labels[3].Text.Contains("m/s"),"Prismatic units changed incorrectly");
   var retained=inputs[0];control.Width=150;control.Font=new Font(control.Font.FontFamily,12);
   Check(Object.ReferenceEquals(retained,inputs[0])&&inputs[0].Text=="-1","Resize recreated inputs");
   Check(Math.Max(inputs[3].Bottom,labels[3].Bottom)==control.ContentHeight,"Font/width change left trailing space");
   Check(events>=3&&control.PageHeight>control.ContentHeight,"Host height must cover scaled/wrapped rows");
   Console.WriteLine("PASS: compact four/two/zero rows, width/font resize and hidden input retention");
  }
 }
}
'@
[UrdfLimitLayoutTest]::Run()
