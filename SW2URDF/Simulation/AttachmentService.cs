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
    // CAD references live in the project JSON, not in the URDF object model.
    public sealed class AttachmentService
    {
        private readonly ExportHelper exporter;
        public SimulationProject Project { get; set; }
        public string ProjectPath => exporter.ActiveSWModel.GetPathName() + ".sw2urdf.json";
        public AttachmentService(ExportHelper exporter)
        {
            this.exporter = exporter;
            Project = SimulationProject.Load(ProjectPath);
            if (!string.IsNullOrEmpty(Project.configuration) && Project.configuration != exporter.ActiveSWModel.ConfigurationManager.ActiveConfiguration.Name)
                throw new InvalidOperationException("Simulation project belongs to configuration " + Project.configuration + "; activate that configuration before exporting.");
        }
        public void Save()
        {
            if (string.IsNullOrEmpty(exporter.ActiveSWModel.GetPathName())) throw new InvalidOperationException("Save the assembly first.");
            Project.assembly = exporter.ActiveSWModel.GetPathName();
            Project.configuration = exporter.ActiveSWModel.ConfigurationManager.ActiveConfiguration.Name;
            SimulationProject.Write(ProjectPath, Project);
        }
        public sealed class Source
        {
            public ModelDoc2 Model;
            public Component2 Component;
            public Feature Feature;
            public string Type;
            public override string ToString() => Feature.Name + (Component == null ? "" : " <" + Component.Name2 + ">") + " [" + Type + "]";
        }
        public List<Source> Sources()
        {
            var result = new List<Source>();
            AddSources(result, exporter.ActiveSWModel, null);
            var assembly = exporter.ActiveSWModel as AssemblyDoc;
            if (assembly != null)
                foreach (Component2 component in (object[])assembly.GetComponents(false) ?? new object[0])
                {
                    var model = component.GetModelDoc2() as ModelDoc2;
                    if (model != null) AddSources(result, model, component);
                }
            return result;
        }
        private static void AddSources(List<Source> sources, ModelDoc2 model, Component2 component)
        {
            for (Feature feature = model.FirstFeature(); feature != null; feature = feature.GetNextFeature())
            {
                string type = feature.GetTypeName2();
                if (type == "CoordSys" || type == "RefPoint")
                    sources.Add(new Source { Model = model, Component = component, Feature = feature, Type = type == "CoordSys" ? "frame" : "point" });
            }
        }
        public Attachment Capture(Source source, string link, string name)
        {
            return new Attachment { name = name, link = link, type = source.Type, source_name = source.Feature.Name,
                source_pid = Convert.ToBase64String((byte[])source.Model.Extension.GetPersistReference3(source.Feature)),
                component_pid = source.Component == null ? null : Convert.ToBase64String((byte[])exporter.ActiveSWModel.Extension.GetPersistReference3(source.Component)),
                component_name = source.Component?.Name2 };
        }
        private Matrix<double> Resolve(Attachment attachment)
        {
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
            var transforms = new Dictionary<string, Matrix<double>>();
            Link root = exporter.URDFRobot.BaseLink;
            var global = MathOps.GetTransformation(exporter.AttachmentCoordinateTransform(root.Joint.CoordinateSystemName));
            AddTransforms(root, global, transforms);
            var items = new List<Dictionary<string, object>>();
            var names = new HashSet<string>();
            foreach (var attachment in Project.attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.name) || !names.Add(attachment.name)) throw new InvalidOperationException("Attachment names must be nonempty and unique.");
                if (!transforms.TryGetValue(attachment.link, out Matrix<double> transform)) throw new InvalidOperationException("Unknown attachment link: " + attachment.link);
                var relative = transform.Inverse() * Resolve(attachment);
                var item = new Dictionary<string, object> { { "name", attachment.name }, { "link", attachment.link }, { "type", attachment.type }, { "xyz", MathOps.GetXYZ(relative) } };
                if (attachment.type == "frame") item.Add("rpy", MathOps.GetRPY(relative));
                items.Add(item);
            }
            Save();
            return new Dictionary<string, object> { { "schema_version", 1 }, { "assembly", Project.assembly }, { "configuration", Project.configuration },
                { "urdf", Path.GetFileName(urdfPath) }, { "units", "m,rad" }, { "attachments", items },
                { "actuators", Project.actuators }, { "sensors", Project.sensors }, { "equalities", Project.equalities } };
        }
        private static void AddTransforms(Link link, Matrix<double> transform, Dictionary<string, Matrix<double>> transforms)
        {
            transforms.Add(link.Name, transform);
            foreach (Link child in link.Children)
                AddTransforms(child, transform * MathOps.GetTransformation(child.Joint.Origin.GetXYZ(), child.Joint.Origin.GetRPY()), transforms);
        }
    }
}
