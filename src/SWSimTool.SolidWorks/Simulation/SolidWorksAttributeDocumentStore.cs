using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.IO;
using System;
using System.Runtime.CompilerServices;
namespace SWSimTool.Simulation {
    internal sealed class SolidWorksAttributeDocumentStore:IConfigurationTransactionStore {
        readonly SldWorks app;
        readonly ModelDoc2 model;
        readonly SolidWorks.Interop.sldworks.Attribute known;
        string originalNodeName;bool observed;
        internal SolidWorksAttributeDocumentStore(SldWorks app,ModelDoc2 model,SolidWorks.Interop.sldworks.Attribute known=null){this.app=app;this.model=model;this.known=known;}
        internal static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model,string name) {
            Feature feature;return FindCandidate(model,out feature);
        }
        static SolidWorks.Interop.sldworks.Attribute FindCandidate(ModelDoc2 model,out Feature feature){
            SolidWorks.Interop.sldworks.Attribute result=null;feature=null;
            foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true)??new object[0])if(f.GetTypeName2()=="Attribute"){
                var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;if(a==null)continue;
                string name=a.GetName();string display=f.Name;
                if(name!=SimulationStorage.NodeName&&name!=SWSimTool.Persistence.DocumentStorageSchema.LegacyAttributeName&&display!=SimulationStorage.NodeName&&display!=SWSimTool.Persistence.DocumentStorageSchema.LegacyAttributeName)continue;
                if(result!=null)throw new InvalidDataException("发现多个插件配置节点："+(feature.Name??result.GetName())+" 与 "+(display??name)+"。停止读取和写入；请先明确处理冲突，不会自动覆盖。");
                result=a;feature=f;
            }return result;
        }
        internal static bool RequiresNameMigration(ModelDoc2 model){Feature f;var a=FindCandidate(model,out f);return a!=null&&(f?.Name??a.GetName())==SWSimTool.Persistence.DocumentStorageSchema.LegacyAttributeName;}
        internal static string NodeIdentity(ModelDoc2 model){
            Feature f;var a=FindCandidate(model,out f);if(a==null)return "absent";
            var bytes=model.Extension?.GetPersistReference3(f) as byte[];return bytes==null?"node:"+RuntimeHelpers.GetHashCode(a):Convert.ToBase64String(bytes);
        }
        public string ReadConfiguration() {
            Feature f;var found=FindCandidate(model,out f);var attribute=known??found;
            if(!observed){originalNodeName=f?.Name??attribute?.GetName();observed=true;}
            if(attribute==null)return null;var parameter=attribute.GetParameter("data") as Parameter;
            if(parameter==null)throw new InvalidDataException("插件配置节点 “"+(f?.Name??attribute.GetName())+"” 缺少 data 参数，停止写入。");return parameter.GetStringValue();
        }
        public void WriteConfiguration(string data) {
            Feature feature;var found=FindCandidate(model,out feature);var a=known??found;bool created=a==null;string previous=null;
            string previousName=feature?.Name??a?.GetName();bool migrate=!created&&previousName==SWSimTool.Persistence.DocumentStorageSchema.LegacyAttributeName;
            if(created){var def=(AttributeDef)app.DefineAttribute(SimulationStorage.NodeName);def.AddParameter("data",(int)swParamType_e.swParamTypeString,0,0);def.Register();a=def.CreateInstance5(model,null,SimulationStorage.NodeName,0,(int)swInConfigurationOpts_e.swAllConfiguration);if(a==null)throw new IOException("无法创建统一配置节点。");}
            else previous=((Parameter)a.GetParameter("data")).GetStringValue();
            if(migrate){SimulationStorage.Parse(previous);if(string.IsNullOrWhiteSpace(model.GetPathName()))throw new InvalidDataException("请先保存文档，再迁移旧配置节点。");
                SWSimTool.Persistence.ConfigurationBackup.Write(System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SWSimTool","config-backups"),model.GetPathName(),model.ConfigurationManager.ActiveConfiguration.Name,previous,typeof(SimulationStorage).Assembly.GetName().Version.ToString());}
            try {
                var parameter=(Parameter)a.GetParameter("data");if(!parameter.SetStringValue2(data,(int)swInConfigurationOpts_e.swAllConfiguration,""))throw new IOException("无法写入统一配置。");
                if(parameter.GetStringValue()!=data)throw new IOException("统一配置回读不一致。");
                if(migrate){feature.Name=SimulationStorage.NodeName;if(feature.Name!=SimulationStorage.NodeName)throw new IOException("配置节点更名失败。");}
            }catch(Exception error){
                try{if(created){if(!a.Delete(false))throw new IOException("新节点回滚删除失败。");}else{if(!((Parameter)a.GetParameter("data")).SetStringValue2(previous,(int)swInConfigurationOpts_e.swAllConfiguration,"")||((Parameter)a.GetParameter("data")).GetStringValue()!=previous)throw new IOException("配置数据回滚失败。");if(migrate){feature.Name=previousName;if(feature.Name!=previousName)throw new IOException("配置名称回滚失败。");}}}
                catch(Exception rollback){throw new AggregateException("配置写入与回滚均失败，请保留备份和当前草稿。",error,rollback);}throw;
            }
        }
        public void RestoreConfiguration(string original){
            if(original!=null){WriteConfiguration(original);Feature feature;FindCandidate(model,out feature);if(originalNodeName!=null&&feature!=null){feature.Name=originalNodeName;if(feature.Name!=originalNodeName)throw new IOException("无法恢复原节点名称。");}return;}
            var a=Find(model,SimulationStorage.NodeName);if(a!=null&&!a.Delete(false))throw new IOException("无法撤销新建的配置节点。");
        }
    }
}
