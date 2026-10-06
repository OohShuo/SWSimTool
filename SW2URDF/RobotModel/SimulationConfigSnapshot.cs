using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace SW2URDF.RobotModel
{
    public sealed class SiteSnapshot
    {
        public readonly string Id,Name,LinkId;
        public readonly bool IsFrame;
        public readonly RigidTransform LinkFromSite;
        public SiteSnapshot(string id,string name,string link,bool frame,RigidTransform pose) { Id=id;Name=name;LinkId=link;IsFrame=frame;LinkFromSite=pose; }
    }
    public sealed class SimulationConfigSnapshot
    {
        public ReadOnlyCollection<SiteSnapshot> Sites { get; private set; }
        // Isolated legacy configuration bridge. Never expose mutable DTOs to the model caller.
        // Subsequent extension ports replace this private bridge with typed immutable records.
        readonly string legacyJson;
        internal Dictionary<string,object> Configuration() { return new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(legacyJson); }
        public SimulationConfigSnapshot(string resolvedJson)
        {
            var config=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Deserialize<Dictionary<string,object>>(resolvedJson??"{}");if(config==null)throw new InvalidDataException("Invalid simulation configuration");
            if(Config.N(config,"schema_version",1)!=1||Config.S(config,"units","m,rad")!="m,rad")throw new NotSupportedException("Unsupported simulation schema/units");
            legacyJson=new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(config);
            Sites=Array.AsReadOnly(Config.Items(config,"attachments").Select(a=>{
                var name=Config.S(a,"name",null);var kind=Config.S(a,"type",null);if(kind!="frame"&&kind!="point")throw new InvalidDataException("Invalid site type");
                var xyz=Config.V(a,"xyz",3,null);var rpy=kind=="frame"?Config.V(a,"rpy",3,null):new double[3];
                return new SiteSnapshot(Config.S(a,"id",name),name,Config.S(a,"link",null),kind=="frame",new RigidTransform(new Vector3d(xyz[0],xyz[1],xyz[2]),Quaterniond.FromRpy(new Vector3d(rpy[0],rpy[1],rpy[2]))));
            }).ToArray());
            RobotModelValidator.Unique(Sites.Select(s=>s.Id),"site ID");RobotModelValidator.Unique(Sites.Select(s=>s.Name),"site name");
        }
    }
    public sealed class RobotModel
    {
        public const int SchemaVersion=1;
        public RobotCoreSnapshot Core { get; private set; }
        public SimulationConfigSnapshot Simulation { get; private set; }
        public RobotModel(RobotCoreSnapshot core,SimulationConfigSnapshot simulation)
        { Core=core??throw new ArgumentNullException("core");Simulation=simulation??throw new ArgumentNullException("simulation");foreach(var s in simulation.Sites){RobotModelValidator.ValidatePose(s.LinkFromSite);if(!core.Links.Any(l=>l.Id==s.LinkId))throw new InvalidDataException("Unknown site link: "+s.LinkId);} }
    }
    internal static class Config
    {
        public static Dictionary<string,object> D(Dictionary<string,object> value,string key) { object x;return value!=null&&value.TryGetValue(key,out x)&&x!=null?x as Dictionary<string,object>??throw new InvalidDataException("Expected object: "+key):null; }
        public static IEnumerable<Dictionary<string,object>> Items(Dictionary<string,object> value,string key) { object x;if(!value.TryGetValue(key,out x)||x==null)return new Dictionary<string,object>[0];var a=x as System.Collections.IList;if(a==null)throw new InvalidDataException("Expected array: "+key);return a.Cast<object>().Select(v=>v as Dictionary<string,object>??throw new InvalidDataException("Expected array object: "+key)); }
        public static string S(Dictionary<string,object> value,string key,string fallback) { object x;if(value==null||!value.TryGetValue(key,out x)||x==null){if(fallback==null)throw new InvalidDataException("Missing "+key);return fallback;}if(!(x is string))throw new InvalidDataException("Expected string: "+key);return (string)x; }
        public static bool B(Dictionary<string,object> value,string key,bool fallback) { object x;if(value==null||!value.TryGetValue(key,out x)||x==null)return fallback;if(!(x is bool))throw new InvalidDataException("Expected boolean: "+key);return (bool)x; }
        public static double N(Dictionary<string,object> value,string key,double fallback) { object x;if(value==null||!value.TryGetValue(key,out x)||x==null)return fallback;if(x is bool||x is string)throw new InvalidDataException("Expected number: "+key);return Vector3d.Finite(Convert.ToDouble(x,System.Globalization.CultureInfo.InvariantCulture)); }
        public static double[] V(Dictionary<string,object> value,string key,int length,double[] fallback) { object x;if(value==null||!value.TryGetValue(key,out x)||x==null){if(fallback==null)throw new InvalidDataException("Missing "+key);return (double[])fallback.Clone();}var a=x as System.Collections.IList;if(a==null||a.Count!=length)throw new InvalidDataException("Invalid vector: "+key);return a.Cast<object>().Select(v=>{if(v is bool||v is string)throw new InvalidDataException("Expected numeric vector");return Vector3d.Finite(Convert.ToDouble(v,System.Globalization.CultureInfo.InvariantCulture));}).ToArray(); }
    }
}
