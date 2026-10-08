using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWSimTool.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Web.Script.Serialization;
namespace SWSimTool.Simulation {
 public static class SimulationStorage {
  public const string NodeName=SWSimTool.Persistence.DocumentStorageSchema.AttributeName;
  public sealed class Entry {public string instance_id{get;set;}=Guid.NewGuid().ToString("N"); public long generation{get;set;} public string configuration_id{get;set;} public string configuration_name{get;set;} public string urdf_xml{get;set;} public double urdf_version{get;set;}=1.4; public SimulationProject simulation{get;set;}}
  public sealed class Document {public int version{get;set;}=2;public Dictionary<string,Entry> configurations{get;set;}=new Dictionary<string,Entry>();}
  sealed class CachedDocument {public string data;public string normalized;}
  static readonly ConditionalWeakTable<ModelDoc2,CachedDocument> cache=new ConditionalWeakTable<ModelDoc2,CachedDocument>();
  public static int ParseCount {get;private set;}
  public static void Invalidate(ModelDoc2 model){cache.Remove(model);ProjectSourceCache.Clear(model);CadSnapshotCache.Clear(model);}
  public static void ValidateEnvelope(string data){ValidateDocument(Serializer().Deserialize<Document>(data));}
  static JavaScriptSerializer Serializer()=>SWSimTool.Persistence.DocumentEnvelopeSerializer.Serializer();
  static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model,string name)=>SolidWorksAttributeDocumentStore.Find(model,name);
  public static Document Parse(string data){using(var timing=new SWSimTool.Utilities.PerformanceScope("storage.parse_validate")){
   ParseCount++;var d=SWSimTool.Persistence.DocumentEnvelopeSerializer.Deserialize<Document>(data,SWSimTool.Persistence.DocumentStorageSchema.SupportedVersion);
   ValidateDocument(d);
   // Materialize legacy identities once in the loaded envelope. Independent tree
   // readers must not generate unrelated IDs. No CAD write/dirty flag occurs here.
   foreach(var entry in d.configurations.Values){
    var tree=ConfigurationSerialization.ReadTree(entry.urdf_xml,entry.urdf_version);
    if(tree==null)continue;
    StableReferences.Normalize(entry.simulation,tree);
    entry.urdf_xml=ConfigurationSerialization.WriteTree(tree);
   }
   return d;}
  }
  static void ValidateDocument(Document d){
   if(d==null||d.version!=2||d.configurations==null||d.configurations.Values.Any(e=>e==null))throw new InvalidDataException("SWSimTool 配置损坏或版本不支持。");
   foreach(var e in d.configurations.Values){ConfigurationSerialization.ValidateTreeData(e.urdf_xml,e.urdf_version);ValidateProject(e.simulation);}
  }
  public static void RemapConfigurations(Document document,IDictionary<string,string> namesById){
   var mapped=new Dictionary<string,Entry>();var seen=new HashSet<string>();foreach(var pair in document.configurations){var entry=pair.Value;
    if(string.IsNullOrWhiteSpace(entry.configuration_id)){var match=namesById.Where(x=>x.Value==pair.Key).ToArray();if(match.Length>1)throw new InvalidDataException("Ambiguous configuration identity");entry.configuration_id=match.Length==1?match[0].Key:Guid.NewGuid().ToString("N");}
    if(!seen.Add(entry.configuration_id))throw new InvalidDataException("Duplicate configuration identity");string name;bool live=namesById.TryGetValue(entry.configuration_id,out name);entry.configuration_name=live?name:entry.configuration_name??pair.Key;
    mapped.Add(live?name:"__unresolved_configuration_"+entry.configuration_id,entry);if(live&&entry.simulation!=null)entry.simulation.configuration=name;
   }document.configurations=mapped;
  }
  static string ConfigurationId(Configuration configuration)=>"swcfg:"+configuration.GetID().ToString(System.Globalization.CultureInfo.InvariantCulture);
  static Document Identify(Document document,ModelDoc2 model){var names=new Dictionary<string,string>();foreach(string name in (string[])model.GetConfigurationNames()??new[]{model.ConfigurationManager.ActiveConfiguration.Name}){var configuration=model.GetConfigurationByName(name) as Configuration;if(configuration==null&&name==model.ConfigurationManager.ActiveConfiguration.Name)configuration=model.ConfigurationManager.ActiveConfiguration;if(configuration!=null)names.Add(ConfigurationId(configuration),name);}RemapConfigurations(document,names);return document;}
  static Document Read(ModelDoc2 model,SolidWorks.Interop.sldworks.Attribute known=null){
   var a=known??Find(model,NodeName);if(a!=null){var data=new SolidWorksAttributeDocumentStore(null,model,a).ReadConfiguration();var saved=cache.GetValue(model,key=>new CachedDocument());if(saved.data==data&&saved.normalized!=null)return Identify(Serializer().Deserialize<Document>(saved.normalized),model);var parsed=Parse(data);saved.normalized=Serializer().Serialize(parsed);saved.data=data;return Identify(parsed,model);}
   cache.Remove(model);ProjectSourceCache.Clear(model);CadSnapshotCache.Clear(model);
   return Identify(new Document(),model);
  }
  static void ValidateProject(SimulationProject p){if(p!=null&&(p.schema_version!=1||p.attachments==null||p.sensors==null||p.actuators==null||p.equalities==null))throw new InvalidDataException("仿真配置损坏或版本不支持。");}
  public static Entry LoadEntry(ModelDoc2 model){
   var attribute=Find(model,NodeName);if(attribute==null){cache.Remove(model);ProjectSourceCache.Clear(model);CadSnapshotCache.Clear(model);return null;}
   Entry e;return Read(model,attribute).configurations.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out e)?e:new Entry();
  }
  public static SimulationProject Load(ModelDoc2 model){Entry e;if(!Read(model).configurations.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out e))return null;StableReferences.Normalize(e.simulation,ConfigurationSerialization.ReadTree(e.urdf_xml,e.urdf_version));return e.simulation;}
  public static void SaveTree(SldWorks app,ModelDoc2 model,string xml,double version,SimulationProject completeDraft=null){var d=Read(model);var e=Current(d,model);if(completeDraft!=null)e.simulation=completeDraft;var previous=ConfigurationSerialization.ReadTree(e.urdf_xml,e.urdf_version);var current=ConfigurationSerialization.ReadTree(xml,version);StableReferences.ValidateIdentities(current);StableReferences.Normalize(e.simulation,completeDraft==null?(previous??current):current);StableReferences.RemapLinkModesByStableId(e.simulation);e.generation++;e.urdf_version=version;CadTreeReferences.Normalize(model,current);StableReferences.Normalize(e.simulation,current);e.urdf_xml=ConfigurationSerialization.WriteTree(current);Write(app,model,d);SimulationSession.Mark(model,SimulationDirtyFlags.Source|SimulationDirtyFlags.Mjcf);}
  public static void Save(SldWorks app,ModelDoc2 model,SimulationProject project){var d=Read(model);var entry=Current(d,model);var before=entry.simulation;var tree=ConfigurationSerialization.ReadTree(entry.urdf_xml,entry.urdf_version);CadTreeReferences.Normalize(model,tree);StableReferences.Normalize(project,tree);if(tree!=null){entry.urdf_xml=ConfigurationSerialization.WriteTree(tree);entry.urdf_version=1.4;}entry.simulation=project;entry.generation++;Write(app,model,d);SimulationSession.Applied(model,before,project);}
  static Entry Current(Document d,ModelDoc2 model){string key=model.ConfigurationManager.ActiveConfiguration.Name;Entry e;if(!d.configurations.TryGetValue(key,out e))d.configurations[key]=e=new Entry{configuration_id=ConfigurationId(model.ConfigurationManager.ActiveConfiguration),configuration_name=key};return e;}
  static void Write(SldWorks app,ModelDoc2 model,Document d){using(var timing=new SWSimTool.Utilities.PerformanceScope("storage.save")){
   int cadStamp=CadRevision.BeforeConfigurationWrite(model);
   ValidateDocument(d);string data=Serializer().Serialize(d);
   new SolidWorksAttributeDocumentStore(app,model).WriteConfiguration(data);
   ConfigurationSession.RecordSaved(model);
   var saved=cache.GetValue(model,key=>new CachedDocument());saved.normalized=data;saved.data=data;model.SetSaveFlag();
   CadRevision.AfterConfigurationWrite(model,cadStamp);
  }}
 }
}
