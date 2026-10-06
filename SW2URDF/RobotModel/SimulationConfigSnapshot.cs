using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace SW2URDF.RobotModel {
public sealed class ConstraintSnapshot {
public static readonly ConstraintSnapshot Default = new ConstraintSnapshot(new Dictionary<string,object>());
public readonly double? Timeconst;
public readonly double? Dampratio;
public readonly double? Dmin;
public readonly double? Dmax;
public readonly double? Width;
public readonly double? Midpoint;
public readonly double? Power;
public readonly double? Margin;
public readonly double? Condim;
internal ConstraintSnapshot(Dictionary<string,object> input) {
Timeconst=LegacySimulationConfigImporter.Number(input,"timeconst");
Dampratio=LegacySimulationConfigImporter.Number(input,"dampratio");
Dmin=LegacySimulationConfigImporter.Number(input,"dmin");
Dmax=LegacySimulationConfigImporter.Number(input,"dmax");
Width=LegacySimulationConfigImporter.Number(input,"width");
Midpoint=LegacySimulationConfigImporter.Number(input,"midpoint");
Power=LegacySimulationConfigImporter.Number(input,"power");
Margin=LegacySimulationConfigImporter.Number(input,"margin");
Condim=LegacySimulationConfigImporter.Number(input,"condim");
}}
public sealed class SolverSnapshot {
public readonly double? Timestep;
public readonly double? Iterations;
public readonly double? Tolerance;
public readonly double? NoslipIterations;
public readonly double? Impratio;
public readonly bool? Enabled;
public readonly ConstraintSnapshot Equality;
public readonly ConstraintSnapshot Contact;
internal SolverSnapshot(Dictionary<string,object> input) {
Timestep=LegacySimulationConfigImporter.Number(input,"timestep");
Iterations=LegacySimulationConfigImporter.Number(input,"iterations");
Tolerance=LegacySimulationConfigImporter.Number(input,"tolerance");
NoslipIterations=LegacySimulationConfigImporter.Number(input,"noslip_iterations");
Impratio=LegacySimulationConfigImporter.Number(input,"impratio");
Enabled=LegacySimulationConfigImporter.Boolean(input,"enabled");
Equality=Config.D(input,"equality") == null ? null : new ConstraintSnapshot(Config.D(input,"equality"));
Contact=Config.D(input,"contact") == null ? null : new ConstraintSnapshot(Config.D(input,"contact"));
}}
public sealed class JointSettingsSnapshot {
public readonly string Joint;
public readonly string JointId;
public readonly string Type;
public readonly string LimitMode;
public readonly string SpringMode;
public readonly double? Damping;
public readonly double? Frictionloss;
public readonly double? Armature;
public readonly double? Stiffness;
public readonly double? Springref;
public readonly double? Ref;
public readonly double? Margin;
public readonly double? Lower;
public readonly double? Upper;
public readonly ReadOnlyCollection<double> Pos;
public readonly ReadOnlyCollection<double> Axis;
public readonly ConstraintSnapshot LimitSolver;
public readonly ConstraintSnapshot FrictionSolver;
internal JointSettingsSnapshot(Dictionary<string,object> input) {
Joint=LegacySimulationConfigImporter.String(input,"joint");
JointId=LegacySimulationConfigImporter.String(input,"joint_id");
Type=LegacySimulationConfigImporter.String(input,"type");
LimitMode=LegacySimulationConfigImporter.String(input,"limit_mode");
SpringMode=LegacySimulationConfigImporter.String(input,"spring_mode");
Damping=LegacySimulationConfigImporter.Number(input,"damping");
Frictionloss=LegacySimulationConfigImporter.Number(input,"frictionloss");
Armature=LegacySimulationConfigImporter.Number(input,"armature");
Stiffness=LegacySimulationConfigImporter.Number(input,"stiffness");
Springref=LegacySimulationConfigImporter.Number(input,"springref");
Ref=LegacySimulationConfigImporter.Number(input,"ref");
Margin=LegacySimulationConfigImporter.Number(input,"margin");
Lower=LegacySimulationConfigImporter.Number(input,"lower");
Upper=LegacySimulationConfigImporter.Number(input,"upper");
Pos=LegacySimulationConfigImporter.Vector(input,"pos",3);
Axis=LegacySimulationConfigImporter.Vector(input,"axis",3);
LimitSolver=Config.D(input,"limit_solver") == null ? null : new ConstraintSnapshot(Config.D(input,"limit_solver"));
FrictionSolver=Config.D(input,"friction_solver") == null ? null : new ConstraintSnapshot(Config.D(input,"friction_solver"));
}}
public sealed class JointForceSnapshot {
public readonly string Joint;
public readonly string JointId;
public readonly double? Lower;
public readonly double? Upper;
internal JointForceSnapshot(Dictionary<string,object> input) {
Joint=LegacySimulationConfigImporter.String(input,"joint");
JointId=LegacySimulationConfigImporter.String(input,"joint_id");
Lower=LegacySimulationConfigImporter.Number(input,"lower");
Upper=LegacySimulationConfigImporter.Number(input,"upper");
}}
public sealed class ActuatorSnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Joint;
public readonly string JointId;
public readonly string Type;
public readonly double? Gear;
public readonly double? Gain;
public readonly double? CtrlMin;
public readonly double? CtrlMax;
public readonly double? ForceMin;
public readonly double? ForceMax;
internal ActuatorSnapshot(Dictionary<string,object> input) {
Id=LegacySimulationConfigImporter.String(input,"id");
Name=LegacySimulationConfigImporter.String(input,"name");
Joint=LegacySimulationConfigImporter.String(input,"joint");
JointId=LegacySimulationConfigImporter.String(input,"joint_id");
Type=LegacySimulationConfigImporter.String(input,"type");
Gear=LegacySimulationConfigImporter.Number(input,"gear");
Gain=LegacySimulationConfigImporter.Number(input,"gain");
CtrlMin=LegacySimulationConfigImporter.Number(input,"ctrl_min");
CtrlMax=LegacySimulationConfigImporter.Number(input,"ctrl_max");
ForceMin=LegacySimulationConfigImporter.Number(input,"force_min");
ForceMax=LegacySimulationConfigImporter.Number(input,"force_max");
}}
public sealed class SensorSnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Site;
public readonly string SiteId;
public readonly string Type;
public readonly double? Cutoff;
public readonly double? Fovy;
public readonly double? Noise;
internal SensorSnapshot(Dictionary<string,object> input) {
Id=LegacySimulationConfigImporter.String(input,"id");
Name=LegacySimulationConfigImporter.String(input,"name");
Site=LegacySimulationConfigImporter.String(input,"site");
SiteId=LegacySimulationConfigImporter.String(input,"site_id");
Type=LegacySimulationConfigImporter.String(input,"type");
Cutoff=LegacySimulationConfigImporter.Number(input,"cutoff");
Fovy=LegacySimulationConfigImporter.Number(input,"fovy");
Noise=LegacySimulationConfigImporter.Number(input,"noise");
}}
public sealed class EqualitySnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Type;
public readonly string Binding;
public readonly string Site1;
public readonly string Site1Id;
public readonly string Site2;
public readonly string Site2Id;
public readonly string Joint1;
public readonly string Joint1Id;
public readonly string Joint2;
public readonly string Joint2Id;
public readonly string Body1;
public readonly string Body1Id;
public readonly string Body2;
public readonly string Body2Id;
public readonly string PoseMode;
public readonly double? Torquescale;
public readonly bool? Active;
public readonly ReadOnlyCollection<double> Polycoef;
public readonly ReadOnlyCollection<double> Anchor;
public readonly ReadOnlyCollection<double> Position;
public readonly ReadOnlyCollection<double> Orientation;
public readonly ConstraintSnapshot Solver;
internal EqualitySnapshot(Dictionary<string,object> input) {
Id=LegacySimulationConfigImporter.String(input,"id");
Name=LegacySimulationConfigImporter.String(input,"name");
Type=LegacySimulationConfigImporter.String(input,"type");
Binding=LegacySimulationConfigImporter.String(input,"binding");
Site1=LegacySimulationConfigImporter.String(input,"site1");
Site1Id=LegacySimulationConfigImporter.String(input,"site1_id");
Site2=LegacySimulationConfigImporter.String(input,"site2");
Site2Id=LegacySimulationConfigImporter.String(input,"site2_id");
Joint1=LegacySimulationConfigImporter.String(input,"joint1");
Joint1Id=LegacySimulationConfigImporter.String(input,"joint1_id");
Joint2=LegacySimulationConfigImporter.String(input,"joint2");
Joint2Id=LegacySimulationConfigImporter.String(input,"joint2_id");
Body1=LegacySimulationConfigImporter.String(input,"body1");
Body1Id=LegacySimulationConfigImporter.String(input,"body1_id");
Body2=LegacySimulationConfigImporter.String(input,"body2");
Body2Id=LegacySimulationConfigImporter.String(input,"body2_id");
PoseMode=LegacySimulationConfigImporter.String(input,"pose_mode");
Torquescale=LegacySimulationConfigImporter.Number(input,"torquescale");
Active=LegacySimulationConfigImporter.Boolean(input,"active");
Polycoef=LegacySimulationConfigImporter.Vector(input,"polycoef",5);
Anchor=LegacySimulationConfigImporter.Vector(input,"anchor",3);
Position=LegacySimulationConfigImporter.Vector(input,"position",3);
Orientation=LegacySimulationConfigImporter.Vector(input,"orientation",3);
Solver=Config.D(input,"solver") == null ? null : new ConstraintSnapshot(Config.D(input,"solver"));
}}
public sealed class SiteForceSnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Type;
public readonly string Site1;
public readonly string Site1Id;
public readonly string Site2;
public readonly string Site2Id;
public readonly string LengthMode;
public readonly double? Stiffness;
public readonly double? Damping;
public readonly double? Magnitude;
public readonly double? RestLength;
public readonly bool? Enabled;
internal SiteForceSnapshot(Dictionary<string,object> input) {
Id=LegacySimulationConfigImporter.String(input,"id");
Name=LegacySimulationConfigImporter.String(input,"name");
Type=LegacySimulationConfigImporter.String(input,"type");
Site1=LegacySimulationConfigImporter.String(input,"site1");
Site1Id=LegacySimulationConfigImporter.String(input,"site1_id");
Site2=LegacySimulationConfigImporter.String(input,"site2");
Site2Id=LegacySimulationConfigImporter.String(input,"site2_id");
LengthMode=LegacySimulationConfigImporter.String(input,"length_mode");
Stiffness=LegacySimulationConfigImporter.Number(input,"stiffness");
Damping=LegacySimulationConfigImporter.Number(input,"damping");
Magnitude=LegacySimulationConfigImporter.Number(input,"magnitude");
RestLength=LegacySimulationConfigImporter.Number(input,"rest_length");
Enabled=LegacySimulationConfigImporter.Boolean(input,"enabled");
}}
public sealed class CollisionGeometrySnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Link;
public readonly string LinkId;
public readonly string Type;
public readonly ReadOnlyCollection<double> Size;
public readonly ReadOnlyCollection<double> Xyz;
public readonly ReadOnlyCollection<double> Rpy;
internal CollisionGeometrySnapshot(Dictionary<string,object> input) {
Id=LegacySimulationConfigImporter.String(input,"id");
Name=LegacySimulationConfigImporter.String(input,"name");
Link=LegacySimulationConfigImporter.String(input,"link");
LinkId=LegacySimulationConfigImporter.String(input,"link_id");
Type=LegacySimulationConfigImporter.String(input,"type");
Size=LegacySimulationConfigImporter.Vector(input,"size",0);
Xyz=LegacySimulationConfigImporter.Vector(input,"xyz",3);
Rpy=LegacySimulationConfigImporter.Vector(input,"rpy",3);
}}
public sealed class ContactPairSnapshot {
public readonly string Link1;
public readonly string Link1Id;
public readonly string Link2;
public readonly string Link2Id;
public readonly ConstraintSnapshot Solver;
internal ContactPairSnapshot(Dictionary<string,object> input) {
Link1=LegacySimulationConfigImporter.String(input,"link1");
Link1Id=LegacySimulationConfigImporter.String(input,"link1_id");
Link2=LegacySimulationConfigImporter.String(input,"link2");
Link2Id=LegacySimulationConfigImporter.String(input,"link2_id");
Solver=Config.D(input,"solver") == null ? null : new ConstraintSnapshot(Config.D(input,"solver"));
}}
public sealed class CollisionSnapshot {
public readonly bool? DisableInternal;
public readonly ReadOnlyCollection<CollisionGeometrySnapshot> Geometries;
public readonly ReadOnlyCollection<ContactPairSnapshot> AllowedPairs;
public readonly ReadOnlyDictionary<string,string> LinkModes;
internal CollisionSnapshot(Dictionary<string,object> input) {
DisableInternal=LegacySimulationConfigImporter.Boolean(input,"disable_internal");
Geometries=Array.AsReadOnly(Config.Items(input,"geometries").Select(v=>new CollisionGeometrySnapshot(v)).ToArray());
AllowedPairs=Array.AsReadOnly(Config.Items(input,"allowed_pairs").Select(v=>new ContactPairSnapshot(v)).ToArray());
LinkModes=new ReadOnlyDictionary<string,string>((Config.D(input,"link_modes")??new Dictionary<string,object>()).ToDictionary(v=>v.Key,v=>Config.S(Config.D(input,"link_modes"),v.Key,null)));
}}
public sealed class SimulationConfigSnapshot {
public readonly string BaseMode;
public readonly bool? JointDefaults;
public readonly SolverSnapshot Solver;
public readonly CollisionSnapshot Collision;
public readonly ReadOnlyCollection<JointSettingsSnapshot> Joints;
public readonly ReadOnlyCollection<JointForceSnapshot> JointForceLimits;
public readonly ReadOnlyCollection<ActuatorSnapshot> Actuators;
public readonly ReadOnlyCollection<SensorSnapshot> Sensors;
public readonly ReadOnlyCollection<EqualitySnapshot> Equalities;
public readonly ReadOnlyCollection<SiteForceSnapshot> SiteForces;
public readonly ReadOnlyCollection<SiteSnapshot> Sites;
public SimulationConfigSnapshot(string json):this(LegacySimulationConfigImporter.Parse(json)) {}
internal SimulationConfigSnapshot(Dictionary<string,object> input) {
BaseMode=LegacySimulationConfigImporter.String(input,"base_mode");
JointDefaults=LegacySimulationConfigImporter.Boolean(input,"joint_defaults");
Solver=Config.D(input,"solver") == null ? null : new SolverSnapshot(Config.D(input,"solver"));
Collision=Config.D(input,"collision") == null ? null : new CollisionSnapshot(Config.D(input,"collision"));
Joints=Array.AsReadOnly(Config.Items(input,"joints").Select(v=>new JointSettingsSnapshot(v)).ToArray());
JointForceLimits=Array.AsReadOnly(Config.Items(input,"joint_force_limits").Select(v=>new JointForceSnapshot(v)).ToArray());
Actuators=Array.AsReadOnly(Config.Items(input,"actuators").Select(v=>new ActuatorSnapshot(v)).ToArray());
Sensors=Array.AsReadOnly(Config.Items(input,"sensors").Select(v=>new SensorSnapshot(v)).ToArray());
Equalities=Array.AsReadOnly(Config.Items(input,"equalities").Select(v=>new EqualitySnapshot(v)).ToArray());
SiteForces=Array.AsReadOnly(Config.Items(input,"site_forces").Select(v=>new SiteForceSnapshot(v)).ToArray());
Sites=Array.AsReadOnly(Config.Items(input,"attachments").Select(LegacySimulationConfigImporter.Site).ToArray());RobotModelValidator.Unique(Sites.Select(s=>s.Id),"site ID");RobotModelValidator.Unique(Sites.Select(s=>s.Name),"site name");
}}
    public sealed class SiteSnapshot
    {
        public readonly string Id,Name,LinkId;
        public readonly bool IsFrame;
        public readonly RigidTransform LinkFromSite;
        public SiteSnapshot(string id,string name,string link,bool frame,RigidTransform pose) { Id=id;Name=name;LinkId=link;IsFrame=frame;LinkFromSite=pose; }
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
