using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.URDFExport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace SW2URDF.Simulation {
 public static class SimulationStorage {
  public const string NodeName="SW2MuJoCo Configuration",LegacyNodeName="MuJoCo Simulation Configuration (v1)";
  public sealed class Entry {public string urdf_xml{get;set;} public double urdf_version{get;set;}=1.4; public SimulationProject simulation{get;set;}}
  public sealed class Document {public int version{get;set;}=2;public Dictionary<string,Entry> configurations{get;set;}=new Dictionary<string,Entry>();}
  sealed class LegacyDocument {public int version{get;set;} public Dictionary<string,SimulationProject> configurations{get;set;}}
  static JavaScriptSerializer Serializer()=>new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
  static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model,string name){
   foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true)??new object[0])if(f.GetTypeName2()=="Attribute"){
    var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;if(a!=null&&a.GetName()==name)return a;
   }return null;
  }
  public static Document Parse(string data){
   var d=Serializer().Deserialize<Document>(data);
   if(d==null||d.version!=2||d.configurations==null||d.configurations.Values.Any(e=>e==null))throw new InvalidDataException("SW2MuJoCo 配置损坏或版本不支持。");
   foreach(var e in d.configurations.Values){ConfigurationSerialization.ValidateTreeData(e.urdf_xml,e.urdf_version);ValidateProject(e.simulation);}return d;
  }
  static Document Read(ModelDoc2 model){
   var a=Find(model,NodeName);if(a!=null)return Parse(((Parameter)a.GetParameter("data")).GetStringValue());
   var d=new Document();double version;string xml=ConfigurationSerialization.GetLegacyConfigTreeData(model,out version);ConfigurationSerialization.ValidateTreeData(xml,version);
   // Old URDF data applied to every configuration. Preserve it without activating configurations.
   foreach(string key in (string[])model.GetConfigurationNames()??new[]{model.ConfigurationManager.ActiveConfiguration.Name})d.configurations[key]=new Entry{urdf_xml=xml,urdf_version=version};
   var legacy=Find(model,LegacyNodeName);
   if(legacy!=null){
    var old=Serializer().Deserialize<LegacyDocument>(((Parameter)legacy.GetParameter("data")).GetStringValue());
    if(old==null||old.version!=1||old.configurations==null)throw new InvalidDataException("旧仿真配置损坏，不能迁移。");
    foreach(var p in old.configurations){ValidateProject(p.Value);Entry e;if(!d.configurations.TryGetValue(p.Key,out e))d.configurations[p.Key]=e=new Entry{urdf_xml=xml,urdf_version=version};e.simulation=p.Value;}
   }return d;
  }
  static void ValidateProject(SimulationProject p){if(p!=null&&(p.schema_version!=1||p.attachments==null||p.sensors==null||p.actuators==null||p.equalities==null))throw new InvalidDataException("仿真配置损坏或版本不支持。");}
  public static Entry LoadEntry(ModelDoc2 model){
   if(Find(model,NodeName)==null)return null;
   Entry e;return Read(model).configurations.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out e)?e:new Entry();
  }
  public static SimulationProject Load(ModelDoc2 model){Entry e;return Read(model).configurations.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out e)?e.simulation:null;}
  public static void SaveTree(SldWorks app,ModelDoc2 model,string xml,double version){var d=Read(model);var e=Current(d,model);e.urdf_xml=xml;e.urdf_version=version;Write(app,model,d);}
  public static void Save(SldWorks app,ModelDoc2 model,SimulationProject project){var d=Read(model);Current(d,model).simulation=project;Write(app,model,d);}
  static Entry Current(Document d,ModelDoc2 model){string key=model.ConfigurationManager.ActiveConfiguration.Name;Entry e;if(!d.configurations.TryGetValue(key,out e))d.configurations[key]=e=new Entry();return e;}
  static void Write(SldWorks app,ModelDoc2 model,Document d){
   string data=Serializer().Serialize(d);Parse(data);
   var a=Find(model,NodeName);bool created=a==null;string previous=null;
   if(created){var def=(AttributeDef)app.DefineAttribute(NodeName);def.AddParameter("data",(int)swParamType_e.swParamTypeString,0,0);def.Register();a=def.CreateInstance5(model,null,NodeName,0,(int)swInConfigurationOpts_e.swAllConfiguration);if(a==null)throw new IOException("无法创建统一配置节点。");}
   else previous=((Parameter)a.GetParameter("data")).GetStringValue();
   try{
    var parameter=(Parameter)a.GetParameter("data");if(!parameter.SetStringValue2(data,(int)swInConfigurationOpts_e.swAllConfiguration,""))throw new IOException("无法写入统一配置。");
    string verified=parameter.GetStringValue();Parse(verified);if(verified!=data)throw new IOException("统一配置回读不一致。");
   }catch{if(created)a.Delete(false);else ((Parameter)a.GetParameter("data")).SetStringValue2(previous,(int)swInConfigurationOpts_e.swAllConfiguration,"");throw;}
   model.SetSaveFlag();
   // Only recognized legacy attributes are removed, after verifying all migrated configurations.
   foreach(string name in new[]{LegacyNodeName,ConfigurationSerialization.UrdfConfigurationSwAttributeName}.Concat(ConfigurationSerialization.PREVIOUS_URDF_CONFIGURATION_NAMES)){
    var old=Find(model,name);if(old!=null&&!old.Delete(false))throw new IOException("新配置已保存，但旧节点未移除："+name);
   }
  }
 }
}
