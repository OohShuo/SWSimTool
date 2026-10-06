using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SW2URDF.RobotModel
{
    public sealed class PreparedMeshAsset
    {
        public readonly string SourceId,Name,RelativePath;
        public PreparedMeshAsset(string sourceId,string name,string path)
        {
            if(string.IsNullOrWhiteSpace(sourceId)||string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(path)||Path.IsPathRooted(path)||path.Contains(":")||path.Split('/','\\').Any(p=>p==".."||p=="."))throw new InvalidDataException("Invalid prepared mesh path");
            SourceId=sourceId;Name=name;RelativePath=path.Replace('\\','/');
        }
    }
    public sealed class PreparedAssets
    {
        public ReadOnlyCollection<PreparedMeshAsset> Meshes { get; private set; }
        public PreparedAssets(IEnumerable<PreparedMeshAsset> meshes) { Meshes=Array.AsReadOnly(meshes.ToArray());RobotModelValidator.Unique(Meshes.Select(m=>m.SourceId),"prepared source");RobotModelValidator.Unique(Meshes.Select(m=>m.Name),"mesh name");RobotModelValidator.Unique(Meshes.Select(m=>m.RelativePath),"mesh path"); }
    }
    public sealed class ExportContext
    {
        public readonly string ModelName;
        public ExportContext(string name) { ModelName=name; }
    }
    public static class MjcfExporter
    {
        public static string Generate(RobotModel model,PreparedAssets assets,ExportContext context)
        {
            RobotModelValidator.Validate(model.Core);
            var root=new XElement("mujoco",new XAttribute("model",context.ModelName));
            root.Add(new XElement("compiler",new XAttribute("angle","radian"),new XAttribute("fusestatic","false"),new XAttribute("inertiafromgeom","auto")));
            var group=new XElement("asset");var meshNames=new Dictionary<string,string>();var number=0;
            foreach(var mesh in model.Core.Links.SelectMany(l=>l.Geometries).Where(g=>g.Mesh!=null).Select(g=>g.Mesh).GroupBy(m=>m.Id+"|"+m.Scale.ToString()).Select(g=>g.First())){
                var prepared=assets.Meshes.SingleOrDefault(m=>m.SourceId==mesh.Id);if(prepared==null)throw new InvalidDataException("Unprepared mesh: "+mesh.Id);
                var name=prepared.Name+"_"+number++;meshNames[mesh.Id+"|"+mesh.Scale.ToString()]=name;
                group.Add(new XElement("mesh",new XAttribute("name",name),new XAttribute("file",prepared.RelativePath),new XAttribute("scale",mesh.Scale.ToString())));
            }if(group.HasElements)root.Add(group);
            var config=model.Simulation;var world=new XElement("worldbody");root.Add(world);var bodies=new Dictionary<string,XElement>();var joints=new Dictionary<string,XElement>();
            var core=model.Core;
            Action<LinkSnapshot,XElement,JointSnapshot> build=null;
            build=(link,parent,incoming)=>{
                XElement body;{
                    body=new XElement("body",new XAttribute("name",link.Name));parent.Add(body);
                    if(incoming!=null){Pose(body,incoming.ParentLinkFromJoint);if(incoming.Kind!=JointKind.Fixed){
                        if(incoming.Kind==JointKind.Floating)body.Add(new XElement("freejoint",new XAttribute("name",incoming.Name)));
                        else{var j=new XElement("joint",new XAttribute("name",incoming.Name),new XAttribute("type",incoming.Kind==JointKind.Prismatic?"slide":"hinge"),new XAttribute("axis",incoming.AxisInJointFrame.ToString()),new XAttribute("pos","0 0 0"),new XAttribute("damping",Numbers.Format(incoming.Damping)),new XAttribute("frictionloss",Numbers.Format(incoming.FrictionLoss)),new XAttribute("limited",incoming.Lower.HasValue?"true":"false"));
                            if(incoming.Lower.HasValue)j.SetAttributeValue("range",Numbers.Text(new[]{incoming.Lower.Value,incoming.Upper.Value}));if(incoming.EffortLimit>0){j.SetAttributeValue("actuatorfrclimited","true");j.SetAttributeValue("actuatorfrcrange",Numbers.Text(new[]{-incoming.EffortLimit,incoming.EffortLimit}));}body.Add(j);joints[incoming.Id]=j;}
                    }}
                }bodies[link.Id]=body;
                if(body!=world&&link.Inertial!=null&&link.Inertial.Mass>0){var i=link.Inertial;body.Add(new XElement("inertial",new XAttribute("pos",i.LinkFromInertial.Translation.ToString()),new XAttribute("mass",Numbers.Format(i.Mass)),new XAttribute("fullinertia",i.InertiaAtComInLinkFrame.ToString())));}
                var index=0;foreach(var geom in link.Geometries){var g=new XElement("geom",new XAttribute("name",link.Name+"_"+(geom.IsCollision?"collision":"visual")+"_"+index++),new XAttribute("type",geom.Kind.ToString().ToLowerInvariant()),new XAttribute("rgba",Numbers.Text(geom.Rgba)),new XAttribute("group",geom.IsCollision?"0":"1"));Pose(g,geom.LinkFromGeometry);
                    if(!geom.IsCollision){g.SetAttributeValue("contype","0");g.SetAttributeValue("conaffinity","0");g.SetAttributeValue("density","0");}
                    switch(geom.Kind){case GeometryKind.Mesh:g.SetAttributeValue("mesh",meshNames[geom.Mesh.Id+"|"+geom.Mesh.Scale.ToString()]);break;case GeometryKind.Box:g.SetAttributeValue("size",(geom.Dimensions*.5).ToString());break;case GeometryKind.Sphere:g.SetAttributeValue("size",Numbers.Format(geom.Dimensions.X));break;case GeometryKind.Cylinder:g.SetAttributeValue("size",Numbers.Text(new[]{geom.Dimensions.X,geom.Dimensions.Y/2}));break;}body.Add(g);
                }
                foreach(var child in core.Joints.Where(j=>j.ParentLinkId==link.Id))build(core.Links.Single(l=>l.Id==child.ChildLinkId),body,child);
            };build(core.Root,world,null);
            foreach(var site in model.Simulation.Sites){var s=new XElement("site",new XAttribute("name",site.Name),new XAttribute("size","0.003"),new XAttribute("rgba","1 0.3 0.1 1"));Pose(s,site.LinkFromSite);bodies[site.LinkId].Add(s);}
            SimulationExtensions.Apply(root,model,config,bodies,joints);
            using(var stream=new MemoryStream()){
                using(var writer=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),Indent=true,CloseOutput=false}))new XDocument(root).Save(writer);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        internal static void Pose(XElement element,RigidTransform pose) { element.SetAttributeValue("pos",pose.Translation.ToString());element.SetAttributeValue("quat",pose.Rotation.ToString()); }
    }
}
