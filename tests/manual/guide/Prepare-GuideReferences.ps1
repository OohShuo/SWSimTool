$ErrorActionPreference='Stop'
$root=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$interop='D:\sw\sw2025\SOLIDWORKS'
$refs=@("$interop\SolidWorks.Interop.sldworks.dll","$interop\SolidWorks.Interop.swconst.dll",'System.Core','System.Web.Extensions')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Collections.Generic;using System.Linq;using System.Runtime.InteropServices;using System.Web.Script.Serialization;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;
public static class GuideReferences {
 static void Guard(SldWorks sw,string root){if(sw.GetProcessID()!=Int32.Parse(File.ReadAllText(Path.Combine(root,"pid.txt"))))throw new Exception("Wrong SW process"); foreach(ModelDoc2 doc in ((object[])sw.GetDocuments()).Cast<ModelDoc2>())if(!doc.GetPathName().StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new Exception("Non-demo document");}
 static ModelDoc2 Open(SldWorks sw,string path){int e=0,w=0;var m=(ModelDoc2)sw.OpenDoc6(path,2,1,"",ref e,ref w);if(m==null)throw new Exception("Open failed "+e);return m;}
 public static void Run(string root){var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");Guard(sw,root);var current=(ModelDoc2)sw.ActiveDoc;if(!current.GetPathName().Contains("\\origin\\"))throw new Exception("Expected origin copy");int er=0,wa=0;if(!current.Save3(1,ref er,ref wa))throw new Exception("Save origin failed");sw.CloseDoc(current.GetTitle());
 var done=Open(sw,Path.Combine(root,"done","balance2026_gimbal.SLDASM"));
 string[] frames={"origin_coord","yaw_coord","pitch_coord","pitch0_coord","pitch1_coord","imu_coord","yaw_coll1_coord","yaw_coll2_coord","pitch0_coll_coord","pitch_coll_coord"};
 string[] axes={"yaw_shaft","pitch_shaft","pitch0_shaft","pitch1_shaft"};string[] points={"pitch1_cnct_point","pitch_cnct_point"};
 var data=new Dictionary<string,double[]>();foreach(string n in frames){var t=done.Extension.GetCoordinateSystemTransformByName(n);if(t==null)throw new Exception("Missing frame "+n);data[n]=(double[])((MathTransform)t).ArrayData;}
 foreach(string n in axes){var f=(Feature)((AssemblyDoc)done).FeatureByName(n);data[n]=(double[])((RefAxis)f.GetSpecificFeature2()).GetRefAxisParams();}
 foreach(string n in points){var f=(Feature)((AssemblyDoc)done).FeatureByName(n);data[n]=(double[])((MathPoint)((RefPoint)f.GetSpecificFeature2()).GetRefPoint()).ArrayData;}
 File.WriteAllText(Path.Combine(root,"cad-references.json"),new JavaScriptSerializer().Serialize(data));sw.CloseDoc(done.GetTitle());var m=Open(sw,Path.Combine(root,"origin","balance2026_gimbal.SLDASM"));Guard(sw,root);
 foreach(string n in frames){if(((AssemblyDoc)m).FeatureByName(n)!=null)throw new Exception("Reference already exists "+n);var a=data[n];m.ClearSelection2(true);m.SketchManager.Insert3DSketch(true);var p=m.SketchManager.CreatePoint(a[9],a[10],a[11]);var x=m.SketchManager.CreateLine(a[9],a[10],a[11],a[9]+a[0]*.02,a[10]+a[1]*.02,a[11]+a[2]*.02);var y=m.SketchManager.CreateLine(a[9],a[10],a[11],a[9]+a[3]*.02,a[10]+a[4]*.02,a[11]+a[5]*.02);m.SketchManager.Insert3DSketch(true);m.ClearSelection2(true);var sd=((SelectionMgr)m.SelectionManager).CreateSelectData();sd.Mark=1;p.Select4(false,sd);sd.Mark=2;x.Select4(true,sd);sd.Mark=4;y.Select4(true,sd);var f=m.FeatureManager.InsertCoordinateSystem(false,false,false);if(f==null)throw new Exception("Frame insertion "+n);f.Name=n;var actual=(double[])((MathTransform)m.Extension.GetCoordinateSystemTransformByName(n)).ArrayData;if(Enumerable.Range(0,12).Any(i=>Math.Abs(actual[i]-a[i])>1e-8))throw new Exception("Frame mismatch "+n);}
 foreach(string n in axes){var a=data[n];m.ClearSelection2(true);m.SketchManager.Insert3DSketch(true);var line=m.SketchManager.CreateLine(a[0],a[1],a[2],a[3],a[4],a[5]);m.SketchManager.Insert3DSketch(true);m.ClearSelection2(true);line.Select4(false,null);if(!m.InsertAxis2(true))throw new Exception("Axis insertion "+n);((Feature)m.FeatureByPositionReverse(0)).Name=n;}
 foreach(string n in points){var a=data[n];m.ClearSelection2(true);m.SketchManager.Insert3DSketch(true);var p=m.SketchManager.CreatePoint(a[0],a[1],a[2]);m.SketchManager.Insert3DSketch(true);p.Select4(false,null);var result=(object[])m.FeatureManager.InsertReferencePoint((int)swRefPointType_e.swRefPointSketchPoint,0,0,1);if(result==null||result.Length==0)throw new Exception("Point insertion "+n);((Feature)result[0]).Name=n;}
 m.ClearSelection2(true);m.ForceRebuild3(false);if(!m.Save3(1,ref er,ref wa))throw new Exception("Save references failed "+er);m.ViewZoomtofit2();Console.WriteLine("Prepared and checked ten coordinate systems, four axes and two reference points in origin copy.");
 }
}
'@
[GuideReferences]::Run((Join-Path $root 'build\guide-demo'))
