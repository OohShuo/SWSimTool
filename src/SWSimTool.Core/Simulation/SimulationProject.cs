using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;


namespace SWSimTool.Simulation
{
    public sealed class ConstraintSettings
    {
        public double timeconst { get; set; } = .005;
        public double dampratio { get; set; } = 1;
        public double dmin { get; set; } = .99;
        public double dmax { get; set; } = .99;
        public double width { get; set; } = .001;
        public double midpoint { get; set; } = .5;
        public double power { get; set; } = 2;
        public double margin { get; set; }
        public int condim { get; set; } = 3;
        public static ConstraintSettings Contact() => new ConstraintSettings { timeconst=.003, dmax=.995, margin=.001 };
        public void Validate(bool contact)
        {
            foreach(double x in new[]{timeconst,dampratio,dmin,dmax,width,midpoint,power,margin})
                if(double.IsNaN(x)||double.IsInfinity(x))throw new InvalidDataException("求解参数必须为有限数字。");
            if(timeconst<=0||dampratio<=0||dmin<=0||dmax>=1||dmin>dmax||width<=0||midpoint<=0||midpoint>=1||power<1||margin<0)
                throw new InvalidDataException("求解参数范围无效：timeconst/阻尼/width > 0，0 < dmin ≤ dmax < 1，0 < midpoint < 1，power ≥ 1，margin ≥ 0。");
            if(contact&&condim!=1&&condim!=3&&condim!=4&&condim!=6)throw new InvalidDataException("condim 只能为 1、3、4、6。");
        }
    }
    public sealed class SolverSettings
    {
        public bool enabled { get; set; }
        public double timestep { get; set; } = .001;
        public int iterations { get; set; } = 100;
        public double tolerance { get; set; } = 1e-9;
        public int noslip_iterations { get; set; }
        public double impratio { get; set; } = 10;
        public ConstraintSettings equality { get; set; } = new ConstraintSettings();
        public ConstraintSettings contact { get; set; } = ConstraintSettings.Contact();
        public void Validate()
        {
            if(!enabled)return;
            if(double.IsNaN(timestep)||double.IsInfinity(timestep)||timestep<=0||double.IsNaN(tolerance)||double.IsInfinity(tolerance)||tolerance<0||double.IsNaN(impratio)||double.IsInfinity(impratio)||impratio<=0||iterations<1||noslip_iterations<0)
                throw new InvalidDataException("全局求解参数无效。");
            if(equality==null||contact==null)throw new InvalidDataException("缺少默认约束参数。");
            equality.Validate(false);contact.Validate(true);
        }
    }
    public sealed class Attachment
    {
        [Browsable(false)] public string link_id { get; set; }
        [Browsable(false)] public string id { get; set; } = Guid.NewGuid().ToString("N");
        public string name { get; set; } = "site";
        public string link { get; set; }
        public string type { get; set; }
        public string source_name { get; set; }
        [Browsable(false)] public string source_pid { get; set; }
        [Browsable(false)] public string component_pid { get; set; }
        [Browsable(false)] public string component_name { get; set; }
        [Browsable(false)] public CollisionReference reference { get; set; }
        public override string ToString()=>name+" ["+type+"]";
    }

    public sealed class ActuatorConfig
    {
        [Browsable(false)] public string id { get; set; }
        [Browsable(false)] public string joint_id { get; set; }
        public string name { get; set; } = "actuator";
        public string joint { get; set; }
        public string type { get; set; } = "motor";
        public double gear { get; set; } = 1;
        public double gain { get; set; } = 1;
        public double ctrl_min { get; set; } = -1;
        public double ctrl_max { get; set; } = 1;
        public double force_min { get; set; } = -10;
        public double force_max { get; set; } = 10;
        public override string ToString() => name + " → " + joint;
    }

    public sealed class SensorConfig
    {
        [Browsable(false)] public string id { get; set; }
        public string name { get; set; } = "sensor";
        [Browsable(false)] public string site_id { get; set; }
        public string site { get; set; }
        public string type { get; set; } = "imu";
        public double noise { get; set; }
        public double cutoff { get; set; }
        public double fovy { get; set; } = 45;
        public override string ToString() => name + " → " + site;
    }

    public sealed class EqualityConfig
    {
        [Browsable(false)] public string id { get; set; }
        [Browsable(false)] public string joint1_id { get; set; }
        [Browsable(false)] public string joint2_id { get; set; }
        [Browsable(false)] public string body1_id { get; set; }
        [Browsable(false)] public string body2_id { get; set; }
        public string name { get; set; } = "closure";
        [Browsable(false)] public string site1_id { get; set; }
        [Browsable(false)] public string site2_id { get; set; }
        public string site1 { get; set; }
        public string site2 { get; set; }
        public string type { get; set; } = "connect";
        public ConstraintSettings solver { get; set; }
        public bool active { get; set; } = true;
        public string binding { get; set; } = "site";
        public string body1 { get; set; }
        public string body2 { get; set; }
        public string joint1 { get; set; }
        public string joint2 { get; set; }
        public double[] polycoef { get; set; } = new double[]{0,1,0,0,0};
        public double torquescale { get; set; } = 1;
        public string pose_mode { get; set; } = "inherit";
        public double[] position { get; set; } = new double[3];
        public double[] orientation { get; set; } = new double[3];
        public double[] anchor { get; set; } = new double[3];
        public void ValidateParameters(){
            if(!new[]{"connect","weld","joint"}.Contains(type)||!new[]{"site","body"}.Contains(binding)||!new[]{"inherit","custom"}.Contains(pose_mode))throw new InvalidDataException("约束类型或绑定方式无效。");
            var arrays=type=="joint"?new[]{polycoef}:binding=="body"?(type=="connect"?new[]{anchor}:pose_mode=="custom"?new[]{position,orientation}:new double[0][]):new double[0][];
            foreach(var array in arrays)if(array==null||array.Length!=(type=="joint"?5:3)||array.Any(v=>double.IsNaN(v)||double.IsInfinity(v)))throw new InvalidDataException("约束系数与位姿需要完整的有限数字。");
            if(type=="weld"&&(double.IsNaN(torquescale)||double.IsInfinity(torquescale)||torquescale<0))throw new InvalidDataException("torquescale 必须为非负有限数字。");solver?.Validate(false);
        }
        public override string ToString() => name + " ["+type+"] : " + (type=="joint"?joint1:binding=="body"?body1:site1) + " ↔ " + (type=="joint"?joint2:binding=="body"?body2:site2);
    }

    public sealed class SiteForceConfig
    {
        [Browsable(false)] public string id { get; set; }
        public string name { get; set; } = "force";
        public string type { get; set; } = "pull";
        public bool enabled { get; set; } = true;
        public double stiffness { get; set; }
        public double damping { get; set; }
        public string length_mode { get; set; } = "initial";
        public double rest_length { get; set; } = .1;
        public double magnitude { get; set; }
        public string site1 { get; set; }
        public string site2 { get; set; }
        [Browsable(false)] public string site1_id { get; set; }
        [Browsable(false)] public string site2_id { get; set; }
        public void Validate(){
            if(string.IsNullOrWhiteSpace(name)||!new[]{"pull","push","spring"}.Contains(type))throw new InvalidDataException("两点作用力名称或类型无效。");
            var values=type=="spring"?new[]{stiffness,damping}:new[]{magnitude};
            if(values.Any(v=>double.IsNaN(v)||double.IsInfinity(v)||v<0))throw new InvalidDataException("力值、弹簧刚度和阻尼须为非负有限数字。");
            if(type=="spring"&&(!new[]{"initial","custom"}.Contains(length_mode)||(length_mode=="custom"&&(double.IsNaN(rest_length)||double.IsInfinity(rest_length)||rest_length<0))))throw new InvalidDataException("请选择自然长度方式，自定义自然长度须为非负有限数字 m。");
        }
        public override string ToString()=>name+" ["+type+"] : "+site1+" ↔ "+site2;
    }

    public sealed class UrdfExportSettings {
        public bool inertia {get;set;}=true;
        public bool geometry {get;set;}=true;
        public bool kinematics {get;set;}=true;
        // Explicit tree limits have priority; users may opt into CAD mate limits.
        public bool limits {get;set;}
    }
    public sealed class SimulationProject
    {
        public int schema_version { get; set; } = 1;
        public string assembly { get; set; }
        public string configuration { get; set; }
        public string python { get; set; } = "python";
        public List<Attachment> attachments { get; set; } = new List<Attachment>();
        public List<ActuatorConfig> actuators { get; set; } = new List<ActuatorConfig>();
        public List<SensorConfig> sensors { get; set; } = new List<SensorConfig>();
        public List<SiteForceConfig> site_forces { get; set; } = new List<SiteForceConfig>();
        public List<EqualityConfig> equalities { get; set; } = new List<EqualityConfig>();
        public CollisionConfiguration collision { get; set; }
        public SolverSettings solver { get; set; }
        public List<JointConfiguration> joints { get; set; } = new List<JointConfiguration>();
        public List<JointForceLimit> joint_force_limits { get; set; } = new List<JointForceLimit>();
        public UrdfExportSettings urdf_export {get;set;}
        public string base_mode { get; set; } = "inherit";
        public bool joint_defaults { get; set; } = true;
        // Deletion plans change collections only; record values remain untouched until a plan is accepted.
        public SimulationProject CopyCollections(){
            var p=(SimulationProject)MemberwiseClone();
            p.attachments=new List<Attachment>(attachments);p.actuators=new List<ActuatorConfig>(actuators);p.sensors=new List<SensorConfig>(sensors);
            p.equalities=new List<EqualityConfig>(equalities);p.site_forces=new List<SiteForceConfig>(site_forces);p.joints=new List<JointConfiguration>(joints);p.joint_force_limits=new List<JointForceLimit>(joint_force_limits);
            if(collision!=null)p.collision=new CollisionConfiguration{disable_internal=collision.disable_internal,link_modes_migrated=collision.link_modes_migrated,link_modes=new Dictionary<string,string>(collision.link_modes),link_modes_by_id=new Dictionary<string,string>(collision.link_modes_by_id),geometries=new List<CollisionGeometry>(collision.geometries),allowed_pairs=new List<CollisionPair>(collision.allowed_pairs)};
            return p;
        }

        public void NormalizeSiteReferences(){
            ConfigurationIdentityValidation.Validate(this);
            var ids=new HashSet<string>();foreach(var a in attachments){if(string.IsNullOrWhiteSpace(a.id))a.id=Guid.NewGuid().ToString("N");if(!ids.Add(a.id))throw new InvalidDataException("site 内部 ID 重复。");}
            if(site_forces==null)throw new InvalidDataException("两点作用力配置不完整。");
            foreach(var force in site_forces){var a=FindSite(force.site1_id,force.site1);var b=FindSite(force.site2_id,force.site2);if(a!=null){force.site1_id=a.id;force.site1=a.name;}else if(string.IsNullOrWhiteSpace(force.site1_id)&&!string.IsNullOrWhiteSpace(force.site1))force.site1_id=Guid.NewGuid().ToString("N");if(b!=null){force.site2_id=b.id;force.site2=b.name;}else if(string.IsNullOrWhiteSpace(force.site2_id)&&!string.IsNullOrWhiteSpace(force.site2))force.site2_id=Guid.NewGuid().ToString("N");}
            foreach(var sensor in sensors){var a=FindSite(sensor.site_id,sensor.site);if(a!=null){sensor.site_id=a.id;sensor.site=a.name;}else if(string.IsNullOrWhiteSpace(sensor.site_id)&&!string.IsNullOrWhiteSpace(sensor.site))sensor.site_id=Guid.NewGuid().ToString("N");}
            foreach(var e in equalities.Where(e=>e.type!="joint"&&e.binding=="site")){var a=FindSite(e.site1_id,e.site1);var b=FindSite(e.site2_id,e.site2);if(a!=null){e.site1_id=a.id;e.site1=a.name;}else if(string.IsNullOrWhiteSpace(e.site1_id)&&!string.IsNullOrWhiteSpace(e.site1))e.site1_id=Guid.NewGuid().ToString("N");if(b!=null){e.site2_id=b.id;e.site2=b.name;}else if(string.IsNullOrWhiteSpace(e.site2_id)&&!string.IsNullOrWhiteSpace(e.site2))e.site2_id=Guid.NewGuid().ToString("N");}
        }
        Attachment FindSite(string id,string name){if(!string.IsNullOrEmpty(id)){var item=attachments.FirstOrDefault(a=>a.id==id);return item;}var matches=attachments.Where(a=>a.name==name).ToArray();if(matches.Length>1)throw new InvalidDataException("site 名称重复："+name);return matches.FirstOrDefault();}
        public void RenameSite(Attachment item,string name){name=(name??"").Trim();if(string.IsNullOrWhiteSpace(name)||attachments.Any(a=>a!=item&&a.name==name))throw new InvalidDataException("site 名称不能为空或重复。");NormalizeSiteReferences();item.name=name;NormalizeSiteReferences();}
        public void ValidateSolver()
        {
            if(site_forces==null)throw new InvalidDataException("两点作用力配置不完整。");
            foreach(var force in site_forces)force.Validate();if(site_forces.Select(f=>f.name).Distinct().Count()!=site_forces.Count)throw new InvalidDataException("两点作用力名称重复。");
            solver?.Validate();
            if(joints==null||joint_force_limits==null||!new[]{"inherit","fixed","floating"}.Contains(base_mode))throw new InvalidDataException("关节配置不完整。");
            foreach(var j in joints)j.Validate();foreach(var f in joint_force_limits)f.Validate();
            if(joints.Select(j=>j.joint).Distinct().Count()!=joints.Count||joint_force_limits.Select(j=>j.joint).Distinct().Count()!=joint_force_limits.Count)throw new InvalidDataException("关节配置重复。");
            foreach(var equality in equalities)equality.ValidateParameters();
            if(collision!=null)foreach(var pair in collision.allowed_pairs)pair.solver?.Validate(true);
        }

    }
}
