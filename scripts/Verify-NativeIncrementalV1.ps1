param([string]$Payload='native-candidate')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$directory=Join-Path $root ('build\native-incremental-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$bin=Join-Path $root ('build\'+$Payload)
$interop='D:\sw\sw2025\SOLIDWORKS'
$refs=@("$interop\SolidWorks.Interop.sldworks.dll","$interop\SolidWorks.Interop.swconst.dll","$interop\SolidWorks.Interop.swpublished.dll","$bin\SW2URDF.dll","$bin\MathNet.Numerics.dll",'System.Windows.Forms','System.Drawing','System.Runtime.Serialization','System.Web.Extensions','System.Xml','System.Xml.Linq','System.Core')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
# Native SolidWorks API, no mouse/keyboard automation. Never attach to an existing document.
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using Environment=System.Environment;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Simulation;
public static class NativeIncrementalProbe {
 static JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 static object Stage(SldWorks sw,ModelDoc2 model,string directory,string label,string settings){
  int g=ExportInstrumentation.GeometryQueries,s=ExportInstrumentation.StlExports,b=ProjectExport.SourceBuildCount;
  using(var export=new ProjectExport(sw,model,model.ConfigurationManager.ActiveConfiguration.Name)){
   string output=Path.Combine(directory,label+"_mjcf","robot.xml");var lines=new List<string>();
   int code=PythonBackend.RunAsync(@"D:\Softwaves\python\python.exe",export.Urdf,export.Sidecar,output,false,null,line=>{lines.Add(line);Console.WriteLine(line);},settings,export.ExportId).GetAwaiter().GetResult();
   Check(code==0,"native "+label+" export succeeds");export.MarkSucceeded();
   var metrics=json.Deserialize<Dictionary<string,object>>(lines.Single(x=>x.StartsWith("Export metrics: ")).Substring("Export metrics: ".Length));
   var counts=(Dictionary<string,object>)metrics["counts"];
   var delta=new Dictionary<string,int>{{"geometry_query",ExportInstrumentation.GeometryQueries-g},{"stl_export",ExportInstrumentation.StlExports-s},{"source_build",ProjectExport.SourceBuildCount-b}};
   if(label=="incremental")Check(delta["geometry_query"]==0&&delta["stl_export"]==0&&delta["source_build"]==0&&Convert.ToInt32(counts["mesh_simplification"])==0&&Convert.ToInt32(counts["mjcf_generation"])==1&&Convert.ToInt32(counts["mujoco_validation"])==1,"native V1 zero/zero/zero/one/one counts");
   else Check(delta["source_build"]==1&&delta["stl_export"]>0,"native "+label+" really builds CAD source/STL");
   return new{label,output,cad=delta,backend=metrics};
  }
 }
 public static void Run(string directory){
  var sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application"));
  // Do not change visibility, close documents, or quit unless this instance starts empty.
  if(sw.GetDocumentCount()!=0)throw new Exception("Refusing SolidWorks instance containing existing documents");
  File.WriteAllText(Path.Combine(directory,"solidworks-process.txt"),sw.GetProcessID().ToString());
  var oldCache=Environment.GetEnvironmentVariable("SW2MUJOCO_CACHE");var oldMesh=Environment.GetEnvironmentVariable("SW2MUJOCO_MESH_CACHE");var oldProfile=Environment.GetEnvironmentVariable("SW2MUJOCO_PROFILE");
  Environment.SetEnvironmentVariable("SW2MUJOCO_CACHE",Path.Combine(directory,"cache"));Environment.SetEnvironmentVariable("SW2MUJOCO_MESH_CACHE",Path.Combine(directory,"mesh-cache"));Environment.SetEnvironmentVariable("SW2MUJOCO_PROFILE","1");
  try{
   sw.Visible=false;sw.UserControl=false;string templates=@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\";int errors=0,warnings=0;
   for(int i=0;i<2;i++){
    var part=(ModelDoc2)sw.NewDocument(templates+"gb_part.prtdot",0,0,0);if(part==null)throw new Exception("Part template missing");Feature plane=(Feature)part.FirstFeature();while(plane!=null&&plane.GetTypeName2()!="RefPlane")plane=(Feature)plane.GetNextFeature();
    Check(plane!=null&&plane.Select2(false,0),"new fixture plane available");part.SketchManager.InsertSketch(true);part.SketchManager.CreateCornerRectangle(-.025,-.02,0,.025,.02,0);part.SketchManager.InsertSketch(true);
    Check(part.FeatureManager.FeatureExtrusion2(true,false,false,0,0,.02,.02,false,false,false,false,0,0,false,false,false,false,true,true,true,0,0,false)!=null,"new block extruded");part.ClearSelection2(true);
    Check(part.Extension.SaveAs(Path.Combine(directory,"block"+i+".SLDPRT"),0,1,null,ref errors,ref warnings),"new block saved");
   }
   var model=(ModelDoc2)sw.NewDocument(templates+"gb_assembly.asmdot",0,0,0);if(model==null)throw new Exception("Assembly template missing");var assembly=(AssemblyDoc)model;
   var first=assembly.AddComponent5(Path.Combine(directory,"block0.SLDPRT"),0,"",false,"",0,0,0);var second=assembly.AddComponent5(Path.Combine(directory,"block1.SLDPRT"),0,"",false,"",.08,0,0);Check(first!=null&&second!=null,"new components inserted");
   var root=new LinkNode(new Link(null));root.Link.Name="base";root.Name=root.Text="base";root.IsBaseNode=true;((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(root.Link)).Add(first);root.Link.SWMainComponent=first;root.Link.Joint.CoordinateSystemName="Automatically Generate";
   var child=new LinkNode(new Link(root.Link));child.Link.Name="arm";child.Name=child.Text="arm";((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(child.Link)).Add(second);child.Link.SWMainComponent=second;child.Link.Joint.Name="hinge";child.Link.Joint.Type="continuous";child.Link.Joint.CoordinateSystemName="Automatically Generate";child.Link.Joint.AxisName="Automatically Generate";root.Nodes.Add(child);root.UpdateLinkTree(null);
   CommonSwOperations.RetrieveSWComponentPIDs(model,root);ConfigurationSerialization.SaveConfigTreeXML(sw,model,root,false);
   var project=new SimulationProject{solver=new SolverSettings{enabled=true,timestep=.001},collision=new CollisionConfiguration()};SimulationStorage.Save(sw,model,project);
   Check(model.Extension.SaveAs(Path.Combine(directory,"fixture.SLDASM"),0,1,null,ref errors,ref warnings),"new assembly saved");
   string settings=Path.Combine(directory,"mesh-settings.json");new MeshExportSettings{Enabled=true,MaximumTriangles=100000,Backend="fast-simplification"}.Save(settings);
   var initial=Stage(sw,model,directory,"initial",settings);
   project=SimulationStorage.Load(model);string beforeConfig=json.Serialize(project);project.solver.timestep=.002;SimulationStorage.Save(sw,model,project);
   var verified=SimulationStorage.Load(model);verified.solver.timestep=.001;Check(json.Serialize(verified)==beforeConfig,"only timestep configuration changed");
   var incremental=Stage(sw,model,directory,"incremental",settings);
   // Only delete caches inside this newly generated, owned fixture directory.
   foreach(var name in new[]{"cache","mesh-cache"}){string path=Path.GetFullPath(Path.Combine(directory,name));if(Path.GetDirectoryName(path)!=Path.GetFullPath(directory))throw new Exception("Cache containment check failed");if(Directory.Exists(path))Directory.Delete(path,true);}
   var full=Stage(sw,model,directory,"full",settings);
   File.WriteAllText(Path.Combine(directory,"native-counts.json"),json.Serialize(new{status="passed",source="new native SW assembly",stages=new[]{initial,incremental,full}}));Console.WriteLine("NATIVE_REPORT: "+directory);
  }finally{
   Environment.SetEnvironmentVariable("SW2MUJOCO_CACHE",oldCache);Environment.SetEnvironmentVariable("SW2MUJOCO_MESH_CACHE",oldMesh);Environment.SetEnvironmentVariable("SW2MUJOCO_PROFILE",oldProfile);
   // Close only documents created by this probe. Never close an unexpected document.
   var docs=sw.GetDocuments() as object[];if(docs!=null)foreach(ModelDoc2 doc in docs){var path=doc.GetPathName();if(!string.IsNullOrEmpty(path)&&Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))sw.CloseDoc(doc.GetTitle());}
   if(sw.GetDocumentCount()==0)sw.ExitApp();
  }
 }
}
'@
[NativeIncrementalProbe]::Run($directory)
$directory | Set-Content -LiteralPath (Join-Path $root 'build\native-incremental-directory.txt')
