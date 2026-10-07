param([Parameter(Mandatory=$true)][int]$ProcessId,[Parameter(Mandatory=$true)][string]$FixtureDirectory,[string]$Payload='sw2mujoco-final',[switch]$UI)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$fixture=(Resolve-Path -LiteralPath $FixtureDirectory).Path
$owned=[IO.Path]::GetFullPath((Join-Path $root 'build'))+[IO.Path]::DirectorySeparatorChar
if(-not $fixture.StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)){throw 'Only generated workspace fixtures are allowed'}
if([int](Get-Content -LiteralPath (Join-Path $fixture 'solidworks-process.txt')) -ne $ProcessId){throw 'Fixture process mismatch'}
$bin=Join-Path $root ('build\'+$Payload)
$interop='D:\sw\sw2025\SOLIDWORKS'
$refs=@("$interop\SolidWorks.Interop.sldworks.dll","$interop\SolidWorks.Interop.swconst.dll","$interop\SolidWorks.Interop.swpublished.dll","$bin\SW2URDF.dll","$bin\MathNet.Numerics.dll",'System.Windows.Forms','System.Drawing','System.Runtime.Serialization','System.Web.Extensions','System.Xml','System.Core')
$refs | Where-Object {$_ -like '*.dll'} | ForEach-Object {[Reflection.Assembly]::LoadFrom($_)|Out-Null}
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.Simulation;
using SW2URDF.URDFExport;
public static class SW2MuJoCoProbe {
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS: "+text);}
 static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model,string name){foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true))if(f.GetTypeName2()=="Attribute"){var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;if(a!=null&&a.GetName()==name)return a;}return null;}
 static SolidWorks.Interop.sldworks.Attribute Create(SldWorks sw,ModelDoc2 model,string name,bool version){var def=(AttributeDef)sw.DefineAttribute(name);def.AddParameter("data",(int)swParamType_e.swParamTypeString,0,0);if(version)def.AddParameter("exporterVersion",(int)swParamType_e.swParamTypeDouble,1.4,0);def.Register();return def.CreateInstance5(model,null,name,0,(int)swInConfigurationOpts_e.swAllConfiguration);}
 public static void Run(int pid,string directory,bool ui){
  var sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");if(sw.GetProcessID()!=pid)throw new Exception("Wrong SolidWorks process");
  var model=(ModelDoc2)sw.ActiveDoc;if(model==null||!string.Equals(model.GetPathName(),Path.Combine(directory,"isolated_fixture.SLDASM"),StringComparison.OrdinalIgnoreCase))throw new Exception("Wrong document; only generated fixture allowed");
  int errors=0,warnings=0;string first=model.ConfigurationManager.ActiveConfiguration.Name;
  var entry=SimulationStorage.LoadEntry(model);string xml=entry.urdf_xml;var project=SimulationStorage.Load(model);int count=project.collision.geometries.Count;
  model.AddConfiguration3("ProbeVariant","Generated test configuration","",0);model.ShowConfiguration2(first);
  Check(Find(model,SimulationStorage.NodeName).Delete(false),"remove generated unified node to simulate legacy input");
  var tree=Create(sw,model,ConfigurationSerialization.UrdfConfigurationSwAttributeName,true);((Parameter)tree.GetParameter("data")).SetStringValue2(xml,(int)swInConfigurationOpts_e.swAllConfiguration,"");
  var legacy=Create(sw,model,SimulationStorage.LegacyNodeName,false);
  var variant=new SimulationProject();variant.attachments.Add(new Attachment{name="variant_draft",link="arm",type="point"});
  ((Parameter)legacy.GetParameter("data")).SetStringValue2(new JavaScriptSerializer().Serialize(new{version=1,configurations=new Dictionary<string,SimulationProject>{{first,project},{"ProbeVariant",variant}}}),(int)swInConfigurationOpts_e.swAllConfiguration,"");
  Check(SimulationStorage.Load(model).collision.geometries.Count==count,"legacy simulation loads without writing nodes");
  Check(Find(model,SimulationStorage.NodeName)==null,"read does not migrate or modify assembly");
  SimulationStorage.Save(sw,model,project);
  Check(Find(model,SimulationStorage.NodeName)!=null&&Find(model,SimulationStorage.LegacyNodeName)==null&&Find(model,ConfigurationSerialization.UrdfConfigurationSwAttributeName)==null,"verified migration produces one unified node");
  model.ShowConfiguration2("ProbeVariant");Check(SimulationStorage.Load(model).attachments[0].name=="variant_draft","migration preserves other configuration simulation");
  bool abort;Check(ConfigurationSerialization.LoadBaseNodeFromModel(model,out abort)!=null&&!abort,"legacy shared tree preserved in other configuration");
  model.ShowConfiguration2(first);
  var root=ConfigurationSerialization.LoadBaseNodeFromModel(model,out abort);CommonSwOperations.LoadSWComponents(model,root,new List<string>());
  ConfigurationSerialization.SaveConfigTreeXML(sw,model,root,false);Check(SimulationStorage.Load(model).collision.geometries.Count==count,"tree save preserves collision configuration");
  var helper=new ExportHelper(sw){SavePath=directory,PackageName="plain",ShowExportLocation=false};helper.GetSimulation();Check(helper.CreateRobotFromTreeView(root),"build original URDF robot");
  helper.Simulation.Project.attachments.Add(new Attachment{name="unfinished",link="arm",type="point"});helper.ExportRobot();
  Check(helper.LastURDFPath!=null&&!File.Exists(Path.ChangeExtension(helper.LastURDFPath,".sim.json")),"plain URDF export ignores unfinished simulation and emits no sidecar");
  helper.Simulation.Project.attachments.RemoveAll(a=>a.name=="unfinished");
  helper.PackageName="with_config";helper.ExportSimulationInformation=true;helper.ExportRobot();Check(File.Exists(helper.LastSimulationPath),"optional URDF sidecar export remains available");
  var service=helper.GetSimulation();var source=service.Sources().First(s=>s.Type=="frame");service.Project.attachments.Add(service.Capture(source,"arm","probe_frame"));service.Save();
  using(var output=ProjectExport.ForReferenceTests(sw,model,first)){
   var sidecar=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(output.Sidecar));Check(sidecar.ContainsKey("attachments"),"engineering export resolves sites against exported robot frames");
   string final=Path.Combine(directory,"engineering_mjcf","engineering.xml");
   int code=PythonBackend.RunAsync(@"D:\Softwaves\python\python.exe",output.Urdf,output.Sidecar,final,false,null,Console.WriteLine).GetAwaiter().GetResult();
   Check(code==0&&File.Exists(final),"engineering MJCF conversion compiles and publishes XML");
   Check(Directory.GetFiles(Path.GetDirectoryName(final)).Length==1&&Directory.Exists(Path.Combine(Path.GetDirectoryName(final),"meshes")),"engineering output contains XML and meshes only");
   File.WriteAllText(Path.Combine(directory,"temporary-export-path.txt"),output.Urdf);
  }
  Check(!File.Exists(File.ReadAllText(Path.Combine(directory,"temporary-export-path.txt"))),"engineering intermediate URDF is cleaned up");
  model.Save3(1,ref errors,ref warnings);sw.CloseDoc(model.GetTitle());model=(ModelDoc2)sw.OpenDoc6(Path.Combine(directory,"isolated_fixture.SLDASM"),2,1,"",ref errors,ref warnings);
  Check(SimulationStorage.Load(model).attachments.Any(a=>a.name=="probe_frame"),"unified configuration persists after reopen");
  if(ui){
   var addin=new SW2URDF.SW.SwAddin();Check(addin.ConnectToSW(sw,18120),"connect final plugin menu in generated fixture instance");
   Console.WriteLine("READY: SW2MuJoCo menu and native editors");
   Application.Run(new Form{Text="SW2MuJoCo generated fixture validation",ShowInTaskbar=false,WindowState=FormWindowState.Minimized});GC.KeepAlive(addin);
  }
 }
}
'@
[SW2MuJoCoProbe]::Run($ProcessId,$fixture,$UI.IsPresent)
