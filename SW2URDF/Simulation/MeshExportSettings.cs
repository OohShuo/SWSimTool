using System;
using System.IO;
using System.Web.Script.Serialization;
namespace SW2URDF.Simulation
{
    public sealed class MeshExportSettings
    {
        public bool Enabled { get; set; }
        public int MaximumTriangles { get; set; } = 100000;
        public string Backend { get; set; } = "pymeshlab";
        public string Python { get; set; } = "python";
        public string Blender { get; set; } = "";
        public static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SW2URDF", "mesh-export.json"); } }
        public static MeshExportSettings Load(string path)
        {
            if (!File.Exists(path)) return new MeshExportSettings();
            return new JavaScriptSerializer().Deserialize<MeshExportSettings>(File.ReadAllText(path)) ?? new MeshExportSettings();
        }
        public void Validate()
        {
            if (!Enabled) return;
            if (MaximumTriangles < 4 || MaximumTriangles > 200000) throw new ArgumentException("每个 STL 的三角形上限必须在 4 到 200,000 之间。");
            if (Backend != "pymeshlab" && Backend != "fast-simplification" && Backend != "blender") throw new ArgumentException("请选择有效的减面后端。");
            if (Backend == "blender" && !File.Exists(Blender)) throw new FileNotFoundException("请选择有效的 blender.exe 路径。");
        }
        public void Save(string path)
        {
            Validate();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
