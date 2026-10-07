using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Security.Cryptography;
namespace SWSimTool.Simulation {
  public class ProjectCacheEntry {public bool nativeSource;public string source,geometry,folder,urdf;public SimulationProject project;public Dictionary<string,object> sidecar;public Dictionary<string,string> files;public Dictionary<string,double[]> frames;public SWSimTool.RobotModel.RobotCoreSnapshot core;public ResolvedSimulationGeometry resolvedGeometry;}
 public sealed class SourceFileMetadata {public bool Exists;public long Length;public DateTime LastWriteTimeUtc;}
 public static class SourceCacheRepository {
  public static string Root => Path.Combine(Environment.GetEnvironmentVariable("SWSIMTOOL_CACHE")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SWSimTool","cache"),"raw-mesh");
  public static string FileHash(string file){using(var hash=SHA256.Create())using(var stream=File.OpenRead(file))return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","");}

 public static SourceFileMetadata Metadata(string path){var file=new FileInfo(path);return new SourceFileMetadata{Exists=file.Exists,Length=file.Exists?file.Length:0,LastWriteTimeUtc=file.Exists?file.LastWriteTimeUtc:default(DateTime)};}
 public static bool Validate(ProjectCacheEntry entry){try{return entry.files.All(x=>File.Exists(Path.Combine(entry.folder,x.Key))&&FileHash(Path.Combine(entry.folder,x.Key))==x.Value);}catch(IOException){}catch(UnauthorizedAccessException){}return false;}
 public static void Store(ProjectCacheEntry entry,string folder,string urdf){
  Directory.CreateDirectory(Root);string next=Path.Combine(Root,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(next);
  foreach(string dir in Directory.GetDirectories(folder,"*",SearchOption.AllDirectories))Directory.CreateDirectory(Path.Combine(next,dir.Substring(folder.Length+1)));
  foreach(string file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))File.Copy(file,Path.Combine(next,file.Substring(folder.Length+1)));
  entry.folder=next;entry.urdf=urdf==null?null:urdf.Substring(folder.Length+1);
  entry.project=ExportFingerprint.Serializer().Deserialize<SimulationProject>(ExportFingerprint.Serializer().Serialize(entry.project));
  entry.files=Directory.GetFiles(next,"*",SearchOption.AllDirectories).ToDictionary(x=>x.Substring(next.Length+1),FileHash);
  entry.core=entry.core?.RemapMeshPaths(path=>Path.Combine(next,path.Substring(folder.Length+1)));
 }
  public static Dictionary<string,object> Overlay(ProjectCacheEntry entry,SimulationProject current){
   var serializer=ExportFingerprint.Serializer();var result=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(entry.sidecar));var fields=serializer.Deserialize<Dictionary<string,object>>(serializer.Serialize(current));
   foreach(string key in new[]{"site_forces","actuators","sensors","equalities","solver","joint_defaults","joints","base_mode","joint_force_limits"})result[key]=fields[key];
   var collision=serializer.Deserialize<CollisionConfiguration>(serializer.Serialize(result["collision"]));
   var identities=entry.core?.Links.ToDictionary(x=>x.Id,x=>x.Name)??new Dictionary<string,string>();
   if(identities.Count==0&&result.ContainsKey("identities")){var identity=(Dictionary<string,object>)result["identities"];foreach(var p in (Dictionary<string,object>)identity["links"])identities.Add(Convert.ToString(p.Value),p.Key);}
   DomainStableReferences.MigrateLegacyLinkModesByName(current,identities);var projected=current.collision.LegacyExport(identities);
   collision.disable_internal=current.collision.disable_internal;collision.link_modes=projected.link_modes;collision.link_modes_by_id=projected.link_modes_by_id;collision.link_modes_migrated=true;collision.allowed_pairs=current.collision.allowed_pairs;result["collision"]=collision;
   return result;
  }
 }}
