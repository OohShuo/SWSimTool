using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SW2URDF.RobotModel
{
    public static class LegacySimulationConfigImporter
    {
        public static SimulationConfigSnapshot Import(string json) { return new SimulationConfigSnapshot(Parse(json)); }
        // A local URDF has names, but no persisted CAD identities. Materialize its explicit
        // sidecar registry before resolving references; references never override that registry.
        public static RobotCoreSnapshot IdentifyImportedCore(RobotCoreSnapshot core,string json)
        {
            var input=Parse(json);var registry=Config.D(input,"identities");
            var links=core.Links.ToDictionary(l=>l.Name,l=>l.Id);var joints=core.Joints.ToDictionary(j=>j.Name,j=>j.Id);
            Action<string,Dictionary<string,string>> apply=(kind,map)=>{
                var declared=Config.D(registry,kind);
                if(declared!=null)foreach(var item in declared){if(!map.ContainsKey(item.Key))throw new InvalidDataException("Identity registry refers to missing "+kind+": "+item.Key);var id=Config.S(declared,item.Key,null);if(string.IsNullOrWhiteSpace(id))throw new InvalidDataException("Empty identity");map[item.Key]=id;}
            };
            apply("links",links);apply("joints",joints);
            if(registry==null) {
                // Pre-registry sidecars may carry partial stable references. This is a one-time
                // import of an external name-only format, never a rebind of an existing CAD core.
                Action<Dictionary<string,object>,string,Dictionary<string,string>> infer=(item,key,map)=>{
                    var name=String(item,key);var id=String(item,key+"_id");if(string.IsNullOrWhiteSpace(id))return;
                    if(name==null||!map.ContainsKey(name))throw new InvalidDataException("Unknown legacy target: "+name);
                    if(map[name]!=name&&map[name]!=id)throw new InvalidDataException("Conflicting legacy identity: "+name);map[name]=id;
                };
                foreach(var item in Config.Items(input,"attachments"))infer(item,"link",links);
                foreach(var list in new[]{"actuators","joints","joint_force_limits"})foreach(var item in Config.Items(input,list))infer(item,"joint",joints);
                foreach(var item in Config.Items(input,"equalities")){infer(item,"joint1",joints);infer(item,"joint2",joints);infer(item,"body1",links);infer(item,"body2",links);}
                var collision=Config.D(input,"collision");if(collision!=null){foreach(var item in Config.Items(collision,"geometries"))infer(item,"link",links);foreach(var item in Config.Items(collision,"allowed_pairs")){infer(item,"link1",links);infer(item,"link2",links);}}
            }
            var linkSnapshots=core.Links.Select(l=>new LinkSnapshot(links[l.Name],l.Name,l.Inertial,l.Geometries));
            var linkNames=core.Links.ToDictionary(l=>l.Id,l=>l.Name);
            var jointNames=core.Joints.ToDictionary(j=>j.Id,j=>j.Name);
            var jointSnapshots=core.Joints.Select(j=>new JointSnapshot(joints[j.Name],j.Name,links[linkNames[j.ParentLinkId]],links[linkNames[j.ChildLinkId]],j.Kind,j.ParentLinkFromJoint,j.AxisInJointFrame,j.Lower,j.Upper,j.Damping,j.FrictionLoss,j.EffortLimit,j.Mimic==null?null:new MimicSnapshot(joints[jointNames[j.Mimic.SourceJointId]],j.Mimic.Multiplier,j.Mimic.Offset)));
            return new RobotCoreSnapshot(core.Name,linkSnapshots,jointSnapshots);
        }
        public static SimulationConfigSnapshot Import(string json,RobotCoreSnapshot core)
        {
            var input=Parse(json);
            var links=core.Links.ToDictionary(l=>l.Id,l=>l.Name);
            var joints=core.Joints.ToDictionary(j=>j.Id,j=>j.Name);
            Action<Dictionary<string,object>,string,Dictionary<string,string>> bind=(item,key,objects)=>{
                var id=String(item,key+"_id");var name=String(item,key);
                if(string.IsNullOrWhiteSpace(id)&&string.IsNullOrWhiteSpace(name))return;
                string target;
                if(!string.IsNullOrWhiteSpace(id)){if(!objects.ContainsKey(id))throw new InvalidDataException("Unknown stable reference: "+id);target=id;}
                else {var found=objects.Where(p=>p.Value==name).ToArray();if(found.Length!=1)throw new InvalidDataException("Unknown or ambiguous reference: "+name);target=found[0].Key;}
                item[key]=target;item[key+"_id"]=target;
            };
            foreach(var item in Config.Items(input,"attachments"))bind(item,"link",links);
            foreach(var list in new[]{"joints","joint_force_limits","actuators"})foreach(var item in Config.Items(input,list))bind(item,"joint",joints);
            foreach(var item in Config.Items(input,"equalities")) {
                if(String(item,"type")=="joint"){bind(item,"joint1",joints);bind(item,"joint2",joints);}
                else if(String(item,"binding")=="body"){bind(item,"body1",links);bind(item,"body2",links);}
            }
            var collision=Config.D(input,"collision");
            if(collision!=null) {
                foreach(var item in Config.Items(collision,"geometries"))bind(item,"link",links);
                foreach(var item in Config.Items(collision,"allowed_pairs")){bind(item,"link1",links);bind(item,"link2",links);}
                var modes=Config.D(collision,"link_modes");if(modes!=null){var normalized=new Dictionary<string,object>();foreach(var pair in modes){var name=pair.Key;var key=links.ContainsKey(name)?name:links.SingleOrDefault(p=>p.Value==name).Key;if(key==null)throw new InvalidDataException("Unknown collision link: "+name);normalized.Add(key,pair.Value);}collision["link_modes"]=normalized;}
            }
            return new SimulationConfigSnapshot(input);
        }
        internal static Dictionary<string,object> Parse(string json)
        {
            var input=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(json??"{}");
            if(input==null)throw new InvalidDataException("Invalid simulation configuration");
            if(Config.N(input,"schema_version",1)!=1||Config.S(input,"units","m,rad")!="m,rad")throw new NotSupportedException("Unsupported simulation schema/units");
            return input;
        }
        internal static string String(Dictionary<string,object> input,string key)
        { object value;return !input.TryGetValue(key,out value)||value==null?null:Config.S(input,key,null); }
        internal static double? Number(Dictionary<string,object> input,string key)
        { object value;return !input.TryGetValue(key,out value)||value==null?(double?)null:Config.N(input,key,0); }
        internal static bool? Boolean(Dictionary<string,object> input,string key)
        { object value;return !input.TryGetValue(key,out value)||value==null?(bool?)null:Config.B(input,key,false); }
        internal static ReadOnlyCollection<double> Vector(Dictionary<string,object> input,string key,int length)
        {
            object value;if(!input.TryGetValue(key,out value)||value==null)return null;
            if(length==0){var list=value as System.Collections.IList;if(list==null)throw new InvalidDataException("Expected vector: "+key);length=list.Count;}
            return Array.AsReadOnly(Config.V(input,key,length,null));
        }
        internal static SiteSnapshot Site(Dictionary<string,object> item)
        {
            var name=Config.S(item,"name",null);var kind=Config.S(item,"type",null);
            if(kind!="frame"&&kind!="point")throw new InvalidDataException("Invalid site type");
            var xyz=Config.V(item,"xyz",3,null);var rpy=kind=="frame"?Config.V(item,"rpy",3,null):new double[3];
            return new SiteSnapshot(Config.S(item,"id",name),name,Config.S(item,"link",null),kind=="frame",new RigidTransform(new Vector3d(xyz[0],xyz[1],xyz[2]),Quaterniond.FromRpy(new Vector3d(rpy[0],rpy[1],rpy[2]))));
        }
    }
    internal static class TypedValues
    {
        internal static string Text(string value,string fallback,string label)
        { if(value!=null)return value;if(fallback==null)throw new InvalidDataException("Missing "+label);return fallback; }
        internal static double[] Vector(ReadOnlyCollection<double> value,int length,double[] fallback,string label)
        { if(value==null){if(fallback==null)throw new InvalidDataException("Missing "+label);return (double[])fallback.Clone();}if(value.Count!=length)throw new InvalidDataException("Invalid vector: "+label);return value.ToArray(); }
    }
}
