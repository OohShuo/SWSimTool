$ErrorActionPreference='Stop'
$root=Join-Path (Split-Path -Parent $PSScriptRoot) 'build\guide-demo'
$bin='C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter'
$interop='D:\sw\sw2025\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
$refs=@($interop,"$bin\SW2URDF.dll","$bin\MathNet.Numerics.dll",'System.Core','System.Web.Extensions','System.Windows.Forms','System.Drawing','System.Xml','System.Runtime.Serialization')
$refs|Where-Object {$_ -like '*.dll'}|ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Web.Script.Serialization;using SolidWorks.Interop.sldworks;using SW2URDF.Simulation;using SW2URDF.URDFExport;using SW2URDF.URDF;
public static class GuidePersistence {
 static string Snapshot(ModelDoc2 m){bool error;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(m,out error);if(error||tree==null)throw new Exception("Missing URDF tree");var names=new System.Collections.Generic.List<string>();Action<LinkNode> visit=null;visit=n=>{names.Add(n.Name+"|"+(n.Parent==null?"":n.Parent.Name)+"|"+n.Link.Joint.Name+"|"+n.Link.Joint.Type+"|"+n.Link.Joint.CoordinateSystemName+"|"+n.Link.Joint.AxisName);foreach(LinkNode c in n.Nodes)visit(c);};visit(tree);return new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(new{entry=SimulationStorage.LoadEntry(m),tree=names});}
 static void Guard(SldWorks sw,ModelDoc2 m,string root){if(sw.GetProcessID()!=Int32.Parse(File.ReadAllText(Path.Combine(root,"pid.txt"))))throw new Exception("Different process");if(m==null||!m.GetPathName().StartsWith(Path.Combine(root,"origin")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Not origin demo copy");foreach(Component2 c in (object[])((AssemblyDoc)m).GetComponents(false))if(!c.GetPathName().StartsWith(Path.Combine(root,"origin")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Non-copy component");}
 public static void Verify(string root){var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");var m=(ModelDoc2)sw.ActiveDoc;Guard(sw,m,root);string path=m.GetPathName(),before=Snapshot(m);int err=0,warn=0;if(!m.Save3(1,ref err,ref warn)||err!=0)throw new Exception("Save failed: "+err);sw.CloseDoc(m.GetTitle());m=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref err,ref warn);Guard(sw,m,root);string after=Snapshot(m);if(before!=after)throw new Exception("Configuration changed on reopen");File.WriteAllText(Path.Combine(root,"persistence.json"),after);Console.WriteLine("Save/reopen: exact simulation configuration and URDF tree retained");}
}
'@
[GuidePersistence]::Verify($root)
