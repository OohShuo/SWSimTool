param([Parameter(Mandatory=$true)][int]$ProcessId,[Parameter(Mandatory=$true)][string]$FixtureDirectory,[string]$Payload='collision-ui-final')
$ErrorActionPreference='Stop'
$workspacePath=Split-Path -Parent $PSScriptRoot
$fixturePath=(Resolve-Path -LiteralPath $FixtureDirectory).Path
$ownedRoot=[IO.Path]::GetFullPath((Join-Path $workspacePath 'build'))+[IO.Path]::DirectorySeparatorChar
if(-not $fixturePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Only generated workspace fixtures are permitted'}
if([int](Get-Content -LiteralPath (Join-Path $fixturePath 'solidworks-process.txt')) -ne $ProcessId){throw 'Fixture process marker mismatch'}
$interopDirectory='D:\sw\sw2025\SOLIDWORKS'
$payloadDirectory=Join-Path $workspacePath ('build\'+$Payload)
$references=@("$interopDirectory\SolidWorks.Interop.sldworks.dll","$interopDirectory\SolidWorks.Interop.swconst.dll","$interopDirectory\SolidWorks.Interop.swpublished.dll","$payloadDirectory\SW2URDF.dll","$payloadDirectory\MathNet.Numerics.dll",'System.Windows.Forms','System.Drawing','System.Runtime.Serialization','System.Web.Extensions','System.Xml','System.Core')
$references | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
public static class CollisionPageFlowProbe {
 public static void Run(int pid,string directory){
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
  Application.ThreadException+=(s,e)=>{Console.WriteLine("UI ERROR: "+e.Exception);File.AppendAllText(Path.Combine(directory,"page-errors.txt"),e.Exception.ToString()+System.Environment.NewLine);};
  var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
  if(sw.GetProcessID()!=pid)throw new Exception("Different SolidWorks process; refusing");
  var model=(ModelDoc2)sw.ActiveDoc;
  if(model==null||!string.Equals(model.GetPathName(),Path.Combine(directory,"isolated_fixture.SLDASM"),StringComparison.OrdinalIgnoreCase))throw new Exception("Only the generated fixture may be used");
  var page=new ExportPropertyManager(sw);
  if(!page.LoadConfigTree())throw new Exception("Fixture URDF tree failed to load");
  var tree=page.Tree;
  var original=(SW2URDF.URDF.LinkNode)tree.SelectedNode;var components=original.Link.GetType().GetField("SWComponents");int componentCount=((System.Collections.IList)components.GetValue(original.Link)).Count;
  object checkedPage=null;
  var observer=new Timer{Interval=250};
  observer.Tick+=(s,e)=>{
   var collision=typeof(ExportPropertyManager).GetField("collisionPage",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(page);
   if(collision==null||collision==checkedPage)return;
   var editor=(Control)collision.GetType().GetField("editor",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(collision);
   if(!editor.IsDisposed)return;
   checkedPage=collision;
   if(((System.Collections.IList)components.GetValue(original.Link)).Count!=componentCount||tree.SelectedNode!=original)throw new Exception("Return changed selected link or component ownership");
   Console.WriteLine("PASS: returned to original selected URDF link; component ownership preserved ("+componentCount+")");
  };observer.Start();
  page.Show();
  Console.WriteLine("READY: native URDF page; robot export preparation was not called");
  Application.Run(new Form{Text="Generated fixture page flow validation",Width=320,Height=100,ShowInTaskbar=false,WindowState=FormWindowState.Minimized});
  GC.KeepAlive(page);GC.KeepAlive(sw);
 }
}
'@
[CollisionPageFlowProbe]::Run($ProcessId,$fixturePath)
