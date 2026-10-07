param([string]$Payload="bin/SWSimTool.SolidWorks/Release/net48",[switch]$Installed)
$ErrorActionPreference='Stop'
$root=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$bin=if($Installed){'C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter'}else{Join-Path $root ('build\'+$Payload)}
$interop='D:\sw\sw2025\SOLIDWORKS'
$refs=@("$interop\SolidWorks.Interop.sldworks.dll","$interop\SolidWorks.Interop.swconst.dll","$interop\SolidWorks.Interop.swpublished.dll","$bin\SWSimTool.dll","$bin\SWSimTool.Core.dll","$bin\SWSimTool.Application.dll","$bin\SWSimTool.Infrastructure.dll","$bin\MathNet.Numerics.dll",'System.Core','System.Xml','System.Xml.Linq','System.Web.Extensions','System.Runtime.Serialization','System.Windows.Forms','System.Drawing')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @"
using System;using System.IO;using System.Linq;using System.Web.Script.Serialization;using System.Collections.Generic;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;using SWSimTool.URDFExport;using SWSimTool.URDF;
public static class GuideHost {
 public static void Start(string root,string dll){
  var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));if(sw.GetDocumentCount()!=0)throw new Exception("Refusing existing documents");
  sw.Visible=true;sw.UserControl=true;File.WriteAllText(Path.Combine(root,"pid.txt"),sw.GetProcessID().ToString());
  foreach(var name in new[]{"origin","done"}){
   var dir=Path.Combine(root,name);var assembly=Path.Combine(dir,"balance2026_gimbal.SLDASM");
   var deps=sw.GetDocumentDependencies2(assembly,true,true,false) as string[];
   if(deps!=null)for(int i=1;i<deps.Length;i+=2){string target=Path.Combine(dir,Path.GetFileName(deps[i]));if(File.Exists(target)&&!String.Equals(deps[i],target,StringComparison.OrdinalIgnoreCase)){if(!sw.ReplaceReferencedDocument(assembly,deps[i],target))throw new Exception("Reference relink failed: "+deps[i]);}}
  }
  Console.WriteLine("LoadAddIn="+sw.LoadAddIn(dll));int err=0,warn=0;
  var doc=(ModelDoc2)sw.OpenDoc6(Path.Combine(root,"done","balance2026_gimbal.SLDASM"),2,1,"",ref err,ref warn);if(doc==null)throw new Exception("Open copy failed: "+err);
  foreach(Component2 c in (object[])((AssemblyDoc)doc).GetComponents(false))if(!c.GetPathName().StartsWith(Path.Combine(root,"done")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Non-copy component loaded: "+c.GetPathName());
  var entry=SimulationStorage.LoadEntry(doc);bool treeError;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(doc,out treeError);if(tree!=null)CommonSwOperations.LoadSWComponents(doc,tree,new List<string>());var links=new List<object>();Action<LinkNode> visit=null;visit=n=>{links.Add(new{name=n.Name,parent=n.Parent==null?null:n.Parent.Name,joint=n.Link.Joint.Name,type=n.Link.Joint.Type,coordinate=n.Link.Joint.CoordinateSystemName,axis=n.Link.Joint.AxisName,components=((System.Collections.IEnumerable)typeof(Link).GetField("SWComponents").GetValue(n.Link)).Cast<Component2>().Select(c=>c.Name2).ToArray()});foreach(LinkNode c in n.Nodes)visit(c);};if(tree!=null)visit(tree);
  var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};File.WriteAllText(Path.Combine(root,"reference.json"),json.Serialize(new{entry,links,features=((object[])doc.FeatureManager.GetFeatures(true)).Cast<Feature>().Select(f=>new{name=f.Name,type=f.GetTypeName2()}).ToArray()}));doc.ViewZoomtofit2();Console.WriteLine("Demo PID: "+sw.GetProcessID());
 }
}
"@
if($Installed){[GuideHost]::Start((Join-Path $root 'build\guide-demo'),(Join-Path $bin 'SWSimTool.dll'));exit}
$overrideKey='HKCU:\Software\Classes\CLSID\{974a302b-3966-4e45-a5a5-1c24c26b7faa}'
if(Test-Path $overrideKey){throw 'Existing per-user override'}
$dll=Join-Path $bin 'SWSimTool.dll';$assemblyName=[Reflection.AssemblyName]::GetAssemblyName($dll)
$r=New-Item -Path ($overrideKey+'\InprocServer32') -Force
$r.SetValue('','mscoree.dll');$r.SetValue('ThreadingModel','Both');$r.SetValue('Class','SWSimTool.SW.SwAddin');$r.SetValue('Assembly',$assemblyName.FullName);$r.SetValue('RuntimeVersion','v4.0.30319');$r.SetValue('CodeBase',([Uri]$dll).AbsoluteUri)
$v=New-Item -Path ($overrideKey+'\InprocServer32\'+$assemblyName.Version.ToString()) -Force
foreach($n in @('Class','Assembly','RuntimeVersion','CodeBase')){$v.SetValue($n,$r.GetValue($n))}
try{[GuideHost]::Start((Join-Path $root 'build\guide-demo'),$dll)}finally{Remove-Item -LiteralPath $overrideKey -Recurse -Force}


