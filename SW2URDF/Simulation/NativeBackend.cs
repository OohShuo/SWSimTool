using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using SW2URDF.RobotModel;

namespace SW2URDF.Simulation
{
    public static class NativeBackend
    {
        static string Support => Path.Combine(Path.GetDirectoryName(typeof(NativeBackend).Assembly.Location),"mujoco_backend","native_support.py");
        static string Hash(string path) {using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        public static Task<int> PreviewAsync(string python,string xml,Action<string> report) => Task.Run(()=>NativeToolSteps.Preview(python,Path.GetFullPath(xml),report,null));
        public static SW2URDF.RobotModel.RobotModel LoadLocal(string urdf,string jsonPath)
        {
            var serializer=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
            var json=string.IsNullOrWhiteSpace(jsonPath)?"{\"collision\":{\"disable_internal\":true}}":File.ReadAllText(jsonPath);
            var config=serializer.Deserialize<Dictionary<string,object>>(json);object name,hash;
            if(config.TryGetValue("urdf",out name)&&Convert.ToString(name)!=Path.GetFileName(urdf))throw new InvalidDataException("Sidecar belongs to a different URDF");
            if(config.TryGetValue("urdf_sha256",out hash)&&!string.IsNullOrEmpty(Convert.ToString(hash))&&!string.Equals(Convert.ToString(hash),Hash(urdf),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("URDF changed since sidecar export; re-export the pair");
            var core=LegacySimulationConfigImporter.IdentifyImportedCore(UrdfRobotModelImporter.Load(urdf),json);
            return new SW2URDF.RobotModel.RobotModel(core,LegacySimulationConfigImporter.Import(json,core));
        }
        public static Task<int> RunAsync(string python,SW2URDF.RobotModel.RobotModel model,string output,bool preview,Action<string> report,string meshSettingsPath=null,string exportId=null)
            => NativeExportPipeline.RunAsync(python,model,output,preview,report,meshSettingsPath,exportId);
    }
}
