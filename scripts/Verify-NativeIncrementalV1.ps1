param([string]$Payload='native-candidate',[switch]$NativeOnly)
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
 public static bool NativeOnly;
 static JavaScriptSerializer json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
 static void CoreParity(SldWorks sw,ModelDoc2 model,string directory){
  bool error;var legacyTree=ConfigurationSerialization.LoadBaseNodeFromModel(model,out error);CommonSwOperations.LoadSWComponents(model,legacyTree,new List<string>());
  var legacyHelper=new ExportHelper(sw);Check(legacyHelper.CreateRobotFromTreeView(legacyTree),"legacy core reference builds on new fixture");
  var legacy=SW2URDF.RobotModel.SolidWorksRobotModelBuilder.FromResolvedRobot(legacyHelper.URDFRobot,new Dictionary<string,SW2URDF.RobotModel.MeshSource>());
  var tree=ConfigurationSerialization.LoadBaseNodeFromModel(model,out error);CommonSwOperations.LoadSWComponents(model,tree,new List<string>());
  var helper=new ExportHelper(sw);helper.EnsureNativeReferences(tree);helper.GetSimulation().SetCollisionTree(tree);
  var direct=CadRobotCoreBuilder.Build("robot",helper.GetSimulation(),tree,new Dictionary<string,SW2URDF.RobotModel.MeshSource>());
  Check(helper.URDFRobot==null,"direct CAD builder never constructs URDF Robot");
  var assets=new SW2URDF.RobotModel.PreparedAssets(new SW2URDF.RobotModel.PreparedMeshAsset[0]);var context=new SW2URDF.RobotModel.ExportContext("robot");
  File.WriteAllText(Path.Combine(directory,"core_reference.xml"),SW2URDF.RobotModel.MjcfExporter.Generate(new SW2URDF.RobotModel.RobotModel(legacy,new SW2URDF.RobotModel.SimulationConfigSnapshot()),assets,context));
  File.WriteAllText(Path.Combine(directory,"core_direct.xml"),SW2URDF.RobotModel.MjcfExporter.Generate(new SW2URDF.RobotModel.RobotModel(direct,new SW2URDF.RobotModel.SimulationConfigSnapshot()),assets,context));
 }
 static object Stage(SldWorks sw,ModelDoc2 model,string directory,string label,string settings){
  int g=ExportInstrumentation.GeometryQueries,s=ExportInstrumentation.StlExports,b=ProjectExport.SourceBuildCount;
  using(var export=new ProjectExport(sw,model,model.ConfigurationManager.ActiveConfiguration.Name,NativeOnly)){
   if(NativeOnly){
    Check(export.Urdf==null,"engineering export has no intermediate URDF");
    Check(export.Sidecar==null,"engineering model construction has no sidecar JSON bridge");
    string directOutput=Path.Combine(directory,label+"_native_mjcf","robot.xml");var directLines=new List<string>();
    int directCode=NativeBackend.RunAsync(@"D:\Softwaves\python\python.exe",export.NativeModel(),directOutput,false,line=>{directLines.Add(line);Console.WriteLine(line);},settings,export.ExportId).GetAwaiter().GetResult();
    Check(directCode==0,"native production "+label+" export succeeds");export.MarkSucceeded();
    var directMetrics=json.Deserialize<Dictionary<string,object>>(directLines.Single(x=>x.StartsWith("Native export metrics: ")).Substring("Native export metrics: ".Length));
    var directCounts=(Dictionary<string,object>)directMetrics["counts"];
    var directDelta=new Dictionary<string,int>{{"geometry_query",ExportInstrumentation.GeometryQueries-g},{"stl_export",ExportInstrumentation.StlExports-s},{"source_build",ProjectExport.SourceBuildCount-b}};
    if(label=="incremental")Check(directDelta["geometry_query"]==0&&directDelta["stl_export"]==0&&directDelta["source_build"]==0&&Convert.ToInt32(directCounts["mesh_prepare"])==0&&Convert.ToInt32(directCounts["mesh_simplification"])==0&&Convert.ToInt32(directCounts["mjcf_generation"])==1&&Convert.ToInt32(directCounts["mujoco_validation"])==1,"native production zero/zero/zero/one/one counts");
    else Check(directDelta["source_build"]==1&&directDelta["stl_export"]==4,"cold native production exports four raw STL");
    Check(!Directory.GetFiles(directory,"*.urdf",SearchOption.AllDirectories).Any(),"native-only fixture and cache contain no URDF");
    return new{label,nativeOutput=directOutput,cad=directDelta,nativeBackend=directMetrics,nativeOnly=true};
   }
   string output=Path.Combine(directory,label+"_mjcf","robot.xml");var lines=new List<string>();
   int code=PythonBackend.RunAsync(@"D:\Softwaves\python\python.exe",export.Urdf,export.Sidecar,output,false,null,line=>{lines.Add(line);Console.WriteLine(line);},settings,export.ExportId).GetAwaiter().GetResult();
   Check(code==0,"native "+label+" export succeeds");export.MarkSucceeded();
   var metrics=json.Deserialize<Dictionary<string,object>>(lines.Single(x=>x.StartsWith("Export metrics: ")).Substring("Export metrics: ".Length));
   var counts=(Dictionary<string,object>)metrics["counts"];
   var nativeLines=new List<string>();string nativeOutput=Path.Combine(directory,label+"_native_mjcf","robot.xml");
   var nativeModel=export.NativeModel();
   code=NativeBackend.RunAsync(@"D:\Softwaves\python\python.exe",nativeModel,nativeOutput,false,line=>{nativeLines.Add(line);Console.WriteLine(line);},settings,export.ExportId).GetAwaiter().GetResult();
   Check(code==0,"C# shadow "+label+" export succeeds");
   var nativeMetrics=json.Deserialize<Dictionary<string,object>>(nativeLines.Single(x=>x.StartsWith("Native export metrics: ")).Substring("Native export metrics: ".Length));
   var nativeCounts=(Dictionary<string,object>)nativeMetrics["counts"];
   if(label=="incremental")Check(Convert.ToInt32(nativeCounts["mesh_prepare"])==0&&Convert.ToInt32(nativeCounts["mesh_simplification"])==0&&Convert.ToInt32(nativeCounts["mjcf_generation"])==1&&Convert.ToInt32(nativeCounts["mujoco_validation"])==1,"C# shadow zero preparation / one generation / one validation");
   var delta=new Dictionary<string,int>{{"geometry_query",ExportInstrumentation.GeometryQueries-g},{"stl_export",ExportInstrumentation.StlExports-s},{"source_build",ProjectExport.SourceBuildCount-b}};
   if(label=="incremental")Check(delta["geometry_query"]==0&&delta["stl_export"]==0&&delta["source_build"]==0&&Convert.ToInt32(counts["mesh_simplification"])==0&&Convert.ToInt32(counts["mjcf_generation"])==1&&Convert.ToInt32(counts["mujoco_validation"])==1,"native V1 zero/zero/zero/one/one counts");
   else Check(delta["source_build"]==1&&delta["stl_export"]>0,"native "+label+" really builds CAD source/STL");
   return new{label,output,nativeOutput,sourceUrdf=export.Urdf,cad=delta,backend=metrics,nativeBackend=nativeMetrics};
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
   for(int i=0;i<4;i++){
    var part=(ModelDoc2)sw.NewDocument(templates+"gb_part.prtdot",0,0,0);if(part==null)throw new Exception("Part template missing");Feature plane=(Feature)part.FirstFeature();while(plane!=null&&plane.GetTypeName2()!="RefPlane")plane=(Feature)plane.GetNextFeature();
    Check(plane!=null&&plane.Select2(false,0),"new fixture plane available");part.SketchManager.InsertSketch(true);part.SketchManager.CreateCornerRectangle(-.025,-.02,0,.025+i*.005,.02+i*.003,0);part.SketchManager.InsertSketch(true);
    Check(part.FeatureManager.FeatureExtrusion2(true,false,false,0,0,.02,.02,false,false,false,false,0,0,false,false,false,false,true,true,true,0,0,false)!=null,"new block extruded");part.ClearSelection2(true);
    Check(part.Extension.SaveAs(Path.Combine(directory,"block"+i+".SLDPRT"),0,1,null,ref errors,ref warnings),"new block saved");
   }
   var model=(ModelDoc2)sw.NewDocument(templates+"gb_assembly.asmdot",0,0,0);if(model==null)throw new Exception("Assembly template missing");var assembly=(AssemblyDoc)model;
   var first=assembly.AddComponent5(Path.Combine(directory,"block0.SLDPRT"),0,"",false,"",0,0,0);var second=assembly.AddComponent5(Path.Combine(directory,"block1.SLDPRT"),0,"",false,"",.08,0,0);Check(first!=null&&second!=null,"new components inserted");
   var root=new LinkNode(new Link(null));root.Link.Name="base";root.Name=root.Text="base";root.IsBaseNode=true;((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(root.Link)).Add(first);root.Link.SWMainComponent=first;root.Link.Joint.CoordinateSystemName="Automatically Generate";
   var child=new LinkNode(new Link(root.Link));child.Link.Name="arm";child.Name=child.Text="arm";((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(child.Link)).Add(second);child.Link.SWMainComponent=second;child.Link.Joint.Name="hinge";child.Link.Joint.Type="continuous";child.Link.Joint.CoordinateSystemName="Automatically Generate";child.Link.Joint.AxisName="Automatically Generate";root.Nodes.Add(child);root.UpdateLinkTree(null);
   var previous=child;
   for(int i=2;i<4;i++){
    var component=assembly.AddComponent5(Path.Combine(directory,"block"+i+".SLDPRT"),0,"",false,"",i*.08,.01*i,0);Check(component!=null,"additional native component inserted");
    var node=new LinkNode(new Link(previous.Link));node.Name=node.Text=node.Link.Name=i==2?"elbow":"tool";((System.Collections.IList)typeof(Link).GetField("SWComponents").GetValue(node.Link)).Add(component);node.Link.SWMainComponent=component;node.Link.Joint.Name=i==2?"bend":"tool_fixed";node.Link.Joint.Type=i==2?"revolute":"fixed";node.Link.Joint.CoordinateSystemName="Automatically Generate";node.Link.Joint.AxisName="Automatically Generate";if(i==2){node.Link.Joint.Limit.Lower=-1;node.Link.Joint.Limit.Upper=1;node.Link.Joint.Limit.Effort=10;node.Link.Joint.Limit.Velocity=1;}previous.Nodes.Add(node);previous=node;
   }
   root.UpdateLinkTree(null);
   var helper=new ExportHelper(sw);Check(helper.CreateRobotFromTreeView(root),"native reference fixture builds before capture");
   int movingIndex=0;
   foreach(LinkNode moving in new[]{child,(LinkNode)child.Nodes[0]}) {
    model.ClearSelection2(true);model.SketchManager.Insert3DSketch(true);var segment=model.SketchManager.CreateLine(0,0,0,movingIndex==0?0:.04,movingIndex==0?0:.01,.05);model.SketchManager.Insert3DSketch(true);Check(segment!=null,"explicit native joint direction drawn");
    var names=new HashSet<string>(((object[])model.FeatureManager.GetFeatures(true)).Cast<Feature>().Select(f=>f.Name));
    Check(segment.Select4(false,((SelectionMgr)model.SelectionManager).CreateSelectData()),"explicit joint segment selected");Check(model.InsertAxis2(true),"explicit native axis created");
    var axis=((object[])model.FeatureManager.GetFeatures(true)).Cast<Feature>().First(f=>f.GetTypeName2()=="RefAxis"&&!names.Contains(f.Name));axis.Name="native_axis_"+movingIndex;moving.Link.Joint.AxisName=axis.Name;moving.Link.Joint.Type=movingIndex==0?"continuous":"revolute";movingIndex++;
   }
   model.ClearSelection2(true);
   CommonSwOperations.RetrieveSWComponentPIDs(model,root);ConfigurationSerialization.SaveConfigTreeXML(sw,model,root,false);
   var project=new SimulationProject{solver=new SolverSettings{enabled=true,timestep=.001},collision=new CollisionConfiguration()};
   helper.GetSimulation().Project=project;
   string anchor=child.Link.Joint.CoordinateSystemName;
   foreach(string link in new[]{"base","arm"}) {Check(model.Extension.SelectByID2(anchor,"COORDSYS",0,0,0,false,0,null,0),"native joint coordinate selected");project.attachments.Add(helper.GetSimulation().CaptureSelectedAttachment(link,"anchor_"+link,"frame"));}
   Check(model.Extension.SelectByID2(previous.Link.Joint.CoordinateSystemName,"COORDSYS",0,0,0,false,0,null,0),"native tool frame selected");project.attachments.Add(helper.GetSimulation().CaptureSelectedAttachment("tool","imu_mount","frame"));
   Check(model.Extension.SelectByID2(root.Link.Joint.CoordinateSystemName,"COORDSYS",0,0,0,false,0,null,0),"native base frame selected");project.attachments.Add(helper.GetSimulation().CaptureSelectedAttachment("base","force_base","point"));
   project.actuators.Add(new ActuatorConfig{name="motor",joint="hinge",force_min=-1,force_max=1});project.sensors.Add(new SensorConfig{name="imu",site="imu_mount",type="imu"});project.equalities.Add(new EqualityConfig{name="closure",site1="anchor_base",site2="anchor_arm"});
   project.site_forces.Add(new SiteForceConfig{name="pull",type="pull",site1="force_base",site2="imu_mount",magnitude=.1});project.site_forces.Add(new SiteForceConfig{name="spring",type="spring",site1="force_base",site2="imu_mount",stiffness=1,damping=.01});
   foreach(string link in new[]{"base","arm","elbow","tool"}){project.collision.link_modes[link]="primitive";project.collision.geometries.Add(new CollisionGeometry{name=link+"_proxy",link=link,type="box",size=new[]{.04,.03,.02},xyz=new double[3],rpy=new double[3]});}
   project.collision.allowed_pairs.Add(new CollisionPair{link1="base",link2="tool"});SimulationStorage.Save(sw,model,project);
   Check(model.Extension.SaveAs(Path.Combine(directory,"fixture.SLDASM"),0,1,null,ref errors,ref warnings),"new assembly saved");
   string settings=Path.Combine(directory,"mesh-settings.json");new MeshExportSettings{Enabled=true,MaximumTriangles=100000,Backend="fast-simplification"}.Save(settings);
   if(NativeOnly)CoreParity(sw,model,directory);
   var initial=Stage(sw,model,directory,"initial",settings);
   project=SimulationStorage.Load(model);string beforeConfig=json.Serialize(project);project.solver.timestep=.002;SimulationStorage.Save(sw,model,project);
   var verified=SimulationStorage.Load(model);verified.solver.timestep=.001;Check(json.Serialize(verified)==beforeConfig,"only timestep configuration changed");
   var incremental=Stage(sw,model,directory,"incremental",settings);
   // Only delete caches inside this newly generated, owned fixture directory.
   foreach(var name in new[]{"cache","mesh-cache"}){string path=Path.GetFullPath(Path.Combine(directory,name));if(Path.GetDirectoryName(path)!=Path.GetFullPath(directory))throw new Exception("Cache containment check failed");if(Directory.Exists(path))Directory.Delete(path,true);}
   var full=Stage(sw,model,directory,"full",settings);
   File.WriteAllText(Path.Combine(directory,"native-counts.json"),json.Serialize(new{status="passed",source="new native SW assembly",stages=new[]{initial,incremental,full}}));Console.WriteLine("NATIVE_REPORT: "+directory);
  }catch(Exception error){Console.WriteLine(error.ToString());throw;}finally{
   Environment.SetEnvironmentVariable("SW2MUJOCO_CACHE",oldCache);Environment.SetEnvironmentVariable("SW2MUJOCO_MESH_CACHE",oldMesh);Environment.SetEnvironmentVariable("SW2MUJOCO_PROFILE",oldProfile);
   // Close only documents created by this probe. Never close an unexpected document.
   var docs=sw.GetDocuments() as object[];if(docs!=null)foreach(ModelDoc2 doc in docs){var path=doc.GetPathName();if(!string.IsNullOrEmpty(path)&&Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))sw.CloseDoc(doc.GetTitle());}
   if(sw.GetDocumentCount()==0)sw.ExitApp();
  }
 }
}
'@
[NativeIncrementalProbe]::NativeOnly=$NativeOnly.IsPresent
[NativeIncrementalProbe]::Run($directory)
$directory | Set-Content -LiteralPath (Join-Path $root 'build\native-incremental-directory.txt')
