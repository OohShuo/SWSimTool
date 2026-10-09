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
using System;using System.Collections.Generic;using Moq;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;using SWSimTool.UI;using SWSimTool.URDF;
public static class UrdfLimitLayoutTest {
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 public static void Run(){
  var labels=new List<Mock<IPropertyManagerPageLabel>>();var inputs=new List<Mock<IPropertyManagerPageTextbox>>();var ids=new HashSet<int>();
  var group=new Mock<IPropertyManagerPageGroup>();
  group.Setup(g=>g.AddControl2(It.IsAny<int>(),It.IsAny<short>(),It.IsAny<string>(),It.IsAny<short>(),It.IsAny<int>(),It.IsAny<string>())).Returns((int id,short type,string caption,short align,int options,string tip)=>{
   Check(ids.Add(id),"Duplicate native control ID");
   Check(type==(short)(id%2==0?swPropertyManagerPageControlType_e.swControlType_Label:swPropertyManagerPageControlType_e.swControlType_Textbox),"Parameters must request native controls, not WindowFromHandle");
   if(id%2==0){var label=new Mock<IPropertyManagerPageLabel>();label.SetupAllProperties();label.As<IPropertyManagerPageControl>().SetupAllProperties();labels.Add(label);return (object)label.Object;}
   var input=new Mock<IPropertyManagerPageTextbox>();input.SetupAllProperties();input.As<IPropertyManagerPageControl>().SetupAllProperties();inputs.Add(input);return (object)input.Object;
  });
  var fields=new UrdfJointLimitFields(group.Object);
  Check(labels.Count==4&&inputs.Count==4,"All fields must be native labels/textboxes");
  var joint=new Joint{Type="revolute"};joint.Limit.SetInputs("-1","1","7","2.5");fields.LoadJoint(joint,false);
  for(int i=0;i<4;i++)Check(((IPropertyManagerPageControl)inputs[i].Object).Visible&&((IPropertyManagerPageControl)labels[i].Object).Visible,"Bounded fields not visible");
  inputs[0].Object.Text="-2";inputs[2].Object.Text="9";fields.SetType("continuous");
  for(int i=0;i<4;i++)Check(((IPropertyManagerPageControl)inputs[i].Object).Visible==(i>=2),"Continuous bounds visibility");
  fields.SetType("fixed");foreach(var input in inputs)Check(!((IPropertyManagerPageControl)input.Object).Visible,"Fixed must hide all fields");
  fields.SetType("prismatic");fields.Commit(joint);
  Check(joint.Limit.Lower==-2&&joint.Limit.Upper==1&&joint.Limit.Effort==9&&joint.Limit.Velocity==2.5,"Native type switching lost edited values");
  Check(labels[0].Object.Caption=="下限 (m)"&&labels[2].Object.Caption=="最大力/力矩 (N)"&&labels[3].Object.Caption=="最大速度 (m/s)","Prismatic units");
  fields.LoadJoint(joint,true);foreach(var input in inputs)Check(!((IPropertyManagerPageControl)input.Object).Visible,"Root must hide parameters");
  inputs[2].Object.Text="";inputs[3].Object.Text="";fields.Commit(joint);Check(joint.Limit.GetInputTexts("G17")[2]==""&&joint.Limit.GetInputTexts("G17")[3]=="","Blank limits must remain blank");
  string before=string.Join("|",joint.Limit.GetInputTexts("G17"));inputs[0].Object.Text="5";
  bool rejected=false;try{fields.Commit(joint);}catch(ArgumentException){rejected=true;}Check(rejected&&string.Join("|",joint.Limit.GetInputTexts("G17"))==before,"Invalid native input partially changed model");
  Check(fields.GetType().BaseType==typeof(object),"Limit fields must not own a WinForms HWND");
  Console.WriteLine("PASS: eight native controls, unique IDs, type/root visibility, units, hidden value retention and atomic input validation");
 }
}
'@
[UrdfLimitLayoutTest]::Run()
