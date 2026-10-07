using System;
using System.IO;
#if NET8_0_OR_GREATER
using JavaScriptSerializer = SWSimTool.Persistence.PortableJsonSerializer;
#else
using System.Web.Script.Serialization;
#endif
namespace SWSimTool.Simulation { public static class SimulationProjectSerializer {
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

}}
