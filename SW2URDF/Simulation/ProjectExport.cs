using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using System;
using System.IO;
namespace SW2URDF.Simulation {
 public sealed class ProjectExport : IDisposable {
  readonly string temporary;
  readonly ModelDoc2 document;
  public string Urdf{get;private set;} public string Sidecar{get;private set;}
  public SW2URDF.RobotModel.RobotCoreSnapshot Core{get;private set;}
  SW2URDF.RobotModel.SimulationConfigSnapshot simulation;
  public SW2URDF.RobotModel.RobotModel NativeModel(){if(Core==null||simulation==null)throw new InvalidOperationException("Native model is unavailable; rebuild the project source.");return new SW2URDF.RobotModel.RobotModel(Core,simulation);}
  public string ExportId{get;}=Guid.NewGuid().ToString("N");
  public static int SourceBuildCount{get;private set;}
  public ExportPlan Plan{get;private set;}
  public ProjectExport(SldWorks app,ModelDoc2 expected,string configuration):this(app,expected,configuration,true){}
  public static ProjectExport ForReferenceTests(SldWorks app,ModelDoc2 expected,string configuration)=>new ProjectExport(app,expected,configuration,false);
  private ProjectExport(SldWorks app,ModelDoc2 expected,string configuration,bool nativeOnly){
   if(!ReferenceEquals(app.ActiveDoc,expected)||expected.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("当前装配或 Configuration 已切换，请重新打开工程导出。");
   document=expected;temporary=Path.Combine(Path.GetTempPath(),"SW2MuJoCo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
   try{
    var current=SimulationStorage.Load(expected)??new SimulationProject();current.NormalizeSiteReferences();current.ValidateSolver();current.collision=current.collision??new CollisionConfiguration();
    var entry=SimulationStorage.LoadEntry(expected);double treeVersion;string treeData=entry==null?ConfigurationSerialization.GetLegacyConfigTreeData(expected,out treeVersion):entry.urdf_xml;
    string source=ProjectSourceCache.Source(app,expected,treeData),geometry=ProjectSourceCache.Geometry(current);
    var cached=ProjectSourceCache.Find(expected,source,geometry);
    if(cached!=null&&(cached.nativeSource!=nativeOnly||(nativeOnly?cached.core==null||cached.resolvedGeometry==null:cached.urdf==null)))cached=null;
    Plan=ExportPlan.Build(cached!=null,SimulationSession.Dirty(expected)|ExportFingerprint.Changed(cached?.project,current));
    if(cached!=null){
     Core=cached.core;
     bool geometryChanged=cached.geometry!=geometry;
     if(nativeOnly){
      var resolvedGeometry=cached.resolvedGeometry;
      if(cached.geometry!=geometry){var resolver=new ExportHelper(app).GetSimulation();resolver.Project=current;resolver.UseExportFrames(cached.frames,Core);resolvedGeometry=resolver.ResolveNativeGeometry(Core);cached.resolvedGeometry=resolvedGeometry;cached.geometry=geometry;}
      simulation=SimulationConfigBuilder.Build(current,Core,resolvedGeometry);return;
     }
     Urdf=cached.urdf==null?null:Path.Combine(cached.folder,cached.urdf);Sidecar=Path.Combine(temporary,"robot.sim.json");
     System.Collections.Generic.Dictionary<string,object> data;
     if(cached.geometry==geometry)data=ProjectSourceCache.Overlay(cached,current);
     else{
      var resolver=new ExportHelper(app).GetSimulation();resolver.Project=current;resolver.UseExportFrames(cached.frames,Core);
      data=resolver.Export(Urdf??"robot.urdf");if(cached.sidecar.ContainsKey("urdf_sha256"))data["urdf_sha256"]=cached.sidecar["urdf_sha256"];
      if(cached.sidecar.ContainsKey("identities"))data["identities"]=cached.sidecar["identities"];
      cached.geometry=geometry;cached.sidecar=data;
     }
     SimulationProject.Write(Sidecar,data);
     if(geometryChanged&&Core!=null){var resolver=new ExportHelper(app).GetSimulation();resolver.Project=current;resolver.UseExportFrames(cached.frames,Core);cached.resolvedGeometry=resolver.ResolveNativeGeometry(Core);}
     if(Core!=null&&cached.resolvedGeometry!=null)simulation=SimulationConfigBuilder.Build(current,Core,cached.resolvedGeometry);return;
    }
    SourceBuildCount++;CadRevision.Invalidate(expected);
    var built=nativeOnly?ProjectSourceBuilder.BuildNative(app,expected,temporary,current):ProjectSourceBuilder.BuildReference(app,expected,temporary,current);
    Core=built.Core;Urdf=built.Urdf;Sidecar=built.Sidecar;
    simulation=SimulationConfigBuilder.Build(current,Core,built.Geometry);
    // CAD/export preferences may change the stamp: cache the final revision.
    source=ProjectSourceCache.Source(app,expected,treeData);
    ProjectSourceCache.Store(expected,source,geometry,temporary,Urdf,current,built.LegacyData,built.Frames,Core,built.Geometry,nativeOnly);
    var stored=ProjectSourceCache.Find(expected,source,geometry);if(stored!=null){Urdf=stored.urdf==null?null:Path.Combine(stored.folder,stored.urdf);Core=stored.core;}
   }catch{Dispose();throw;}
  }
  public void MarkSucceeded(){SimulationSession.ExportSucceeded(document);}
  public void Dispose(){if(Directory.Exists(temporary)&&Path.GetDirectoryName(Path.GetFullPath(temporary))==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(temporary).StartsWith("SW2MuJoCo-",StringComparison.Ordinal))Directory.Delete(temporary,true);}
 }
}
