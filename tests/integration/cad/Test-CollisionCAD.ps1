param([Parameter(Mandatory=$true)][int]$ProcessId,[Parameter(Mandatory=$true)][string]$FixtureDirectory,[switch]$NoUI,[string]$Payload="swsimtool-release")
$ErrorActionPreference='Stop'
$workspacePath=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$fixturePath=(Resolve-Path -LiteralPath $FixtureDirectory).Path
$ownedRoot=[IO.Path]::GetFullPath((Join-Path $workspacePath 'build'))+[IO.Path]::DirectorySeparatorChar
if(-not $fixturePath.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Only newly generated workspace fixtures are permitted'}
if([int](Get-Content -LiteralPath (Join-Path $fixturePath 'solidworks-process.txt')) -ne $ProcessId){throw 'Fixture process marker mismatch'}
$interopDirectory='D:\sw\sw2025\SOLIDWORKS'
$payloadDirectory=Join-Path $workspacePath ('build\'+$Payload)
$references=@("$interopDirectory\SolidWorks.Interop.sldworks.dll","$interopDirectory\SolidWorks.Interop.swconst.dll","$interopDirectory\SolidWorks.Interop.swpublished.dll","$payloadDirectory\SWSimTool.dll","$payloadDirectory\MathNet.Numerics.dll",'System.Windows.Forms','System.Drawing','System.Runtime.Serialization','System.Web.Extensions','System.Xml','System.Core')
$references | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SWSimTool.URDFExport;
using SWSimTool.Simulation;
public static class CollisionCADProbe {
 public static void Run(int pid,string directory,bool noUI){
  var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
  if(sw.GetProcessID()!=pid)throw new Exception("Different SolidWorks process; refusing");
  var model=(ModelDoc2)sw.ActiveDoc;
  string expected=Path.Combine(directory,"isolated_fixture.SLDASM");
  if(model==null||!string.Equals(model.GetPathName(),expected,StringComparison.OrdinalIgnoreCase))throw new Exception("Only this generated fixture may be used");
  int errors=0,warnings=0;bool abort;
  Console.WriteLine("STEP: restore tree");
  var root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out abort);
  if(abort||root==null)throw new Exception("Missing fixture URDF tree");
  CommonSwOperations.LoadSWComponents(model,root,new List<string>());
  Console.WriteLine("STEP: construct helper");
  var helper=new ExportHelper(sw);
  Console.WriteLine("STEP: build links");
  if(!helper.CreateRobotFromTreeView(root))throw new Exception("Fixture conversion failed");
  Console.WriteLine("STEP: load simulation");
  var service=helper.GetSimulation();service.SetCollisionTree(root);service.Project.collision=new CollisionConfiguration();
  string baseline=Path.Combine(directory,"collision-before.urdf");
  helper.URDFRobot.WriteURDF(new SWSimTool.URDF.URDFWriter(baseline).writer);
  foreach(string kind in new[]{"box","sphere","cylinder","capsule"}){
   var g=new CollisionGeometry{name="test_"+kind,link="arm",type=kind,xyz=new[]{.01,.02,.03},rpy=new[]{.1,.2,.3},size=kind=="box"?new[]{.04,.03,.02}:kind=="sphere"?new[]{.01}:new[]{.01,.05}};
   service.ResolveCollision(g);double before=model.Extension.CreateMassProperty().Mass;
   Console.WriteLine("STEP: preview "+kind);
   using(var preview=new CollisionPreview(service)){
    preview.Show(new[]{g},g.id);
    if(kind=="box"){
     var temporary=(Body2)((System.Collections.IList)typeof(CollisionPreview).GetField("bodies",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(preview))[0];
     var displayHost=(Component2)typeof(CollisionPreview).GetField("host",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).GetValue(preview);
     var pose=SWSimTool.Utilities.MathOps.GetTransformation(displayHost.Transform2).Inverse()*service.LinkTransforms()[g.link]*SWSimTool.Utilities.MathOps.GetTransformation(g.xyz,g.rpy);
     var inverse=pose.Inverse();
     var vertices=((object[])temporary.GetVertices()).Cast<Vertex>().Select(v=>SWSimTool.Utilities.MathOps.GetXYZ(inverse*SWSimTool.Utilities.MathOps.GetTranslation((double[])v.GetPoint()))).ToArray();
     for(int axis=0;axis<3;axis++)if(Math.Abs(vertices.Average(v=>v[axis]))>1e-9||Math.Abs(vertices.Max(v=>v[axis])-g.size[axis]/2)>1e-9||Math.Abs(vertices.Min(v=>v[axis])+g.size[axis]/2)>1e-9)throw new Exception("Temporary box centre/extents differ from MJCF");
     Console.WriteLine("PASS: transformed temporary box has the exact MJCF centre and dimensions");
     int created=preview.CreatedBodyCount,redraw=preview.RedrawCount;
     preview.Show(new[]{g},g.id);if(preview.CreatedBodyCount!=created||preview.RedrawCount!=redraw)throw new Exception("Unchanged preview did work");
     g.name="renamed_box";preview.Show(new[]{g},g.id);if(preview.RedrawCount!=redraw)throw new Exception("Name edit redrew geometry");
     g.xyz[0]+=.005;preview.Show(new[]{g},g.id);if(preview.CreatedBodyCount!=created||preview.TransformedBodyCount!=1)throw new Exception("Pose edit rebuilt geometry");
     g.size[0]+=.001;preview.Show(new[]{g},g.id);if(preview.CreatedBodyCount!=created+1)throw new Exception("Size edit did not rebuild only one body");
     preview.Show(new[]{g},null);if(preview.CreatedBodyCount!=created+1)throw new Exception("Highlight rebuilt geometry");
     g.name="test_box";
     Console.WriteLine("PASS: unchanged/name updates do no work; pose reuses body; size rebuilds one body; highlight reuses body");
    }
    if(Math.Abs(before-model.Extension.CreateMassProperty().Mass)>1e-12)throw new Exception("Preview changed mass");
   }
   service.Project.collision.geometries.Add(g);
  }
  service.Project.collision.link_modes["arm"]="primitive";
  Console.WriteLine("STEP: persistent model references");
  model.ClearSelection2(true);
  model.SketchManager.Insert3DSketch(true);
  model.SketchManager.AddToDB=true;
  var p1=model.SketchManager.CreatePoint(.02,.03,.04);
  var p2=model.SketchManager.CreatePoint(.02,.03,.09);
  model.SketchManager.AddToDB=false;
  model.SketchManager.Insert3DSketch(true);
  model.ClearSelection2(true);if(!p1.Select4(false,null))throw new Exception("Point 1 selection failed");var ref1=service.CaptureSelection();
  model.ClearSelection2(true);if(!p2.Select4(false,null))throw new Exception("Point 2 selection failed");var ref2=service.CaptureSelection();
  Console.WriteLine("Point coordinates: "+p1.X+","+p1.Y+","+p1.Z+" / "+p2.X+","+p2.Y+","+p2.Z+"; same PID="+(ref1.pid==ref2.pid));
  var refSphere=new CollisionGeometry{name="reference_sphere",link="base_link",type="sphere",definition="radius_points",references=new List<CollisionReference>{ref1,ref2},size=new[]{.01}};
  service.ResolveCollision(refSphere);
  if(Math.Abs(refSphere.size[0]-.05)>1e-9)throw new Exception("Point radius mismatch");
  var refCylinder=new CollisionGeometry{name="reference_cylinder",link="arm",type="cylinder",definition="endpoints",references=new List<CollisionReference>{ref1,ref2},size=new[]{.01,.02}};
  service.ResolveCollision(refCylinder);
  if(Math.Abs(refCylinder.size[1]-.05)>1e-9)throw new Exception("Endpoint length mismatch");
  service.Project.collision.geometries.Add(refSphere);service.Project.collision.geometries.Add(refCylinder);
  model.ClearSelection2(true);
  var component=((object[])((AssemblyDoc)model).GetComponents(true)).Cast<Component2>().First();
  var frame=component.FeatureByName("FixtureFrame");
  if(frame==null||!frame.Select2(false,0))throw new Exception("Component frame selection failed");
  var frameRef=service.CaptureSelection();
  var refBox=new CollisionGeometry{name="reference_box",link="arm",type="box",definition="frame",references=new List<CollisionReference>{frameRef},size=new[]{.02,.03,.04}};
  service.ResolveCollision(refBox);service.Project.collision.geometries.Add(refBox);
  var corner=new CollisionGeometry{name="corner_probe",link="arm",type="box",definition="corner_frame",references=new List<CollisionReference>{frameRef},size=new[]{.02,.03,.04},corner_signs=new[]{1,-1,1}};
  service.ResolveCollision(corner);
  var basePose=service.LinkTransforms()[refBox.link]*SWSimTool.Utilities.MathOps.GetTransformation(refBox.xyz,refBox.rpy);
  var cornerPose=service.LinkTransforms()[corner.link]*SWSimTool.Utilities.MathOps.GetTransformation(corner.xyz,corner.rpy);
  var cornerLocal=SWSimTool.Utilities.MathOps.GetXYZ(basePose.Inverse()*cornerPose);
  for(int i=0;i<3;i++)if(Math.Abs(cornerLocal[i]-corner.size[i]*corner.corner_signs[i]/2)>1e-9)throw new Exception("Corner centre mismatch");
  foreach(int ax in new[]{0,1,2})foreach(int sign in new[]{-1,1}){
   var cyl=new CollisionGeometry{name="end_probe",link="arm",type="cylinder",definition="end_frame",references=new List<CollisionReference>{frameRef},size=new[]{.01,.05},axis=ax,axis_sign=sign};service.ResolveCollision(cyl);
   var local=basePose.Inverse()*service.LinkTransforms()[cyl.link]*SWSimTool.Utilities.MathOps.GetTransformation(cyl.xyz,cyl.rpy);
   for(int i=0;i<3;i++)if(Math.Abs(local[i,3]-(i==ax?sign*.025:0))>1e-9||Math.Abs(local[i,2]-(i==ax?sign:0))>1e-9)throw new Exception("Cylinder end/axis mismatch");
  }
  Console.WriteLine("PASS: corner-frame box and all six end-frame cylinder directions");
  var part=(PartDoc)component.GetModelDoc2();var body=(Body2)((object[])part.GetBodies2(0,false))[0];
  var localFace=(Face2)((object[])body.GetFaces())[0];var assemblyFace=(Face2)component.GetCorrespondingEntity(localFace);
  var edge=(Edge)((object[])localFace.GetEdges())[0];var assemblyEdge=(Edge)component.GetCorrespondingEntity(edge);model.ClearSelection2(true);if(!((Entity)assemblyEdge).Select4(false,null))throw new Exception("Edge selection failed");
  var edgeRef=service.CaptureSelection();double edgeLength=service.ReferenceEdgeLength(edgeRef);
  refBox.dimension_references["size0"]=edgeRef;service.ResolveCollision(refBox);if(Math.Abs(refBox.size[0]-edgeLength)>1e-9)throw new Exception("Referenced edge dimension mismatch");
  Console.WriteLine("PASS: selected straight edge drives independent box dimension");
  model.ClearSelection2(true);if(!((Entity)assemblyFace).Select4(false,null))throw new Exception("Rectangle face selection failed");
  var rectangle=new CollisionGeometry{name="reference_rectangle",link="arm",type="box",definition="rectangle_face",references=new List<CollisionReference>{service.CaptureSelection()},size=new[]{.01,.01,.01},thickness=.01};
  service.ResolveCollision(rectangle);if(rectangle.size.Any(v=>v<=0))throw new Exception("Invalid rectangle dimensions");
  service.Project.collision.geometries.Add(rectangle);
  Console.WriteLine("PASS: persistent sketch point and component coordinate-system references");
  Console.WriteLine("STEP: save Attribute");service.Save();
  string after=Path.Combine(directory,"collision-after.urdf");helper.URDFRobot.WriteURDF(new SWSimTool.URDF.URDFWriter(after).writer);
  if(!File.ReadAllBytes(baseline).SequenceEqual(File.ReadAllBytes(after)))throw new Exception("Collision configuration changed URDF");
  if(!model.Save3(1,ref errors,ref warnings))throw new Exception("Assembly save failed");
  sw.CloseDoc(model.GetTitle());
  model=(ModelDoc2)sw.OpenDoc6(expected,2,1,"",ref errors,ref warnings);
  var saved=SimulationStorage.Load(model);
  if(saved==null||saved.collision.geometries.Count!=8)throw new Exception("Configuration reopen failed");
  Console.WriteLine("PASS: four temporary preview shapes, unchanged mass, Attribute save/reopen");
  root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out abort);CommonSwOperations.LoadSWComponents(model,root,new List<string>());
  helper=new ExportHelper(sw);if(!helper.CreateRobotFromTreeView(root))throw new Exception("Reopened tree conversion failed");
  service=helper.GetSimulation();foreach(var geometry in service.Project.collision.geometries)service.ResolveCollision(geometry);
  helper.PackageName="collision_robot";helper.SavePath=directory;helper.ShowExportLocation=false;helper.ExportRobot();
  if(!File.Exists(helper.LastSimulationPath))throw new Exception("Collision sidecar missing");
  Console.WriteLine("PASS: references resolve after reopen; byte-identical URDF; actual STL and collision sidecar export: "+helper.LastSimulationPath);
  if(noUI)return;
  service.Project.collision.geometries=service.Project.collision.geometries.Where(g=>g.name=="test_box").ToList();
  Console.WriteLine("STEP: show left PropertyManager");
  var page=new SWSimTool.UI.CollisionPropertyManager(helper.GetSimulation(),"arm");model.ViewZoomtofit2();page.Show();
  System.Windows.Forms.Application.Run(new System.Windows.Forms.Form{Text="Collision configuration validation host",Width=350,Height=120});GC.KeepAlive(sw);GC.KeepAlive(page);
 }
}
'@
[CollisionCADProbe]::Run($ProcessId,$fixturePath,$NoUI.IsPresent)
