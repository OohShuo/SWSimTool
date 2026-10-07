using SolidWorks.Interop.sldworks;
using SWSimTool.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Environment=System.Environment;
using System.Security.Cryptography;
namespace SWSimTool.Simulation {
 // Sessions never trust CAD revision stamps from a previous document open.
 public static class ProjectSourceCache {
  public sealed class Entry:ProjectCacheEntry {}
  sealed class State {public Dictionary<string,Entry> entries=new Dictionary<string,Entry>();}
  static readonly ConditionalWeakTable<ModelDoc2,State> states=new ConditionalWeakTable<ModelDoc2,State>();
  public static string Root => SourceCacheRepository.Root;
  public static string Source(SldWorks app,ModelDoc2 model,string tree){
   var dependencies=new List<object>();var assembly=model as AssemblyDoc;
   if(assembly!=null)foreach(Component2 component in (object[])assembly.GetComponents(false)??new object[0]){
    // Short-lived COM metadata reads only, never shape or transform queries.
    var doc=component.GetModelDoc2() as ModelDoc2;
    if(doc==null)return null; // Unresolved/lightweight dependencies cannot establish validity.
    string path=doc.GetPathName();if(string.IsNullOrEmpty(path))return null;
    var file=SourceCacheRepository.Metadata(path);
    dependencies.Add(new{component=component.Name2,configuration=component.ReferencedConfiguration,path,stamp=doc.GetUpdateStamp(),size=file.Exists?file.Length:0,time=file.Exists?file.LastWriteTimeUtc.Ticks:0});
   }
   return ExportFingerprint.Hash(new{revision=CadRevision.Get(model),tree,dependencies=dependencies.OrderBy(x=>ExportFingerprint.Serializer().Serialize(x),StringComparer.Ordinal).ToArray(),generator=typeof(ProjectSourceCache).Assembly.GetName().Version.ToString()});
  }
  public static string Geometry(SimulationProject project){return ExportFingerprint.Hash(new{project.attachments,geometries=project.collision?.geometries});}
  public static Entry Find(ModelDoc2 model,string source,string geometry){
   if(source==null||Environment.GetEnvironmentVariable("SWSIMTOOL_USE_INCREMENTAL_EXPORT")=="0")return null;
   Entry entry;var state=states.GetValue(model,x=>new State());if(!state.entries.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out entry)||entry.source!=source||(entry.geometry!=geometry&&entry.frames==null))return null;
   return SourceCacheRepository.Validate(entry)?entry:null;
  }
  public static void Store(ModelDoc2 model,string source,string geometry,string folder,string urdf,SimulationProject project,Dictionary<string,object> sidecar,Dictionary<string,double[]> frames=null,SWSimTool.RobotModel.RobotCoreSnapshot core=null,ResolvedSimulationGeometry resolvedGeometry=null,bool nativeSource=false){
   if(source==null||Environment.GetEnvironmentVariable("SWSIMTOOL_USE_INCREMENTAL_EXPORT")=="0")return;
   try{
    var entry=new Entry{source=source,geometry=geometry,project=project,sidecar=sidecar,frames=frames,resolvedGeometry=resolvedGeometry,core=core,nativeSource=nativeSource};
    SourceCacheRepository.Store(entry,folder,urdf);
    states.GetValue(model,x=>new State()).entries[model.ConfigurationManager.ActiveConfiguration.Name]=entry;
   }catch(IOException){}catch(UnauthorizedAccessException){} // Caching is optional.
  }
  public static Dictionary<string,object> Overlay(Entry entry,SimulationProject current)=>SourceCacheRepository.Overlay(entry,current);
 }
}
