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
        public static SimulationConfigSnapshot Import(string json) { return ReadSimulationConfigSnapshot(Parse(json)); }
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
                var stable=Config.B(collision,"link_modes_migrated",false)||Config.D(collision,"link_modes_by_id")?.Count>0;
                var modes=Config.D(collision,stable?"link_modes_by_id":"link_modes");
                if(stable&&modes==null)throw new InvalidDataException("Missing stable collision mode map");
                if(modes!=null){var normalized=new Dictionary<string,object>();foreach(var pair in modes){var key=stable?(links.ContainsKey(pair.Key)?pair.Key:null):links.ContainsKey(pair.Key)?pair.Key:links.SingleOrDefault(p=>p.Value==pair.Key).Key;if(key==null)throw new InvalidDataException("Unknown collision link: "+pair.Key);normalized.Add(key,pair.Value);}collision["link_modes"]=normalized;}
            }
            return ReadSimulationConfigSnapshot(input);
        }
        static ConstraintSnapshot ReadConstraintSnapshot(Dictionary<string,object> input)
        {
            return new ConstraintSnapshot(
                Timeconst: LegacySimulationConfigImporter.Number(input,"timeconst"),
                Dampratio: LegacySimulationConfigImporter.Number(input,"dampratio"),
                Dmin: LegacySimulationConfigImporter.Number(input,"dmin"),
                Dmax: LegacySimulationConfigImporter.Number(input,"dmax"),
                Width: LegacySimulationConfigImporter.Number(input,"width"),
                Midpoint: LegacySimulationConfigImporter.Number(input,"midpoint"),
                Power: LegacySimulationConfigImporter.Number(input,"power"),
                Margin: LegacySimulationConfigImporter.Number(input,"margin"),
                Condim: LegacySimulationConfigImporter.Number(input,"condim"));
        }

        static SolverSnapshot ReadSolverSnapshot(Dictionary<string,object> input)
        {
            return new SolverSnapshot(
                Timestep: LegacySimulationConfigImporter.Number(input,"timestep"),
                Iterations: LegacySimulationConfigImporter.Number(input,"iterations"),
                Tolerance: LegacySimulationConfigImporter.Number(input,"tolerance"),
                NoslipIterations: LegacySimulationConfigImporter.Number(input,"noslip_iterations"),
                Impratio: LegacySimulationConfigImporter.Number(input,"impratio"),
                Enabled: LegacySimulationConfigImporter.Boolean(input,"enabled"),
                Equality: Config.D(input,"equality") == null ? null : ReadConstraintSnapshot(Config.D(input,"equality")),
                Contact: Config.D(input,"contact") == null ? null : ReadConstraintSnapshot(Config.D(input,"contact")));
        }

        static JointSettingsSnapshot ReadJointSettingsSnapshot(Dictionary<string,object> input)
        {
            return new JointSettingsSnapshot(
                Joint: LegacySimulationConfigImporter.String(input,"joint"),
                JointId: LegacySimulationConfigImporter.String(input,"joint_id"),
                Type: LegacySimulationConfigImporter.String(input,"type"),
                LimitMode: LegacySimulationConfigImporter.String(input,"limit_mode"),
                SpringMode: LegacySimulationConfigImporter.String(input,"spring_mode"),
                Damping: LegacySimulationConfigImporter.Number(input,"damping"),
                Frictionloss: LegacySimulationConfigImporter.Number(input,"frictionloss"),
                Armature: LegacySimulationConfigImporter.Number(input,"armature"),
                Stiffness: LegacySimulationConfigImporter.Number(input,"stiffness"),
                Springref: LegacySimulationConfigImporter.Number(input,"springref"),
                Ref: LegacySimulationConfigImporter.Number(input,"ref"),
                Margin: LegacySimulationConfigImporter.Number(input,"margin"),
                Lower: LegacySimulationConfigImporter.Number(input,"lower"),
                Upper: LegacySimulationConfigImporter.Number(input,"upper"),
                Pos: LegacySimulationConfigImporter.Vector(input,"pos",3),
                Axis: LegacySimulationConfigImporter.Vector(input,"axis",3),
                LimitSolver: Config.D(input,"limit_solver") == null ? null : ReadConstraintSnapshot(Config.D(input,"limit_solver")),
                FrictionSolver: Config.D(input,"friction_solver") == null ? null : ReadConstraintSnapshot(Config.D(input,"friction_solver")));
        }

        static JointForceSnapshot ReadJointForceSnapshot(Dictionary<string,object> input)
        {
            return new JointForceSnapshot(
                Joint: LegacySimulationConfigImporter.String(input,"joint"),
                JointId: LegacySimulationConfigImporter.String(input,"joint_id"),
                Lower: LegacySimulationConfigImporter.Number(input,"lower"),
                Upper: LegacySimulationConfigImporter.Number(input,"upper"));
        }

        static ActuatorSnapshot ReadActuatorSnapshot(Dictionary<string,object> input)
        {
            return new ActuatorSnapshot(
                Id: LegacySimulationConfigImporter.String(input,"id"),
                Name: LegacySimulationConfigImporter.String(input,"name"),
                Joint: LegacySimulationConfigImporter.String(input,"joint"),
                JointId: LegacySimulationConfigImporter.String(input,"joint_id"),
                Type: LegacySimulationConfigImporter.String(input,"type"),
                Gear: LegacySimulationConfigImporter.Number(input,"gear"),
                Gain: LegacySimulationConfigImporter.Number(input,"gain"),
                CtrlMin: LegacySimulationConfigImporter.Number(input,"ctrl_min"),
                CtrlMax: LegacySimulationConfigImporter.Number(input,"ctrl_max"),
                ForceMin: LegacySimulationConfigImporter.Number(input,"force_min"),
                ForceMax: LegacySimulationConfigImporter.Number(input,"force_max"));
        }

        static SensorSnapshot ReadSensorSnapshot(Dictionary<string,object> input)
        {
            return new SensorSnapshot(
                Id: LegacySimulationConfigImporter.String(input,"id"),
                Name: LegacySimulationConfigImporter.String(input,"name"),
                Site: LegacySimulationConfigImporter.String(input,"site"),
                SiteId: LegacySimulationConfigImporter.String(input,"site_id"),
                Type: LegacySimulationConfigImporter.String(input,"type"),
                Cutoff: LegacySimulationConfigImporter.Number(input,"cutoff"),
                Fovy: LegacySimulationConfigImporter.Number(input,"fovy"),
                Noise: LegacySimulationConfigImporter.Number(input,"noise"));
        }

        static EqualitySnapshot ReadEqualitySnapshot(Dictionary<string,object> input)
        {
            return new EqualitySnapshot(
                Id: LegacySimulationConfigImporter.String(input,"id"),
                Name: LegacySimulationConfigImporter.String(input,"name"),
                Type: LegacySimulationConfigImporter.String(input,"type"),
                Binding: LegacySimulationConfigImporter.String(input,"binding"),
                Site1: LegacySimulationConfigImporter.String(input,"site1"),
                Site1Id: LegacySimulationConfigImporter.String(input,"site1_id"),
                Site2: LegacySimulationConfigImporter.String(input,"site2"),
                Site2Id: LegacySimulationConfigImporter.String(input,"site2_id"),
                Joint1: LegacySimulationConfigImporter.String(input,"joint1"),
                Joint1Id: LegacySimulationConfigImporter.String(input,"joint1_id"),
                Joint2: LegacySimulationConfigImporter.String(input,"joint2"),
                Joint2Id: LegacySimulationConfigImporter.String(input,"joint2_id"),
                Body1: LegacySimulationConfigImporter.String(input,"body1"),
                Body1Id: LegacySimulationConfigImporter.String(input,"body1_id"),
                Body2: LegacySimulationConfigImporter.String(input,"body2"),
                Body2Id: LegacySimulationConfigImporter.String(input,"body2_id"),
                PoseMode: LegacySimulationConfigImporter.String(input,"pose_mode"),
                Torquescale: LegacySimulationConfigImporter.Number(input,"torquescale"),
                Active: LegacySimulationConfigImporter.Boolean(input,"active"),
                Polycoef: LegacySimulationConfigImporter.Vector(input,"polycoef",5),
                Anchor: LegacySimulationConfigImporter.Vector(input,"anchor",3),
                Position: LegacySimulationConfigImporter.Vector(input,"position",3),
                Orientation: LegacySimulationConfigImporter.Vector(input,"orientation",3),
                Solver: Config.D(input,"solver") == null ? null : ReadConstraintSnapshot(Config.D(input,"solver")));
        }

        static SiteForceSnapshot ReadSiteForceSnapshot(Dictionary<string,object> input)
        {
            return new SiteForceSnapshot(
                Id: LegacySimulationConfigImporter.String(input,"id"),
                Name: LegacySimulationConfigImporter.String(input,"name"),
                Type: LegacySimulationConfigImporter.String(input,"type"),
                Site1: LegacySimulationConfigImporter.String(input,"site1"),
                Site1Id: LegacySimulationConfigImporter.String(input,"site1_id"),
                Site2: LegacySimulationConfigImporter.String(input,"site2"),
                Site2Id: LegacySimulationConfigImporter.String(input,"site2_id"),
                LengthMode: LegacySimulationConfigImporter.String(input,"length_mode"),
                Stiffness: LegacySimulationConfigImporter.Number(input,"stiffness"),
                Damping: LegacySimulationConfigImporter.Number(input,"damping"),
                Magnitude: LegacySimulationConfigImporter.Number(input,"magnitude"),
                RestLength: LegacySimulationConfigImporter.Number(input,"rest_length"),
                Enabled: LegacySimulationConfigImporter.Boolean(input,"enabled"));
        }

        static CollisionGeometrySnapshot ReadCollisionGeometrySnapshot(Dictionary<string,object> input)
        {
            return new CollisionGeometrySnapshot(
                Id: LegacySimulationConfigImporter.String(input,"id"),
                Name: LegacySimulationConfigImporter.String(input,"name"),
                Link: LegacySimulationConfigImporter.String(input,"link"),
                LinkId: LegacySimulationConfigImporter.String(input,"link_id"),
                Type: LegacySimulationConfigImporter.String(input,"type"),
                Size: LegacySimulationConfigImporter.Vector(input,"size",0),
                Xyz: LegacySimulationConfigImporter.Vector(input,"xyz",3),
                Rpy: LegacySimulationConfigImporter.Vector(input,"rpy",3));
        }

        static ContactPairSnapshot ReadContactPairSnapshot(Dictionary<string,object> input)
        {
            return new ContactPairSnapshot(
                Link1: LegacySimulationConfigImporter.String(input,"link1"),
                Link1Id: LegacySimulationConfigImporter.String(input,"link1_id"),
                Link2: LegacySimulationConfigImporter.String(input,"link2"),
                Link2Id: LegacySimulationConfigImporter.String(input,"link2_id"),
                Solver: Config.D(input,"solver") == null ? null : ReadConstraintSnapshot(Config.D(input,"solver")));
        }

        static CollisionSnapshot ReadCollisionSnapshot(Dictionary<string,object> input)
        {
            return new CollisionSnapshot(
                DisableInternal: LegacySimulationConfigImporter.Boolean(input,"disable_internal"),
                Geometries: Array.AsReadOnly(Config.Items(input,"geometries").Select(v=>ReadCollisionGeometrySnapshot(v)).ToArray()),
                AllowedPairs: Array.AsReadOnly(Config.Items(input,"allowed_pairs").Select(v=>ReadContactPairSnapshot(v)).ToArray()),
                LinkModes: new ReadOnlyDictionary<string,string>((Config.D(input,"link_modes")??new Dictionary<string,object>()).ToDictionary(v=>v.Key,v=>Config.S(Config.D(input,"link_modes"),v.Key,null))));
        }

        static SimulationConfigSnapshot ReadSimulationConfigSnapshot(Dictionary<string,object> input)
        {
            return new SimulationConfigSnapshot(
                BaseMode: LegacySimulationConfigImporter.String(input,"base_mode"),
                JointDefaults: LegacySimulationConfigImporter.Boolean(input,"joint_defaults"),
                Solver: Config.D(input,"solver") == null ? null : ReadSolverSnapshot(Config.D(input,"solver")),
                Collision: Config.D(input,"collision") == null ? null : ReadCollisionSnapshot(Config.D(input,"collision")),
                Joints: Array.AsReadOnly(Config.Items(input,"joints").Select(v=>ReadJointSettingsSnapshot(v)).ToArray()),
                JointForceLimits: Array.AsReadOnly(Config.Items(input,"joint_force_limits").Select(v=>ReadJointForceSnapshot(v)).ToArray()),
                Actuators: Array.AsReadOnly(Config.Items(input,"actuators").Select(v=>ReadActuatorSnapshot(v)).ToArray()),
                Sensors: Array.AsReadOnly(Config.Items(input,"sensors").Select(v=>ReadSensorSnapshot(v)).ToArray()),
                Equalities: Array.AsReadOnly(Config.Items(input,"equalities").Select(v=>ReadEqualitySnapshot(v)).ToArray()),
                SiteForces: Array.AsReadOnly(Config.Items(input,"site_forces").Select(v=>ReadSiteForceSnapshot(v)).ToArray()),
                Sites: Array.AsReadOnly(Config.Items(input,"attachments").Select(LegacySimulationConfigImporter.Site).ToArray()));
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
