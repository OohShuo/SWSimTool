using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.URDFExport;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SW2URDF.Simulation
{
    // CAD references live in the assembly Attribute, not in the URDF object model.
    public sealed partial class AttachmentService
    {
        private readonly ExportHelper exporter;
        public SimulationProject Project { get; set; }
        public string ProjectPath => exporter.ActiveSWModel.GetPathName() + ".sw2urdf.json";
        public AttachmentService(ExportHelper exporter)
        {
            this.exporter = exporter;
            Project = SimulationStorage.Load(exporter.ActiveSWModel);
            if (Project == null)
            {
                var legacy = SimulationProject.Load(ProjectPath);
                Project = string.IsNullOrEmpty(legacy.configuration) || legacy.configuration == exporter.ActiveSWModel.ConfigurationManager.ActiveConfiguration.Name
                    ? legacy : new SimulationProject();
            }
        }
        public void Save()
        {
            Project.NormalizeSiteReferences();
            Project.ValidateSolver();
            if (string.IsNullOrEmpty(exporter.ActiveSWModel.GetPathName())) throw new InvalidOperationException("Save the assembly first.");
            Project.assembly = exporter.ActiveSWModel.GetPathName();
            Project.configuration = exporter.ActiveSWModel.ConfigurationManager.ActiveConfiguration.Name;
            SimulationStorage.Save((SldWorks)exporter.iSwApp, exporter.ActiveSWModel, Project);
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
            Project.NormalizeSiteReferences();
            Project.ValidateSolver();
            var transforms = new Dictionary<string, Matrix<double>>();
            if(exportFrames!=null)transforms=LinkTransforms();
            else{SetCollisionTree(null);Link root = exporter.URDFRobot.BaseLink;
                var global = MathOps.GetTransformation(exporter.AttachmentCoordinateTransform(root.Joint.CoordinateSystemName));
                AddTransforms(root, global, transforms);}

            var items = new List<Dictionary<string, object>>();
            var names = new HashSet<string>();
            foreach (var attachment in Project.attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.name) || !names.Add(attachment.name)) throw new InvalidOperationException("Attachment names must be nonempty and unique.");
                if (!transforms.TryGetValue(attachment.link, out Matrix<double> transform)) throw new InvalidOperationException("Unknown attachment link: " + attachment.link);
                var relative = transform.Inverse() * Resolve(attachment);
                var item = new Dictionary<string, object> { { "id", attachment.id }, { "name", attachment.name }, { "link", attachment.link }, { "type", attachment.type }, { "xyz", MathOps.GetXYZ(relative) } };
                if (attachment.type == "frame") item.Add("rpy", MathOps.GetRPY(relative));
                items.Add(item);
            }
            // Newly exported sidecars use the safe internal-collision default;
            // already exported legacy sidecars remain unchanged on disk.
            Project.collision = Project.collision ?? new CollisionConfiguration();
            // The caller chooses the configured tree or actual exported robot frames.
            foreach (var geometry in Project.collision.geometries) ResolveCollision(geometry);

            Project.assembly=Model.GetPathName();Project.configuration=Model.ConfigurationManager.ActiveConfiguration.Name;
            return new Dictionary<string, object> { { "schema_version", 1 }, { "assembly", Project.assembly }, { "configuration", Project.configuration },
                { "urdf", Path.GetFileName(urdfPath) }, { "units", "m,rad" }, { "attachments", items },
                { "site_forces", Project.site_forces }, { "actuators", Project.actuators }, { "sensors", Project.sensors }, { "equalities", Project.equalities }, { "collision", Project.collision }, { "solver", Project.solver }, { "joint_defaults", Project.joint_defaults }, { "joints", Project.joints }, { "base_mode", Project.base_mode }, { "joint_force_limits", Project.joint_force_limits } };
        }
        public Attachment CaptureSelectedAttachment(string link,string name,string type){
            var reference=CaptureSelection();ReferenceFrame(reference,type=="frame");
            return new Attachment{name=name,link=link,type=type,reference=reference,source_name=reference.label};
        }
        public Matrix<double> AttachmentPose(Attachment attachment){
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
