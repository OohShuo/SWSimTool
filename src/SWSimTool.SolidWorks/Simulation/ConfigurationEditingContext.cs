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

        public ConfigurationSession Session {get;}
        public SimulationProject Project {get;set;}
        public Link Tree {get;set;}

        ConfigurationEditingContext(SldWorks app,ModelDoc2 model){
            Session=ConfigurationSession.Capture(app,model);
            var entry=SimulationStorage.LoadEntry(model);
            Project=entry?.simulation??new SimulationProject();
            Tree=CopyTree(ConfigurationSerialization.ReadTree(entry?.urdf_xml,entry?.urdf_version??1.4)?.Snapshot());
        }
        internal static ConfigurationEditingContext Detached(SldWorks app,ModelDoc2 model)=>new ConfigurationEditingContext(app,model);
        public static ConfigurationEditingContext Get(SldWorks app,ModelDoc2 model){
            var holder=contexts.GetValue(model,x=>new Holder());
            if(holder.Current!=null){try{holder.Current.Session.RequireCurrent();return holder.Current;}catch(System.IO.InvalidDataException){holder.Current=null;}}
            return holder.Current=new ConfigurationEditingContext(app,model);
        }
        public static SimulationProject CopyProject(SimulationProject value){var s=ExportFingerprint.Serializer();return s.Deserialize<SimulationProject>(s.Serialize(value));}
        public static Link CopyTree(Link value){var tree=value?.Clone();Action<Link> clear=null;clear=l=>{l.SWMainComponent=null;l.SWComponents.Clear();foreach(var child in l.Children)clear(child);};if(tree!=null)clear(tree);return tree;}
        public void Commit(SimulationProject project,Link tree){Session.RequireCurrent();var p=CopyProject(project);var t=CopyTree(tree);Project=p;Tree=t;}
        public void AcceptSave(){Session.Refresh();}
        internal static string Fingerprint(ModelDoc2 model){
            Holder holder;if(!contexts.TryGetValue(model,out holder)||holder.Current==null)return null;
            try{holder.Current.Session.RequireCurrent();}
            catch(System.IO.InvalidDataException){return null;}
            return ExportFingerprint.Hash(new {project=holder.Current.Project,tree=ConfigurationSerialization.WriteBusinessTree(holder.Current.Tree)});
        }
        public static void Forget(ModelDoc2 model){contexts.Remove(model);}
        internal static void VerifiedRollback(ModelDoc2 model){
            Holder holder;
            if(contexts.TryGetValue(model,out holder)&&holder.Current!=null)holder.Current.Session.Refresh();
        }
    }
}
