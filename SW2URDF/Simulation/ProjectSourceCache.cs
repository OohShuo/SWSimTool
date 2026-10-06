using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Environment=System.Environment;
using System.Security.Cryptography;
namespace SW2URDF.Simulation {
 // Sessions never trust CAD revision stamps from a previous document open.
 public static class ProjectSourceCache {
  public sealed class Entry {public string source,geometry,folder,urdf;public SimulationProject project;public Dictionary<string,object> sidecar;public Dictionary<string,string> files;public Dictionary<string,double[]> frames;public SW2URDF.RobotModel.RobotCoreSnapshot core;}
  sealed class State {public Dictionary<string,Entry> entries=new Dictionary<string,Entry>();}
  static readonly ConditionalWeakTable<ModelDoc2,State> states=new ConditionalWeakTable<ModelDoc2,State>();
  public static string Root => Path.Combine(Environment.GetEnvironmentVariable("SW2MUJOCO_CACHE")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SW2MuJoCo","cache"),"raw-mesh");
  static string FileHash(string file){using(var hash=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");}
  public static string Source(SldWorks app,ModelDoc2 model,string tree){
   var dependencies=new List<object>();var assembly=model as AssemblyDoc;
   if(assembly!=null)foreach(Component2 component in (object[])assembly.GetComponents(false)??new object[0]){
    // Short-lived COM metadata reads only, never shape or transform queries.
    var doc=component.GetModelDoc2() as ModelDoc2;
    if(doc==null)return null; // Unresolved/lightweight dependencies cannot establish validity.
    string path=doc.GetPathName();if(string.IsNullOrEmpty(path))return null;
    var file=new FileInfo(path);
    dependencies.Add(new{component=component.Name2,configuration=component.ReferencedConfiguration,path,stamp=doc.GetUpdateStamp(),size=file.Exists?file.Length:0,time=file.Exists?file.LastWriteTimeUtc.Ticks:0});
   }
   return ExportFingerprint.Hash(new{revision=CadRevision.Get(model),tree,dependencies=dependencies.OrderBy(x=>ExportFingerprint.Serializer().Serialize(x),StringComparer.Ordinal).ToArray(),generator=typeof(ProjectSourceCache).Assembly.GetName().Version.ToString()});
  }
  public static string Geometry(SimulationProject project){return ExportFingerprint.Hash(new{project.attachments,geometries=project.collision?.geometries});}
  public static Entry Find(ModelDoc2 model,string source,string geometry){
   if(source==null||Environment.GetEnvironmentVariable("SW2MUJOCO_USE_INCREMENTAL_EXPORT")=="0")return null;
   Entry entry;var state=states.GetValue(model,x=>new State());if(!state.entries.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out entry)||entry.source!=source||(entry.geometry!=geometry&&entry.frames==null))return null;
   try{if(entry.files.All(x=>File.Exists(Path.Combine(entry.folder,x.Key))&&FileHash(Path.Combine(entry.folder,x.Key))==x.Value))return entry;}catch(IOException){}catch(UnauthorizedAccessException){}return null;
  }
  public static void Store(ModelDoc2 model,string source,string geometry,string folder,string urdf,SimulationProject project,Dictionary<string,object> sidecar,Dictionary<string,double[]> frames=null,SW2URDF.RobotModel.RobotCoreSnapshot core=null){
   if(source==null||Environment.GetEnvironmentVariable("SW2MUJOCO_USE_INCREMENTAL_EXPORT")=="0")return;
   try{
    Directory.CreateDirectory(Root);string next=Path.Combine(Root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(next);
    foreach(string dir in Directory.GetDirectories(folder,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(next,dir.Substring(folder.Length+1)));
    foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))File.Copy(file,Path.Combine(next,file.Substring(folder.Length+1)));
    var entry=new Entry{source=source,geometry=geometry,folder=next,urdf=urdf.Substring(folder.Length+1),project=ExportFingerprint.Serializer().Deserialize<SimulationProject>(ExportFingerprint.Serializer().Serialize(project)),sidecar=sidecar,frames=frames,files=Directory.GetFiles(next,"*",SearchOption.AllDirectories).ToDictionary(x=>x.Substring(next.Length+1),FileHash)};
    entry.core=core?.RemapMeshPaths(path=>Path.Combine(next,path.Substring(folder.Length+1)));
    states.GetValue(model,x=>new State()).entries[model.ConfigurationManager.ActiveConfiguration.Name]=entry;
   }catch(IOException){}catch(UnauthorizedAccessException){} // Caching is optional.
  }
  public static Dictionary<string,object> Overlay(Entry entry,SimulationProject current){
   var serializer=ExportFingerprint.Serializer();var result=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(entry.sidecar));var fields=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(current));
   foreach(string key in new[]{"site_forces","actuators","sensors","equalities","solver","joint_defaults","joints","base_mode","joint_force_limits"})result[key]=fields[key];
   var collision=serializer.Deserialize<CollisionConfiguration>(serializer.Serialize(result["collision"]));
   collision.disable_internal=current.collision.disable_internal;collision.link_modes=current.collision.link_modes;collision.allowed_pairs=current.collision.allowed_pairs;result["collision"]=collision;
   return result;
  }
 }
}
