using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWSimTool.Persistence;
using System;
using System.IO;

namespace SWSimTool.Simulation {
 internal sealed class SolidWorksAttributeDocumentStore:IConfigurationTransactionStore {
  readonly SldWorks app;readonly ModelDoc2 model;readonly SolidWorks.Interop.sldworks.Attribute known;readonly string backupFolder;
  string originalName;
  internal SolidWorksAttributeDocumentStore(SldWorks app,ModelDoc2 model,SolidWorks.Interop.sldworks.Attribute known=null,string backupFolder=null){this.app=app;this.model=model;this.known=known;this.backupFolder=backupFolder;}
  static bool Recognized(string name)=>name==DocumentStorageSchema.AttributeName||name==DocumentStorageSchema.LegacyAttributeName;
  internal static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model,string name){
   SolidWorks.Interop.sldworks.Attribute result=null;
   foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true)??new object[0]){
    if(f.GetTypeName2()!="Attribute")continue;var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
    if(a==null||!(Recognized(name)?Recognized(a.GetName()):a.GetName()==name))continue;
    if(result!=null)throw new InvalidDataException("发现多个 SWSimTool/SW2MuJoCo 配置节点。无法确定归属，停止写入；不会覆盖或合并。");result=a;
   }return result;
  }
  internal static string NodeIdentity(ModelDoc2 model){
   var a=Find(model,SimulationStorage.NodeName);if(a==null)return "absent";
   foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true)??new object[0])if(f.GetTypeName2()=="Attribute"&&(f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute)?.GetName()==a.GetName()){
    var bytes=model.Extension?.GetPersistReference3(f) as byte[];if(bytes!=null&&bytes.Length>0)return Convert.ToBase64String(bytes);
    throw new InvalidDataException("无法取得插件配置节点的 CAD 持久引用。已停止编辑/导出，请重新打开工程后重试；不会使用临时 COM 身份。");
   }throw new InvalidDataException("插件配置节点在读取期间发生变化，请重新进入配置。");
  }
  static string Read(SolidWorks.Interop.sldworks.Attribute a){if(a==null)return null;var p=a.GetParameter("data") as Parameter;if(p==null)throw new InvalidDataException("插件配置节点缺少 data 参数。");return p.GetStringValue();}
  static void Set(SolidWorks.Interop.sldworks.Attribute a,string data){var p=a.GetParameter("data") as Parameter;if(p==null||!p.SetStringValue2(data,(int)swInConfigurationOpts_e.swAllConfiguration,"")||Read(a)!=data)throw new IOException("配置写入或回读验证失败。");}
  SolidWorks.Interop.sldworks.Attribute Create(string name,string data){
   if(app==null)throw new InvalidOperationException("创建插件节点需要 SolidWorks 宿主。");var def=(AttributeDef)app.DefineAttribute(name);def.AddParameter("data",(int)swParamType_e.swParamTypeString,0,0);
   if(!def.Register())throw new IOException("无法注册配置节点类型。");var a=def.CreateInstance5(model,null,name,0,(int)swInConfigurationOpts_e.swAllConfiguration);if(a==null)throw new IOException("无法创建配置节点。");
   try{Set(a,data);return a;}catch{if(!a.Delete(false))throw new IOException("写入失败，且无法删除未完成节点。");throw;}
  }
  public string ReadConfiguration()=>Read(known??Find(model,SimulationStorage.NodeName));
  public void WriteConfiguration(string data){
   var old=known??Find(model,SimulationStorage.NodeName);originalName=old?.GetName();string previous=Read(old);
   if(old==null){Create(DocumentStorageSchema.AttributeName,data);return;}
   if(originalName==DocumentStorageSchema.AttributeName){try{Set(old,data);}catch(Exception e){try{Set(old,previous);}catch(Exception r){throw new AggregateException("写入与恢复均失败。",e,r);}throw;}return;}
   // Ordinary save migration must also have a verified raw-data backup.
   string folder=backupFolder??Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SWSimTool","config-backups");
   if(string.IsNullOrWhiteSpace(model.GetPathName()))throw new InvalidDataException("迁移前请先保存装配。");
   ConfigurationBackup.Write(folder,model.GetPathName(),model.ConfigurationManager.ActiveConfiguration.Name,previous,typeof(SolidWorksAttributeDocumentStore).Assembly.GetName().Version.ToString(),originalName);
   // Verify the complete candidate before deleting the old node.
   var candidate=Create(DocumentStorageSchema.AttributeName,data);
   try{if(Read(candidate)!=data)throw new IOException("迁移回读失败。");if(!old.Delete(false))throw new IOException("旧节点删除失败。");if(!ReferenceEquals(Find(model,SimulationStorage.NodeName),candidate)||ReadConfiguration()!=data)throw new IOException("迁移后验证失败。");}
   catch(Exception e){try{if(!candidate.Delete(false))throw new IOException("无法删除迁移候选节点。");var restored=Find(model,SimulationStorage.NodeName);if(restored==null)restored=Create(originalName,previous);if(restored.GetName()!=originalName||Read(restored)!=previous)throw new IOException("旧节点恢复验证失败。");ConfigurationEditingContext.VerifiedRollback(model);}catch(Exception r){throw new AggregateException("迁移与回滚均失败；保留草稿和备份。",e,r);}throw;}
  }
  public void RestoreConfiguration(string original){
   var a=Find(model,SimulationStorage.NodeName);
   if(original==null){if(a!=null&&!a.Delete(false))throw new IOException("无法撤销新节点。");}
   else if(a==null)Create(originalName??DocumentStorageSchema.AttributeName,original);
   else if(originalName!=null&&a.GetName()!=originalName){if(!a.Delete(false))throw new IOException("无法撤销迁移节点。");Create(originalName,original);}
   else Set(a,original);
   var restored=Find(model,SimulationStorage.NodeName);if(Read(restored)!=original||(original!=null&&originalName!=null&&restored.GetName()!=originalName))throw new IOException("配置恢复回读失败。");
   ConfigurationEditingContext.VerifiedRollback(model);
  }
 }
}
