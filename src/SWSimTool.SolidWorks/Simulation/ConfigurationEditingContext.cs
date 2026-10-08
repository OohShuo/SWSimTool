using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using SolidWorks.Interop.sldworks;
using SWSimTool.URDF;
using SWSimTool.URDFExport;

namespace SWSimTool.Simulation {
    // One complete unsaved draft per active document/configuration. Old pages keep
    // their old context and cannot acquire the lease of a replacement node.
    public sealed class ConfigurationEditingContext {
        sealed class Holder { public ConfigurationEditingContext Current; }
        static readonly ConditionalWeakTable<ModelDoc2,Holder> contexts=new ConditionalWeakTable<ModelDoc2,Holder>();
        readonly List<Action> flushers=new List<Action>();
        public ConfigurationSession Session {get;}
        public SimulationProject Project {get;set;}
        public LinkNode Tree {get;set;}
        public Action ReopenPage {get;set;}
        ConfigurationEditingContext(SldWorks app,ModelDoc2 model){
            Session=ConfigurationSession.Capture(app,model);
            var entry=SimulationStorage.LoadEntry(model);
            Project=entry?.simulation??new SimulationProject();
            Tree=ConfigurationSerialization.ReadTree(entry?.urdf_xml,entry?.urdf_version??1.4);
        }
        internal static ConfigurationEditingContext Detached(SldWorks app,ModelDoc2 model)=>new ConfigurationEditingContext(app,model);
        public static ConfigurationEditingContext Get(SldWorks app,ModelDoc2 model){
            var holder=contexts.GetValue(model,x=>new Holder());
            if(holder.Current!=null){try{holder.Current.Session.RequireCurrent();return holder.Current;}catch(System.IO.InvalidDataException){holder.Current=null;}}
            return holder.Current=new ConfigurationEditingContext(app,model);
        }
        public void Register(Action flush){flushers.Add(flush);}
        public void Unregister(Action flush){flushers.Remove(flush);}
        public void Flush(){Session.RequireCurrent();foreach(var flush in flushers.ToArray())flush();Session.RequireCurrent();}
        public void AcceptSave(){Session.Refresh();}
        internal static string Fingerprint(ModelDoc2 model){
            Holder holder;if(!contexts.TryGetValue(model,out holder)||holder.Current==null)return null;
            try{holder.Current.Session.RequireCurrent();}
            catch(System.IO.InvalidDataException){return null;}
            return ExportFingerprint.Hash(new {project=holder.Current.Project,tree=ConfigurationSerialization.WriteTree(holder.Current.Tree)});
        }
        public static void Forget(ModelDoc2 model){contexts.Remove(model);}
        internal static void VerifiedRollback(ModelDoc2 model){
            Holder holder;
            if(contexts.TryGetValue(model,out holder)&&holder.Current!=null)holder.Current.Session.Refresh();
        }
    }
}
