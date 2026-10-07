using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace SWSimTool.RobotModel
{
    public static class UrdfRobotModelImporter
    {
        public static RobotCoreSnapshot Load(string path) { using(var stream=File.OpenRead(path))return Read(stream,Path.GetDirectoryName(Path.GetFullPath(path))); }
        public static RobotCoreSnapshot Read(Stream input,string directory)
        {
            using(var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null})){
                var root=XDocument.Load(reader).Root;if(root==null||root.Name!="robot")throw new InvalidDataException("Expected URDF robot");
                // Unsupported semantics must fail visibly, never silently disappear.
                var supported=new HashSet<string>(new[]{"link","joint","material"});foreach(var e in root.Elements())if(!supported.Contains(e.Name.LocalName))throw new NotSupportedException("URDF extension: "+e.Name);
                if(root.Elements("material").Any(e=>e.Element("texture")!=null))throw new NotSupportedException("URDF textures not yet supported");
                var materials=root.Elements("material").ToDictionary(e=>Required(e,"name"),e=>Color(e));
                var links=new List<LinkSnapshot>();var joints=new List<JointSnapshot>();
                foreach(var e in root.Elements("link")){
                    var name=Required(e,"name");var inertial=e.Element("inertial");InertialSnapshot mass=null;
                    if(inertial!=null){var i=inertial.Element("inertia");if(i==null)throw new InvalidDataException("Missing inertia");mass=new InertialSnapshot(N(inertial.Element("mass"),"value"),Pose(inertial.Element("origin")),new SymmetricInertia(N(i,"ixx"),N(i,"iyy"),N(i,"izz"),N(i,"ixy"),N(i,"ixz"),N(i,"iyz")));}
                    var geometry=new List<GeometrySnapshot>();foreach(var v in e.Elements().Where(x=>x.Name=="visual"||x.Name=="collision")){
                        var shapes=v.Element("geometry");if(shapes==null||shapes.Elements().Count()!=1)throw new InvalidDataException("Expected one geometry shape");var shape=shapes.Elements().Single();GeometryKind kind;Vector3d dimensions=new Vector3d();MeshSource mesh=null;
                        switch(shape.Name.LocalName){case "box":kind=GeometryKind.Box;dimensions=V(shape,"size",null);break;case "sphere":kind=GeometryKind.Sphere;dimensions=new Vector3d(N(shape,"radius"),0,0);break;case "cylinder":kind=GeometryKind.Cylinder;dimensions=new Vector3d(N(shape,"radius"),N(shape,"length"),0);break;
                            case "mesh":kind=GeometryKind.Mesh;var filename=Required(shape,"filename");var resolved=ResolveMesh(filename,directory);mesh=new MeshSource(resolved,resolved,V(shape,"scale",new Vector3d(1,1,1)));break;default:throw new NotSupportedException("URDF geometry: "+shape.Name);}
                        var material=v.Element("material");if(material==null&&v.Name=="collision")material=e.Element("visual")?.Element("material");double[] color=null;if(material!=null){if(material.Element("texture")!=null)throw new NotSupportedException("URDF textures not yet supported");color=material.Element("color")!=null?Color(material):materials.ContainsKey((string)material.Attribute("name")??"")?materials[(string)material.Attribute("name")]:null;}
                        geometry.Add(new GeometrySnapshot(name+"/"+v.Name+"/"+geometry.Count,kind,Pose(v.Element("origin")),dimensions,mesh,v.Name=="collision",color));
                    }links.Add(new LinkSnapshot(name,name,mass,geometry));
                }
                foreach(var e in root.Elements("joint")){
                    JointKind kind;switch(Required(e,"type")){case "fixed":kind=JointKind.Fixed;break;case "revolute":kind=JointKind.Revolute;break;case "continuous":kind=JointKind.Continuous;break;case "prismatic":kind=JointKind.Prismatic;break;case "floating":kind=JointKind.Floating;break;default:throw new NotSupportedException("URDF joint type: "+Required(e,"type"));}
                    var limit=e.Element("limit");var limited=kind==JointKind.Revolute||kind==JointKind.Prismatic;var dynamics=e.Element("dynamics");var name=Required(e,"name");
                    var mimic=e.Element("mimic");joints.Add(new JointSnapshot(name,name,Required(e.Element("parent"),"link"),Required(e.Element("child"),"link"),kind,Pose(e.Element("origin")),V(e.Element("axis"),"xyz",new Vector3d(1,0,0)),limited?(double?)N(limit,"lower"):null,limited?(double?)N(limit,"upper"):null,N(dynamics,"damping",0),N(dynamics,"friction",0),N(limit,"effort",0),mimic==null?null:new MimicSnapshot(Required(mimic,"joint"),N(mimic,"multiplier",1),N(mimic,"offset",0))));
                }
                return new RobotCoreSnapshot((string)root.Attribute("name")??"robot",links,joints);
            }
        }
        internal static string Required(XElement e,string attr) { var s=e==null?null:(string)e.Attribute(attr);if(string.IsNullOrWhiteSpace(s))throw new InvalidDataException("Missing URDF attribute: "+attr);return s; }
        internal static double N(XElement e,string attr,double? fallback=null) { var s=e==null?null:(string)e.Attribute(attr);if(s==null&&fallback.HasValue)return fallback.Value;return Numbers.Parse(Required(e,attr)); }
        internal static Vector3d V(XElement e,string attr,Vector3d? fallback) { var s=e==null?null:(string)e.Attribute(attr);if(s==null&&fallback.HasValue)return fallback.Value;var values=Required(e,attr).Split((char[])null,StringSplitOptions.RemoveEmptyEntries).Select(Numbers.Parse).ToArray();if(values.Length!=3)throw new InvalidDataException("Expected XYZ");return new Vector3d(values[0],values[1],values[2]); }
        internal static RigidTransform Pose(XElement origin) { return new RigidTransform(V(origin,"xyz",new Vector3d()),Quaterniond.FromRpy(V(origin,"rpy",new Vector3d()))); }
        static double[] Color(XElement material) { var color=material.Element("color");if(color==null)return new[]{1.0,1,1,1};return Required(color,"rgba").Split((char[])null,StringSplitOptions.RemoveEmptyEntries).Select(Numbers.Parse).ToArray(); }
        static string ResolveMesh(string filename,string directory)
        {
            if(filename.StartsWith("package://",StringComparison.Ordinal)){var relative=filename.Substring(10);var slash=relative.IndexOf('/');if(slash<0)throw new InvalidDataException("Invalid package mesh URI");filename=Path.Combine(Path.GetDirectoryName(directory),relative.Substring(slash+1));}
            else if(filename.StartsWith("file://",StringComparison.Ordinal))filename=new Uri(filename).LocalPath;
            else if(filename.Contains("://"))throw new NotSupportedException("Mesh URI: "+filename);
            return Path.GetFullPath(Path.IsPathRooted(filename)?filename:Path.Combine(directory,filename));
        }
    }
}
