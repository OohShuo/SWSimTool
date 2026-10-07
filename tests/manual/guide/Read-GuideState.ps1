$ErrorActionPreference='Stop'
$interop='D:\sw\sw2025\SOLIDWORKS\SolidWorks.Interop.sldworks.dll'
[Reflection.Assembly]::LoadFrom($interop)|Out-Null
Add-Type -ReferencedAssemblies @($interop,'System.Core') -TypeDefinition @'
using System;using System.IO;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;
public static class GuideState {
 public static void Read(string root){var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");if(sw.GetProcessID()!=Int32.Parse(File.ReadAllText(Path.Combine(root,"pid.txt"))))throw new Exception("Different process");var m=(ModelDoc2)sw.ActiveDoc;if(!m.GetPathName().StartsWith(root+Path.DirectorySeparatorChar))throw new Exception("Not demo copy");Console.WriteLine(m.GetPathName());foreach(Component2 c in (object[])((AssemblyDoc)m).GetComponents(false)){if(!c.GetPathName().StartsWith(root+Path.DirectorySeparatorChar))throw new Exception("Non-copy component: "+c.GetPathName());Console.WriteLine(c.Name2+" "+c.GetPathName()+" suppression="+c.GetSuppression());}}
}
'@
[GuideState]::Read('D:\solidworks_urdf_exporter-master\build\guide-demo')
$bin='C:\Program Files\SolidWorks Corp\SolidWorks\URDFExporter'
$refs=@($interop,"$bin\SWSimTool.dll","$bin\MathNet.Numerics.dll",'System.Core','System.Web.Extensions','System.Windows.Forms','System.Drawing')
$refs|Where-Object {$_ -like '*.dll'}|ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Runtime.InteropServices;using System.Web.Script.Serialization;using SolidWorks.Interop.sldworks;using SWSimTool.Simulation;
public static class GuideConfiguration {
 public static void Read(string root){var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");var m=(ModelDoc2)sw.ActiveDoc;var entry=SimulationStorage.LoadEntry(m);File.WriteAllText(Path.Combine(root,"current-configuration.json"),new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(entry));Console.WriteLine("Saved configuration snapshot");}
}
'@
[GuideConfiguration]::Read('D:\solidworks_urdf_exporter-master\build\guide-demo')
