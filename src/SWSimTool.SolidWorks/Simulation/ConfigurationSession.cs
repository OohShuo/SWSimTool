using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace SWSimTool.Simulation {
    // A lease belongs to a document open, SW configuration and exact persisted revision.
    public sealed class ConfigurationSession {
        sealed class State { public string observation;public string identity;public long generation;public bool closed;public string id=Guid.NewGuid().ToString("N"); }
        static readonly ConditionalWeakTable<ModelDoc2,State> states=new ConditionalWeakTable<ModelDoc2,State>();
        readonly ModelDoc2 model;readonly SldWorks app;readonly State state;
        readonly string configuration;long generation;
        public string DocumentIdentity=>state.id;
        public long Generation=>generation;
        public string Instance {get;private set;}
        ConfigurationSession(SldWorks app,ModelDoc2 model){this.app=app;this.model=model;state=states.GetValue(model,x=>new State());configuration=model.ConfigurationManager.ActiveConfiguration.Name;Refresh();}
        public static ConfigurationSession Capture(SldWorks app,ModelDoc2 model)=>new ConfigurationSession(app,model);
        static string Observe(ModelDoc2 model){
            string node=SolidWorksAttributeDocumentStore.NodeIdentity(model);
            return model.ConfigurationManager.ActiveConfiguration.Name+"|"+node+"|"+new SolidWorksAttributeDocumentStore(null,model).ReadConfiguration();
        }
        void CheckObservation(){
            var next=Observe(model);
            string identity=model.ConfigurationManager.ActiveConfiguration.Name+"|"+SolidWorksAttributeDocumentStore.NodeIdentity(model);
            if(state.identity!=null&&state.identity!=identity){ProjectSourceCache.Clear(model);CadSnapshotCache.Clear(model);}
            state.identity=identity;
            if(state.observation!=next){state.observation=next;state.generation++;}
        }
        // Compare COM identity, not the managed wrapper or a reusable document path/title.
        internal static bool SameDocument(object left,object right){
            if(ReferenceEquals(left,right))return true;
            if(left==null||right==null||!Marshal.IsComObject(left)||!Marshal.IsComObject(right))return false;
            IntPtr a=IntPtr.Zero,b=IntPtr.Zero;
            try{a=Marshal.GetIUnknownForObject(left);b=Marshal.GetIUnknownForObject(right);return a==b;}
            finally{if(b!=IntPtr.Zero)Marshal.Release(b);if(a!=IntPtr.Zero)Marshal.Release(a);}
        }
        public void RequireCurrent(){
            if(state.closed)throw new InvalidDataException("文档已关闭，此配置页面或导出任务已失效。");
            if(app!=null&&!SameDocument(app.ActiveDoc,model))throw new InvalidDataException("活动文档已切换，请重新进入配置。");
            CheckObservation();
            if(configuration!=model.ConfigurationManager.ActiveConfiguration.Name||generation!=state.generation)
                throw new InvalidDataException("配置节点已删除、重建、切换或被其他页面修改。旧草稿/任务已失效，请重新进入；不会向新配置写回旧引用。");
        }
        public void Refresh(){
            if(state.closed||configuration!=model.ConfigurationManager.ActiveConfiguration.Name)throw new InvalidDataException("文档已关闭或 SolidWorks 配置已切换。");CheckObservation();generation=state.generation;
            string raw=new SolidWorksAttributeDocumentStore(null,model).ReadConfiguration();
            Instance=null;
            if(!string.IsNullOrWhiteSpace(raw)){
                var envelope=SWSimTool.Persistence.DocumentEnvelopeSerializer.Serializer().Deserialize<SimulationStorage.Document>(raw);
                SimulationStorage.Entry entry;
                if(envelope?.configurations!=null&&envelope.configurations.TryGetValue(configuration,out entry))Instance=entry.instance_id;
            }
        }
        public static void RecordSaved(ModelDoc2 model){
            var s=states.GetValue(model,x=>new State());
            string identity=model.ConfigurationManager.ActiveConfiguration.Name+"|"+SolidWorksAttributeDocumentStore.NodeIdentity(model);
            if(s.identity!=null&&s.identity!=identity){ProjectSourceCache.Clear(model);CadSnapshotCache.Clear(model);}
            s.identity=identity;string observation=Observe(model);
            if(s.observation!=observation){s.observation=observation;s.generation++;}
        }
        public static void Invalidate(ModelDoc2 model,bool closed=false){var s=states.GetValue(model,x=>new State());s.generation++;s.closed=closed;ProjectSourceCache.Clear(model);CadSnapshotCache.Clear(model);}
    }
}
