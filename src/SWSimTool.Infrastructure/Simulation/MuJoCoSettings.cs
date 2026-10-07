using System;
using System.IO;
using System.Web.Script.Serialization;
namespace SWSimTool.Simulation
{
    public sealed class MuJoCoSettings
    {
        public string Python { get; set; } = "python";
        public string Urdf { get; set; } = "";
        public string Sidecar { get; set; } = "";
        public string Output { get; set; } = "";
        public string ExistingMjcf { get; set; } = "";
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SWSimTool", "mujoco-tools.json"); } }
        public static MuJoCoSettings Load(string path)
        {
            if (!File.Exists(path)) return new MuJoCoSettings();
            return new JavaScriptSerializer().Deserialize<MuJoCoSettings>(File.ReadAllText(path)) ?? new MuJoCoSettings();
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
