using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using SWSimTool.URDF;
using SWSimTool.URDFExport;
using SWSimTool.Persistence;

namespace SWSimTool.Simulation {
    public sealed partial class AttachmentService {
        public string RebuildCurrentDraft(string backupFolder=null){
            RequireCurrentDocument();var active=SWSimTool.UI.ConfigurationPageDraft.Active(Model);active?.Collect();
            var serializer=ExportFingerprint.Serializer();
            var draft=serializer.Deserialize<SimulationProject>(serializer.Serialize(active==null?Project:active.Project));
            var tree=ConfigurationSerialization.ReadTree(ConfigurationSerialization.WriteBusinessTree(active==null?DraftTree:active.Tree),1.4);
            if(tree==null)throw new InvalidDataException("当前草稿没有 URDF 树，请先配置。");
            StableReferences.ValidateTree(tree);StableReferences.Normalize(draft,tree);
            SimulationConfigBuilder.ValidateReferences(draft,StableReferences.LinkNames(tree),JointDescriptor.FromTree(tree).ToDictionary(x=>x.id,x=>x.name));
            // Rebuild never validates old target semantic references, nor uses an
            // old parsed CAD model. It resolves only the independent draft.
            CadSnapshotCache.Clear(Model);ProjectSourceCache.Clear(Model);InvalidateCADCache();
            CadTreeReferences.Normalize(Model,tree,true);
            var missing=new List<string>();CommonSwOperations.LoadSWComponents(Model,tree,missing);
            if(missing.Count!=0)throw new InvalidDataException("组件引用失效："+string.Join(", ",missing));
            var calculation=new AttachmentService(exporter,true);calculation.Project=draft;calculation.SetCollisionTree(tree);
            try{
                var core=CadRobotCoreBuilder.Build(Path.GetFileNameWithoutExtension(Model.GetPathName()),calculation,tree,new Dictionary<string,SWSimTool.RobotModel.MeshSource>());
                var geometry=calculation.ResolveNativeGeometry(core);
                SimulationConfigBuilder.Build(draft,core,geometry);
            }finally{calculation.InvalidateCADCache(false);}
            draft.assembly=Model.GetPathName();draft.configuration=Model.ConfigurationManager.ActiveConfiguration.Name;
            if(string.IsNullOrWhiteSpace(draft.assembly))throw new InvalidDataException("请先保存装配，以便建立可恢复备份。");
            var entry=new SimulationStorage.Entry{configuration_id="swcfg:"+Model.ConfigurationManager.ActiveConfiguration.GetID().ToString(System.Globalization.CultureInfo.InvariantCulture),configuration_name=draft.configuration,urdf_xml=ConfigurationSerialization.WriteTree(tree),simulation=draft};
            var store=new SolidWorksAttributeDocumentStore(App,Model);
            string original=store.ReadConfiguration();
            string candidate=ConfigurationEnvelopeReplacement.ReplaceEntry(original,draft.configuration,entry.configuration_id,serializer.Serialize(entry));
            string folder=backupFolder??Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),"SWSimTool","config-backups");
            string backup=ConfigurationReplacement.Replace(store,candidate,session.RequireCurrent,
                SimulationStorage.ValidateEnvelope,
                data=>ConfigurationBackup.Write(folder,draft.assembly,draft.configuration,data,typeof(AttachmentService).Assembly.GetName().Version.ToString(),SolidWorksAttributeDocumentStore.Find(Model,SimulationStorage.NodeName)?.GetName()));
            var close=active?.Close;active?.Dispose();SimulationStorage.Invalidate(Model);ConfigurationSession.Invalidate(Model);ConfigurationEditingContext.Forget(Model);close?.Invoke();
            // Existing pages retain their invalid context. A new menu/page obtains
            // the new persisted draft; no stale task may publish after this point.
            Model.SetSaveFlag();SimulationSession.Mark(Model,SimulationDirtyFlags.Source|SimulationDirtyFlags.Mjcf);
            return backup;
        }
    }
}
