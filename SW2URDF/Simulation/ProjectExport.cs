using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using System;
using System.IO;
namespace SW2URDF.Simulation {
 public sealed class ProjectExport : IDisposable {
  readonly string temporary;
  readonly ModelDoc2 document;
  public string Urdf{get;private set;} public string Sidecar{get;private set;}
  public string ExportId{get;}=Guid.NewGuid().ToString("N");
  public static int SourceBuildCount{get;private set;}
  public ExportPlan Plan{get;private set;}
  public ProjectExport(SldWorks app,ModelDoc2 expected,string configuration){
   if(!ReferenceEquals(app.ActiveDoc,expected)||expected.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("当前装配或 Configuration 已切换，请重新打开工程导出。");
   document=expected;temporary=Path.Combine(Path.GetTempPath(),"SW2MuJoCo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
   try{
    var current=SimulationStorage.Load(expected)??new SimulationProject();current.NormalizeSiteReferences();current.ValidateSolver();current.collision=current.collision??new CollisionConfiguration();
    var entry=SimulationStorage.LoadEntry(expected);double treeVersion;string treeData=entry==null?ConfigurationSerialization.GetLegacyConfigTreeData(expected,out treeVersion):entry.urdf_xml;
    string source=ProjectSourceCache.Source(app,expected,treeData),geometry=ProjectSourceCache.Geometry(current);
    var cached=ProjectSourceCache.Find(expected,source,geometry);
    Plan=ExportPlan.Build(cached!=null,SimulationSession.Dirty(expected)|ExportFingerprint.Changed(cached?.project,current));
    if(cached!=null){
     Urdf=Path.Combine(cached.folder,cached.urdf);Sidecar=Path.Combine(temporary,"robot.sim.json");
     System.Collections.Generic.Dictionary<string,object> data;
     if(cached.geometry==geometry)data=ProjectSourceCache.Overlay(cached,current);
     else{
      var resolver=new ExportHelper(app).GetSimulation();resolver.Project=current;resolver.UseExportFrames(cached.frames);
      data=resolver.Export(Urdf);data["urdf_sha256"]=cached.sidecar["urdf_sha256"];
      cached.geometry=geometry;cached.sidecar=data;
     }
     SimulationProject.Write(Sidecar,data);return;
    }
    SourceBuildCount++;CadRevision.Invalidate(expected);
    bool error;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(expected,out error);
    if(error||tree==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");
    CommonSwOperations.LoadSWComponents(expected,tree,new System.Collections.Generic.List<string>());
    var helper=new ExportHelper(app){SavePath=temporary,PackageName="robot",ShowExportLocation=false,ExportSimulationInformation=true};
    helper.GetSimulation().Project=current;if(!helper.CreateRobotFromTreeView(tree))throw new InvalidOperationException("URDF 构建失败，请检查 link / joint 配置。");
    helper.ExportRobot();Urdf=helper.LastURDFPath;Sidecar=helper.LastSimulationPath;
    if(Urdf==null||Sidecar==null)throw new IOException("临时 URDF 或附加配置导出失败。");
    // Export can update display state/preferences; establish the final metadata revision.
    source=ProjectSourceCache.Source(app,expected,treeData);
    var resolved=ExportFingerprint.Serializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(File.ReadAllText(Sidecar));
    ProjectSourceCache.Store(expected,source,geometry,temporary,Urdf,current,resolved,System.Linq.Enumerable.ToDictionary(helper.GetSimulation().LinkTransforms(),x=>x.Key,x=>x.Value.ToRowMajorArray()));
    var stored=ProjectSourceCache.Find(expected,source,geometry);if(stored!=null)Urdf=Path.Combine(stored.folder,stored.urdf);
   }catch{Dispose();throw;}
  }
  public void MarkSucceeded(){SimulationSession.ExportSucceeded(document);}
  public void Dispose(){if(Directory.Exists(temporary)&&Path.GetDirectoryName(Path.GetFullPath(temporary))==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(temporary).StartsWith("SW2MuJoCo-",StringComparison.Ordinal))Directory.Delete(temporary,true);}
 }
}
