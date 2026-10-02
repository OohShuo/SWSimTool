$ErrorActionPreference = 'Stop'
$workspacePath = Split-Path -Parent $PSScriptRoot
$testDirectory = Join-Path $workspacePath ('build\isolated-validation-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $testDirectory | Out-Null
$testDirectory | Set-Content (Join-Path $workspacePath 'build\isolated-validation-directory.txt')
$interopDirectory = 'D:\sw\sw2025\SOLIDWORKS'
$references = @("$interopDirectory\SolidWorks.Interop.sldworks.dll", "$interopDirectory\SolidWorks.Interop.swconst.dll", "$workspacePath\build\simulation\SW2URDF.dll", "$workspacePath\build\simulation\MathNet.Numerics.dll", 'System.Windows.Forms', 'System.Drawing', 'System.Runtime.Serialization', 'System.Web.Extensions', 'System.Xml', 'System.Core')
$references | Where-Object { $_ -like '*.dll' } | ForEach-Object { [Reflection.Assembly]::LoadFrom($_) | Out-Null }
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
public static class IsolatedSimulationFixture {
 public static void Build(string directory) {
  var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
  File.WriteAllText(Path.Combine(directory,"solidworks-process.txt"),sw.GetProcessID().ToString());
  // A fresh COM server must contain no document. Never inspect existing documents.
  if(sw.GetDocumentCount()!=0) throw new Exception("Fresh SolidWorks server contains documents; refusing to use it.");
  sw.Visible=true; sw.UserControl=true;
  string templates=@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\";
  int errors=0,warnings=0;
  for(int i=0;i<2;i++) {
   var part=(ModelDoc2)sw.NewDocument(templates+"gb_part.prtdot",0,0,0);
   Feature plane=(Feature)part.FirstFeature();
   while(plane!=null && plane.GetTypeName2()!="RefPlane") plane=(Feature)plane.GetNextFeature();
   if(plane==null || !plane.Select2(false,0)) throw new Exception("No template plane");
   part.SketchManager.InsertSketch(true);
   part.SketchManager.CreateCornerRectangle(-0.025,-0.02,0,0.025,0.02,0);
   part.SketchManager.InsertSketch(true);
   var solid=part.FeatureManager.FeatureExtrusion2(true,false,false,0,0,0.02,0.02,false,false,false,false,0,0,false,false,false,false,true,true,true,0,0,false);
   if(solid==null) throw new Exception("Solid extrusion failed");
   part.ClearSelection2(true);
   var frame=part.FeatureManager.InsertCoordinateSystem(false,false,false);
   if(frame==null) throw new Exception("Frame insertion failed");
   frame.Name="FixtureFrame";
   if(!part.Extension.SaveAs(Path.Combine(directory,"block"+i+".SLDPRT"),0,1,null,ref errors,ref warnings)) throw new Exception("Part save failed "+errors);
  }
  var model=(ModelDoc2)sw.NewDocument(templates+"gb_assembly.asmdot",0,0,0);
  var assembly=(AssemblyDoc)model;
  var first=assembly.AddComponent5(Path.Combine(directory,"block0.SLDPRT"),0,"",false,"",0,0,0);
  var second=assembly.AddComponent5(Path.Combine(directory,"block1.SLDPRT"),0,"",false,"",0.08,0,0);
  if(first==null || second==null) throw new Exception("Component insertion failed");
  var root=new LinkNode(new Link(null)); root.Link.Name="base_link"; root.Name=root.Text="base_link"; root.IsBaseNode=true;
  ((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(root.Link)).Add(first); root.Link.SWMainComponent=first; root.Link.Joint.CoordinateSystemName="Automatically Generate";
  var child=new LinkNode(new Link(root.Link)); child.Link.Name="arm"; child.Name=child.Text="arm";
  ((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(child.Link)).Add(second); child.Link.SWMainComponent=second; child.Link.Joint.Name="hinge"; child.Link.Joint.Type="continuous";
  child.Link.Joint.CoordinateSystemName="Automatically Generate"; child.Link.Joint.AxisName="Automatically Generate";
  root.Nodes.Add(child); root.UpdateLinkTree(null);
  CommonSwOperations.RetrieveSWComponentPIDs(model,root);
  ConfigurationSerialization.SaveConfigTreeXML(sw,model,root,false);
  if(!model.Extension.SaveAs(Path.Combine(directory,"isolated_fixture.SLDASM"),0,1,null,ref errors,ref warnings)) throw new Exception("Assembly save failed "+errors);
  model.ViewZoomtofit2();
  Console.WriteLine("CREATED entirely new fixture: "+directory+"; PID="+sw.GetProcessID()); System.Windows.Forms.Application.Run(new System.Windows.Forms.Form { Text="Isolated SolidWorks validation host", Width=360, Height=120 }); GC.KeepAlive(sw);
 }
}
'@
[IsolatedSimulationFixture]::Build($testDirectory)
