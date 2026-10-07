param([Parameter(Mandatory=$true)][int]$ProcessId, [switch]$ShowUI, [string]$FixtureDirectory)
$ErrorActionPreference = 'Stop'
$workspacePath = $(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
if ($FixtureDirectory) {
    $probeDirectory = (Resolve-Path -LiteralPath $FixtureDirectory).Path
    $probeDirectory | Set-Content (Join-Path $workspacePath 'build\last-cad-test-directory.txt')
} else {
    throw 'Provide a newly generated FixtureDirectory; existing engineering files must not be used.'
}
$interopDirectory = 'D:\sw\sw2025\SOLIDWORKS'
$references = @("$interopDirectory\SolidWorks.Interop.sldworks.dll", "$interopDirectory\SolidWorks.Interop.swconst.dll", "$workspacePath\build\simulation\SWSimTool.dll", "$workspacePath\build\simulation\MathNet.Numerics.dll", 'System.Windows.Forms', 'System.Drawing', 'System.Runtime.Serialization', 'System.Web.Extensions')
$references | Where-Object { $_ -like '*.dll' } | ForEach-Object { [Reflection.Assembly]::LoadFrom($_) | Out-Null }
$references += @('System.Xml', 'System.Core', 'System.Numerics')
Add-Type -ReferencedAssemblies $references -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using SolidWorks.Interop.sldworks;
using SWSimTool.URDF;
using SWSimTool.URDFExport;
using SWSimTool.Simulation;
using SWSimTool.Utilities;
public static class SimulationCADProbe { public static bool MatchesProcess(object sw,int pid) { return ((SldWorks)sw).GetProcessID()==pid; }
 public static void Run(object swObject, string directory, bool showUI) {
  var sw=(SldWorks)swObject;
  int errors=0, warnings=0;
  var model=(ModelDoc2)sw.OpenDoc6(Path.Combine(directory,"isolated_fixture.SLDASM"),2,1,"",ref errors,ref warnings);
  if(model==null) throw new Exception("Open assembly failed: "+errors);
  bool abort;
  var node=ConfigurationSerialization.LoadBaseNodeFromModel(model,out abort);
  if(abort || node==null) throw new Exception("Missing fixture URDF tree");
  CommonSwOperations.LoadSWComponents(model,node,new List<string>());
  var helper=new ExportHelper(sw);
  if(!helper.CreateRobotFromTreeView(node)) throw new Exception("Tree conversion failed");
  string before=Path.Combine(directory,"before.urdf");
  helper.URDFRobot.WriteURDF(new URDFWriter(before).writer);
  var service=helper.GetSimulation();
  service.Project.attachments.Clear();
  var sources=service.Sources();
  var frame=sources.First(s=>s.Type=="frame");
  var child=helper.URDFRobot.BaseLink.Children[0];
  service.Project.attachments.Add(service.Capture(frame,child.Name,"cad_frame"));
  var output=service.Export(before);
  var items=(List<Dictionary<string,object>>)output["attachments"];
  var xyz=(double[])items[0]["xyz"];
  Console.WriteLine("CAD frame relative xyz="+string.Join(",",xyz.Select(v=>v.ToString("G17"))));
  // A point on a known sketch location, converted to a SolidWorks reference point.
  model.ClearSelection2(true);
  sw.ActivateDoc3(model.GetTitle(),false,0,ref errors);
  model.SketchManager.Insert3DSketch(true);
  var point=model.SketchManager.CreatePoint(0.02,0.03,0.04);
  if(point==null) throw new Exception("Sketch point creation returned null");
  model.SketchManager.Insert3DSketch(true);
  point.Select4(false,null);
  var points=(object[])model.FeatureManager.InsertReferencePoint((int)SolidWorks.Interop.swconst.swRefPointType_e.swRefPointSketchPoint,0,0,1);
  if(points==null || points.Length==0 || points[0]==null) throw new Exception("Reference point insertion failed");
  ((Feature)points[0]).Name="SimulationProbePoint";
  service.Project.attachments.Add(service.Capture(service.Sources().First(s=>s.Type=="point" && s.Feature.Name=="SimulationProbePoint"),helper.URDFRobot.BaseLink.Name,"cad_point"));
  output=service.Export(before);
  items=(List<Dictionary<string,object>>)output["attachments"];
  if(items[1].ContainsKey("rpy")) throw new Exception("Point unexpectedly has orientation");
  var rootTransform=MathOps.GetTransformation(helper.AttachmentCoordinateTransform(helper.URDFRobot.BaseLink.Joint.CoordinateSystemName));
  var expected=MathOps.GetXYZ(rootTransform.Inverse()*MathOps.GetTranslation(new double[]{0.02,0.03,0.04}));
  var actual=(double[])items[1]["xyz"];
  for(int i=0;i<3;i++) if(Math.Abs(expected[i]-actual[i])>1e-8) throw new Exception("Point relative coordinate mismatch");
  string after=Path.Combine(directory,"after.urdf");
  helper.URDFRobot.WriteURDF(new URDFWriter(after).writer);
  if(!File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after))) throw new Exception("URDF changed");
  SimulationProject.Write(Path.Combine(directory,"simulation_test.sim.json"),output);
  var loaded=SimulationStorage.Load(model);
  if(loaded.attachments.Count!=2) throw new Exception("Project persistence failed");
  Console.WriteLine("PASS: CAD feature PID resolution, local point/frame export, position-only point, project persistence, byte-identical URDF");
  helper.PackageName="cad_robot"; helper.SavePath=directory; helper.ShowExportLocation=false;
  helper.ExportRobot();
  if(!File.Exists(helper.LastSimulationPath)) throw new Exception("Sidecar missing after actual mesh export");
  Console.WriteLine("PASS: actual URDF + mesh + sidecar export: "+helper.LastSimulationPath);
  if(showUI) using(var form=new SWSimTool.UI.SimulationConfigForm(service,child.Name,new string[]{child.Joint.Name})) form.ShowDialog();
  // Close only this disposable fixture; never close the user's existing document.
  if(!showUI) sw.CloseDoc(model.GetTitle());
 }
}
'@
try { $testSw = [Runtime.InteropServices.Marshal]::GetActiveObject("SldWorks.Application"); if(-not [SimulationCADProbe]::MatchesProcess($testSw,$ProcessId)) {throw "Refusing a different SolidWorks process"}; [SimulationCADProbe]::Run($testSw, $probeDirectory, $ShowUI.IsPresent) }
catch { Write-Output $_.Exception.ToString(); throw }
