param([string]$Payload="bin/SWSimTool/Release/net48")
$ErrorActionPreference='Stop'
$root=$(for ($p=$PSScriptRoot; $p; $p=Split-Path -Parent $p) { if (Test-Path -LiteralPath (Join-Path $p 'SWSimTool.sln')) { $p; break } })
$bin=Join-Path $root ('build\'+$Payload)
$refs=@("$bin\SWSimTool.dll",'System.Core','System.Xml','System.Xml.Linq','System.Web.Extensions','System.Runtime.Serialization')
[Reflection.Assembly]::LoadFrom("$bin\SWSimTool.dll")|Out-Null
Add-Type -ReferencedAssemblies $refs -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Web.Script.Serialization;using SWSimTool.RobotModel;using SWSimTool.Simulation;
public static class ConfigBuilderProbe {
 static void Check(bool x,string name){if(!x)throw new Exception(name);Console.WriteLine("PASS: "+name);}
 public static void Run(){
  var core=new RobotCoreSnapshot("typed",new[]{new LinkSnapshot("base-id","base",new InertialSnapshot(1,RigidTransform.Identity,new SymmetricInertia(.1,.1,.1,0,0,0)),new GeometrySnapshot[0]),new LinkSnapshot("arm-id","arm",new InertialSnapshot(1,RigidTransform.Identity,new SymmetricInertia(.1,.1,.1,0,0,0)),new GeometrySnapshot[0])},new[]{new JointSnapshot("hinge-id","hinge","base-id","arm-id",JointKind.Continuous,RigidTransform.Identity,new Vector3d(0,0,1),null,null,0,0)});
  var sites=new[]{new SiteSnapshot("a-id","a","base-id",true,new RigidTransform(new Vector3d(0,.1,0),Quaterniond.Identity)),new SiteSnapshot("b-id","b","arm-id",true,new RigidTransform(new Vector3d(.1,0,0),Quaterniond.Identity))};
  var proxy=new CollisionGeometrySnapshot("proxy-id","proxy","arm-id","arm-id","box",new[]{.1,.2,.3},new double[3],new double[3]);
  var baseProxy=new CollisionGeometrySnapshot("base-proxy-id","base_proxy","base-id","base-id","box",new[]{.1,.2,.3},new double[3],new double[3]);
  var geometry=new ResolvedSimulationGeometry(sites,new[]{proxy,baseProxy});
  var p=new SimulationProject{solver=new SolverSettings{enabled=true},collision=new CollisionConfiguration()};
  p.collision.link_modes["base"]="primitive";p.collision.geometries.Add(new CollisionGeometry{id="base-proxy-id",name="base_proxy",link="base",link_id="base-id",size=new[]{.1,.2,.3}});p.collision.link_modes["arm"]="primitive";p.collision.geometries.Add(new CollisionGeometry{id="proxy-id",name="proxy",link="arm",link_id="arm-id",size=new[]{.1,.2,.3}});p.collision.allowed_pairs.Add(new CollisionPair{link1="base",link1_id="base-id",link2="arm",link2_id="arm-id",solver=ConstraintSettings.Contact()});
  p.joints.Add(new JointConfiguration{joint="hinge",joint_id="hinge-id",damping=.2,frictionloss=.1,armature=.01,spring_mode="custom",stiffness=2,springref=.1,limit_mode="custom",lower=-1,upper=1,limit_solver=JointConfiguration.LimitDefaults()});p.joint_force_limits.Add(new JointForceLimit{joint="hinge",joint_id="hinge-id",lower=-2,upper=2});
  foreach(string type in new[]{"motor","position","velocity"})p.actuators.Add(new ActuatorConfig{id=type+"-id",name=type,joint="hinge",joint_id="hinge-id",type=type,gain=2});
  foreach(string type in new[]{"imu","tof","camera"})p.sensors.Add(new SensorConfig{id=type+"-id",name=type,site="b",site_id="b-id",type=type});
  foreach(string type in new[]{"connect","weld","joint"})p.equalities.Add(new EqualityConfig{id=type+"-id",name=type,type=type,site1="a",site1_id="a-id",site2="b",site2_id="b-id",joint1="hinge",joint1_id="hinge-id",polycoef=new double[5],solver=new ConstraintSettings()});
  foreach(string type in new[]{"pull","push","spring"})p.site_forces.Add(new SiteForceConfig{id=type+"-id",name=type,type=type,site1="a",site1_id="a-id",site2="b",site2_id="b-id",magnitude=.1,stiffness=1,damping=.1});
  var json=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
  var legacy=json.Deserialize<Dictionary<string,object>>(json.Serialize(p));
  legacy["attachments"]=sites.Select(x=>new{id=x.Id,name=x.Name,link=x.LinkId,link_id=x.LinkId,type="frame",xyz=new[]{x.LinkFromSite.Translation.X,x.LinkFromSite.Translation.Y,x.LinkFromSite.Translation.Z},rpy=new double[3]}).ToArray();
  var assets=new PreparedAssets(new PreparedMeshAsset[0]);var context=new ExportContext("typed");
  var snapshot=SimulationConfigBuilder.Build(p,core,geometry);var direct=MjcfExporter.Generate(new RobotModel(core,snapshot),assets,context);
  var reference=MjcfExporter.Generate(new RobotModel(core,LegacySimulationConfigImporter.Import(json.Serialize(legacy),core)),assets,context);
  Check(direct==reference,"typed configuration and legacy adapter agree for all extension families");
  p.solver.timestep=.002;p.equalities[2].polycoef[0]=99;p.collision.geometries[0].size[0]=99;p.joints[0].damping=99;p.actuators.Clear();
  Check(direct==MjcfExporter.Generate(new RobotModel(core,snapshot),assets,context),"project changes cannot mutate an existing snapshot");
  foreach(string target in new[]{"joint","site","link"}){
   var bad=new SimulationProject{collision=new CollisionConfiguration()};
   if(target=="joint")bad.actuators.Add(new ActuatorConfig{joint="hinge",joint_id="deleted"});
   if(target=="site")bad.sensors.Add(new SensorConfig{site="b",site_id="deleted"});
   if(target=="link")bad.collision.allowed_pairs.Add(new CollisionPair{link1="base",link1_id="deleted",link2="arm",link2_id="arm-id"});
   bool rejected=false;try{SimulationConfigBuilder.Build(bad,core,geometry);}catch(InvalidDataException){rejected=true;}Check(rejected,"deleted "+target+" ID cannot rebind to an existing name");
  }
  Check(typeof(SimulationConfigSnapshot).GetConstructors().All(c=>c.GetParameters().All(x=>x.ParameterType!=typeof(string)||x.Name=="BaseMode")),"domain snapshot has no JSON constructor");
 }
}
'@
[ConfigBuilderProbe]::Run()

