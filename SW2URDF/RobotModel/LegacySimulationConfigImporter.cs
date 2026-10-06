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
