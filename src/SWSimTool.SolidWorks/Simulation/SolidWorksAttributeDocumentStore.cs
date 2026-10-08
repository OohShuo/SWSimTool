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
        internal SolidWorksAttributeDocumentStore(SldWorks app,ModelDoc2 model,SolidWorks.Interop.sldworks.Attribute known=null){this.app=app;this.model=model;this.known=known;}
        internal static SolidWorks.Interop.sldworks.Attribute Find(ModelDoc2 model,string name) {
            foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true)??new object[0])if(f.GetTypeName2()=="Attribute") {
                var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;if(a!=null&&a.GetName()==name)return a;
            }
            return null;
        }
        internal static string NodeIdentity(ModelDoc2 model){
            foreach(Feature f in (object[])model.FeatureManager.GetFeatures(true)??new object[0])if(f.GetTypeName2()=="Attribute"){
                var a=f.GetSpecificFeature2() as SolidWorks.Interop.sldworks.Attribute;
                if(a!=null&&a.GetName()==SimulationStorage.NodeName){var bytes=model.Extension?.GetPersistReference3(f) as byte[];return bytes==null?"node:"+RuntimeHelpers.GetHashCode(a):Convert.ToBase64String(bytes);}
            }return "absent";
        }
        public string ReadConfiguration() {
            var attribute=known??Find(model,SimulationStorage.NodeName);
            return attribute==null?null:((Parameter)attribute.GetParameter("data")).GetStringValue();
        }
        public void WriteConfiguration(string data) {
            var a=known??Find(model,SimulationStorage.NodeName);bool created=a==null;string previous=null;
            if(created){var def=(AttributeDef)app.DefineAttribute(SimulationStorage.NodeName);def.AddParameter("data",(int)swParamType_e.swParamTypeString,0,0);def.Register();a=def.CreateInstance5(model,null,SimulationStorage.NodeName,0,(int)swInConfigurationOpts_e.swAllConfiguration);if(a==null)throw new IOException("无法创建统一配置节点。");}
            else previous=((Parameter)a.GetParameter("data")).GetStringValue();
            try {
                var parameter=(Parameter)a.GetParameter("data");if(!parameter.SetStringValue2(data,(int)swInConfigurationOpts_e.swAllConfiguration,""))throw new IOException("无法写入统一配置。");
                if(parameter.GetStringValue()!=data)throw new IOException("统一配置回读不一致。");
            }catch{if(created)a.Delete(false);else ((Parameter)a.GetParameter("data")).SetStringValue2(previous,(int)swInConfigurationOpts_e.swAllConfiguration,"");throw;}
        }
        public void RestoreConfiguration(string original){
            if(original!=null){WriteConfiguration(original);return;}
            var a=Find(model,SimulationStorage.NodeName);if(a!=null&&!a.Delete(false))throw new IOException("无法撤销新建的配置节点。");
        }
    }
}
