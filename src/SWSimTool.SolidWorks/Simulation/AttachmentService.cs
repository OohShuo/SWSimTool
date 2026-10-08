using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SWSimTool.URDF;
using SWSimTool.URDFExport;
using SWSimTool.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SWSimTool.Simulation
{
    // CAD references live in the assembly Attribute, not in the URDF object model.
    public sealed partial class AttachmentService
    {
        private readonly ExportHelper exporter;
        public ConfigurationEditingContext Editing {get;private set;}
        public SWSimTool.UI.ConfigurationPageDraft PageDraft {get;private set;}
        bool pageClosed;
        public SimulationProject Project { get=>PageDraft==null?Editing.Project:PageDraft.Project; set{if(pageClosed)throw new InvalidDataException("配置页面已关闭。");if(PageDraft==null)Editing.Project=value;else PageDraft.Project=value;} }
        public Link DraftTree {get=>PageDraft==null?Editing.Tree:PageDraft.Tree;set{if(PageDraft==null)Editing.Tree=value;else PageDraft.Tree=value;} }
        public void BeginPage(){if(PageDraft!=null)throw new InvalidOperationException("此服务已被活动配置页面使用。");PageDraft=SWSimTool.UI.ConfigurationPageDraft.Open(Model,Editing);pageClosed=false;}
        public void EndPage(){if(PageDraft!=null){PageDraft.Dispose();PageDraft=null;pageClosed=true;}}
        public void FlushPage(){PageDraft?.Collect();}
        ConfigurationSession session=>Editing.Session;
        public void RequireCurrentDocument(){if(pageClosed)throw new InvalidDataException("配置页面已关闭，旧服务不可再保存或导出。");session?.RequireCurrent();PageDraft?.RequireCurrent();}
        public void ReloadSavedProject(){session.Refresh();}
        public string ProjectPath => exporter.ActiveSWModel.GetPathName() + ".swsimtool.json";
        public AttachmentService(ExportHelper exporter,bool isolated=false)
        {
            this.exporter = exporter;
            Editing=isolated?ConfigurationEditingContext.Detached((SldWorks)exporter.iSwApp,exporter.ActiveSWModel):ConfigurationEditingContext.Get((SldWorks)exporter.iSwApp,exporter.ActiveSWModel);
        }
        public ConfigurationCommitResult Save()
        {
            RequireCurrentDocument();FlushPage();
            var candidate=ConfigurationEditingContext.CopyProject(Project);
            var tree=DraftTree?.Clone();
            candidate.NormalizeSiteReferences();candidate.ValidateSolver();
            if(string.IsNullOrEmpty(Model.GetPathName()))throw new InvalidOperationException("Save the assembly first.");
            candidate.assembly=Model.GetPathName();candidate.configuration=Model.ConfigurationManager.ActiveConfiguration.Name;
            if(tree!=null){var nodes=new LinkNode(tree);if(ConfigurationSerialization.WriteBusinessTree(tree)!=ConfigurationSerialization.WriteBusinessTree(Editing.Tree))CadTreeReferences.Normalize(Model,nodes);tree=nodes.Snapshot();StableReferences.Normalize(candidate,nodes);}
            var preparedProject=ConfigurationEditingContext.CopyProject(candidate);
            var preparedTree=ConfigurationEditingContext.CopyTree(tree);
            var sync=PageDraft==null?new List<Action>():PrepareSavedReferences(PageDraft.Project,candidate);
            var result=tree!=null?SimulationStorage.SaveTree(App,Model,ConfigurationSerialization.WriteBusinessTree(tree),1.4,candidate):SimulationStorage.Save(App,Model,candidate);
            if(result.RefreshSucceeded)result.Run(()=>{session.Refresh();Editing.CommitPrepared(preparedProject,preparedTree);foreach(var apply in sync)apply();});
            if(!result.RefreshSucceeded)InvalidateCommittedPage(result);
            return result;
        }
        void InvalidateCommittedPage(ConfigurationCommitResult result){
            pageClosed=true;
            result.Run(()=>ConfigurationSession.Invalidate(Model));
            result.Run(()=>SimulationStorage.Invalidate(Model));
            result.Run(()=>ConfigurationEditingContext.Forget(Model));
            result.Run(()=>PageDraft?.Dispose());
        }
        static List<Action> PrepareSavedReferences(SimulationProject target,SimulationProject saved){
            var plan=new List<Action>();
            foreach(string group in new[]{"attachments","sensors","actuators","equalities","site_forces","joints","joint_force_limits"}){
                var property=typeof(SimulationProject).GetProperty(group);
                var a=(System.Collections.IList)property.GetValue(target);var b=(System.Collections.IList)property.GetValue(saved);
                if(a==null||b==null||a.Count!=b.Count)throw new InvalidDataException("保存结果的对象数量与候选草稿不同。");
                for(int i=0;i<a.Count;i++){
                    if(a[i]==null||b[i]==null||a[i].GetType()!=b[i].GetType())throw new InvalidDataException("保存结果的对象类型与草稿不同。");
                    foreach(var field in a[i].GetType().GetProperties())if(field.CanWrite&&(field.Name=="id"||field.Name.EndsWith("_id",StringComparison.Ordinal))){
                        if(field.PropertyType!=typeof(string)||field.GetIndexParameters().Length!=0)throw new InvalidDataException("引用字段必须为字符串："+field.Name);
                        var owner=a[i];var value=field.GetValue(b[i]);plan.Add(()=>field.SetValue(owner,value));
                    }
                }
            }
            return plan;
        }
        public sealed class Source
        {
            public string Name {get;set;}
            public string ComponentName {get;set;}
            public string SourceId {get;set;}
            public string ComponentId {get;set;}
            public string Type {get;set;}
            public override string ToString() => Name + (string.IsNullOrEmpty(ComponentName) ? "" : " <" + ComponentName + ">") + " [" + Type + "]";
        }
        List<Source> cachedSources;string sourcesRevision;
        public List<Source> Sources()
        {
            string revision=CollisionRevision;if(cachedSources!=null&&sourcesRevision==revision)return new List<Source>(cachedSources);
            var result=CadSnapshotCache.Get(Model).Resolve("sources",()=>ScanSources());cachedSources=result;sourcesRevision=revision;return new List<Source>(result);
        }
        List<Source> ScanSources(){
            using(var timing=new PerformanceScope("cad.sources_scan")){var result = new List<Source>();
            AddSources(result, exporter.ActiveSWModel, null);
            var assembly = exporter.ActiveSWModel as AssemblyDoc;
            if (assembly != null)
                foreach (Component2 component in (object[])assembly.GetComponents(false) ?? new object[0])
                {
                    var model = component.GetModelDoc2() as ModelDoc2;
                    if (model != null) AddSources(result, model, component);
                }
            return result;}
        }
        private void AddSources(List<Source> sources, ModelDoc2 model, Component2 component)
        {
            for (Feature feature = model.FirstFeature(); feature != null; feature = feature.GetNextFeature())
            {
                string type = feature.GetTypeName2();
                if (type == "CoordSys" || type == "RefPoint")
                    sources.Add(new Source { Name=feature.Name, ComponentName=component?.Name2,
                        SourceId=model.Extension==null?null:PersistentId(model.Extension.GetPersistReference3(feature)),
                        ComponentId=component==null?null:PersistentId(Model.Extension.GetPersistReference3(component)), Type = type == "CoordSys" ? "frame" : "point" });
            }
        }
        static string PersistentId(object value){var data=value as byte[];return data==null?null:Convert.ToBase64String(data);}
        public Attachment Capture(Source source, string link, string name)
        {
            if(string.IsNullOrEmpty(source.SourceId))throw new InvalidOperationException("参考不支持持久引用，请重新拾取。");
            return new Attachment { name = name, link = link, type = source.Type, source_name = source.Name,
                source_pid = source.SourceId,component_pid = source.ComponentId,component_name = source.ComponentName };
        }
        private Matrix<double> Resolve(Attachment attachment)
        {
            var values=CadSnapshotCache.Get(Model).Resolve("attachment:"+ExportFingerprint.Hash(attachment),()=>ResolveCore(attachment).ToRowMajorArray());
            return Matrix<double>.Build.DenseOfRowMajor(4,4,values);
        }
        private Matrix<double> ResolveCore(Attachment attachment)
        {
            if(attachment.reference!=null)return ReferenceFrame(attachment.reference,attachment.type=="frame");
            ModelDoc2 model = exporter.ActiveSWModel;
            Component2 component = null;
            int error;
            if (!string.IsNullOrEmpty(attachment.component_pid))
            {
                component = model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(attachment.component_pid), out error) as Component2;
                if (component == null) throw new InvalidOperationException("Component reference is missing: " + attachment.name);
                model = component.GetModelDoc2() as ModelDoc2;
            }
            if (model == null) throw new InvalidOperationException("Resolve the component for " + attachment.name);
            Feature feature = model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(attachment.source_pid), out error) as Feature;
            if (feature == null) throw new InvalidOperationException("CAD reference is missing; rebind " + attachment.name);
            Matrix<double> local;
            if (attachment.type == "frame" && feature.GetTypeName2() == "CoordSys")
                local = MathOps.GetTransformation(model.Extension.GetCoordinateSystemTransformByName(feature.Name));
            else if (attachment.type == "point" && feature.GetSpecificFeature2() is RefPoint point)
                local = MathOps.GetTranslation((double[])((MathPoint)point.GetRefPoint()).ArrayData);
            else throw new InvalidOperationException("Unsupported source for " + attachment.name);
            // Component.Transform2 is relative to the root assembly, including nested placement.
            return component == null ? local : MathOps.GetTransformation(component.Transform2) * local;
        }
        public Dictionary<string, object> Export(string urdfPath)
        {
            if(exporter.URDFRobot?.BaseLink!=null)StableReferences.Normalize(Project,new LinkNode(exporter.URDFRobot.BaseLink));
            Project.NormalizeSiteReferences();
            Project.ValidateSolver();
            var transforms = new Dictionary<string, Matrix<double>>();
            if(exportFrames!=null)transforms=LinkTransforms();
            else{SetCollisionTree(null);Link root = exporter.URDFRobot.BaseLink;
                var global = MathOps.GetTransformation(exporter.AttachmentCoordinateTransform(root.Joint.CoordinateSystemName));
                AddTransforms(root, global, transforms);}

            var items = new List<Dictionary<string, object>>();
            var identities=LinkIdentities();
            var names = new HashSet<string>();
            foreach (var attachment in Project.attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.name) || !names.Add(attachment.name)) throw new InvalidOperationException("Attachment names must be nonempty and unique.");
                string linkId=SimulationConfigBuilder.Reference(attachment.link_id,attachment.link,identities);attachment.link=identities[linkId];
                if (!transforms.TryGetValue(attachment.link, out Matrix<double> transform)) throw new InvalidOperationException("Unknown attachment link: " + attachment.link);
                var relative = transform.Inverse() * Resolve(attachment);
                var item = new Dictionary<string, object> { { "id", attachment.id }, { "name", attachment.name }, { "link", attachment.link }, { "link_id", linkId }, { "type", attachment.type }, { "xyz", MathOps.GetXYZ(relative) } };
                if (attachment.type == "frame") item.Add("rpy", MathOps.GetRPY(relative));
                items.Add(item);
            }
            // Newly exported sidecars use the safe internal-collision default;
            // already exported legacy sidecars remain unchanged on disk.
            Project.collision = Project.collision ?? new CollisionConfiguration();
            var modeIdentities=LinkIdentities();StableReferences.MigrateLegacyLinkModesByName(Project,modeIdentities);
            // The caller chooses the configured tree or actual exported robot frames.
            foreach (var geometry in Project.collision.geometries) ResolveCollision(geometry);

            Project.assembly=Model.GetPathName();Project.configuration=Model.ConfigurationManager.ActiveConfiguration.Name;
            var result = new Dictionary<string, object> { { "product", "SWSimTool" }, { "schema_version", 1 }, { "assembly", Project.assembly }, { "configuration", Project.configuration },
                { "urdf", Path.GetFileName(urdfPath) }, { "units", "m,rad" }, { "attachments", items },
                { "site_forces", Project.site_forces }, { "actuators", Project.actuators }, { "sensors", Project.sensors }, { "equalities", Project.equalities }, { "collision", Project.collision.LegacyExport(modeIdentities) }, { "solver", Project.solver }, { "joint_defaults", Project.joint_defaults }, { "joints", Project.joints }, { "base_mode", Project.base_mode }, { "joint_force_limits", Project.joint_force_limits } };
            if(exporter.URDFRobot?.BaseLink!=null) {
                var links=new Dictionary<string,string>();var joints=new Dictionary<string,string>();
                Action<Link> visit=null;visit=link=>{links.Add(link.Name,link.StableId);if(link.Parent!=null)joints.Add(link.Joint.Name,link.Joint.StableId);foreach(var child in link.Children)visit(child);};visit(exporter.URDFRobot.BaseLink);
                result["identities"]=new{links,joints};
            }
            return result;
        }
        public Attachment CaptureSelectedAttachment(string link,string name,string type){
            var reference=CaptureSelection();ReferenceFrame(reference,type=="frame");
            return new Attachment{name=name,link=link,type=type,reference=reference,source_name=reference.label};
        }
        public Matrix<double> AttachmentPose(Attachment attachment){
            if(!string.IsNullOrWhiteSpace(attachment.link_id)){var identities=LinkIdentities();attachment.link=identities[SimulationConfigBuilder.Reference(attachment.link_id,attachment.link,identities)];}
            Matrix<double> frame;if(!LinkTransforms().TryGetValue(attachment.link,out frame))throw new InvalidOperationException("附着点所属 link 已失效："+attachment.name);
            return frame.Inverse()*Resolve(attachment);
        }
        private static void AddTransforms(Link link, Matrix<double> transform, Dictionary<string, Matrix<double>> transforms)
        {
            transforms.Add(link.Name, transform);
            foreach (Link child in link.Children)
                AddTransforms(child, transform * MathOps.GetTransformation(child.Joint.Origin.GetXYZ(), child.Joint.Origin.GetRPY()), transforms);
        }
    }
}
