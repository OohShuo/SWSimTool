using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Web.Script.Serialization;

namespace SW2URDF.Simulation
{
    public sealed class Attachment
    {
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
        public string name { get; set; } = "sensor";
        public string site { get; set; }
        public string type { get; set; } = "imu";
        public double noise { get; set; }
        public double cutoff { get; set; }
        public double fovy { get; set; } = 45;
        public override string ToString() => name + " → " + site;
    }

    public sealed class EqualityConfig
    {
        public string name { get; set; } = "closure";
        public string site1 { get; set; }
        public string site2 { get; set; }
        public string type { get; set; } = "connect";
        public override string ToString() => name + " : " + site1 + " ↔ " + site2;
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
        public List<EqualityConfig> equalities { get; set; } = new List<EqualityConfig>();
        public CollisionConfiguration collision { get; set; }

        public static SimulationProject Load(string path)
        {
            var p = File.Exists(path) ? new JavaScriptSerializer().Deserialize<SimulationProject>(File.ReadAllText(path)) : new SimulationProject();
            if (p == null || p.schema_version != 1 || p.attachments == null || p.actuators == null || p.sensors == null || p.equalities == null)
                throw new InvalidDataException("Unsupported or incomplete simulation project: " + path);
            return p;
        }

        public static void Write(string path, object value)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(value), new System.Text.UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
