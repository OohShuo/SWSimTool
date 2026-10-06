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
        static readonly object publishGate=new object();
        static string Support => Path.Combine(Path.GetDirectoryName(typeof(NativeBackend).Assembly.Location),"mujoco_backend","native_support.py");
        static string Hash(string path) {using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
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
        {
            return Task.Run(()=>{
                output=PythonBackend.PackageOutput(model.Core.Name+".urdf",output);
                if(!string.Equals(Path.GetExtension(output),".xml",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("MJCF output must have an .xml extension");
                var root=Path.GetDirectoryName(Path.GetFullPath(output));var parent=Path.GetDirectoryName(root);
                Directory.CreateDirectory(parent);
                var workspace=Path.Combine(parent,".sw2mujoco-native-"+Guid.NewGuid().ToString("N"));
                var staging=Path.Combine(parent,".sw2mujoco-stage-"+Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workspace);Directory.CreateDirectory(staging);
                try {
                    var meshes=model.Core.Links.SelectMany(l=>l.Geometries).Where(g=>g.Mesh!=null).Select(g=>g.Mesh).GroupBy(m=>m.Id).Select(g=>g.First()).ToArray();
                    foreach(var mesh in meshes)if(Path.GetFullPath(mesh.SourcePath).StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Output package must not contain original source meshes");
                    var hashes=meshes.GroupBy(m=>m.SourcePath,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>Hash(g.Key),StringComparer.OrdinalIgnoreCase);
                    var assets=meshes.Select((m,index)=>new PreparedMeshAsset(m.Id,"mesh_"+index.ToString("D4"),"meshes/mesh_"+index.ToString("D4")+".stl")).ToArray();
                    // Check all typed references and writer invariants before expensive preparation.
                    string xml=MjcfExporter.Generate(model,new PreparedAssets(assets),new ExportContext(model.Core.Name));
                    var resultFile=Path.Combine(workspace,"prepared.json");var manifest=Path.Combine(workspace,"assets.json");
                    var serializer=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
                    File.WriteAllText(manifest,serializer.Serialize(new{staging,result=resultFile,meshes=meshes.Select((m,index)=>new{id=m.Id,source=m.SourcePath,relative=assets[index].RelativePath,sha256=hashes[m.SourcePath]}).ToArray()}),new UTF8Encoding(false));
                    var preferences=meshSettingsPath??MeshExportSettings.DefaultPath;
                    string arguments="--prepare "+BackendProcess.Quote(manifest)+(File.Exists(preferences)?" --mesh-settings "+BackendProcess.Quote(preferences):"");
                    if(BackendProcess.Run(python,Support,arguments,report,exportId)!=0)return 1;
                    var prepared=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(resultFile));
                    var counts=(Dictionary<string,object>)prepared["counts"];
                    string stagedXml=Path.Combine(staging,Path.GetFileName(output));
                    File.WriteAllText(stagedXml,xml,new UTF8Encoding(false));
                    counts["mjcf_generation"]=1;
                    if(BackendProcess.Run(python,Support,"--validate "+BackendProcess.Quote(stagedXml),report,exportId)!=0)return 1;
                    counts["mujoco_validation"]=1;
                    foreach(var item in hashes)if(Hash(item.Key)!=item.Value)throw new IOException("Source mesh changed during export: "+item.Key);
                    lock(publishGate)PackagePublisher.Publish(staging,output);
                    report?.Invoke("Native export metrics: "+serializer.Serialize(new{export_id=exportId,counts}));
                    report?.Invoke("MJCF package saved: "+output);
                    if(preview)return BackendProcess.Run(python,Support,"--preview "+BackendProcess.Quote(output),report,exportId);
                    return 0;
                } finally {
                    foreach(var directory in new[]{workspace,staging})if(Directory.Exists(directory))Directory.Delete(directory,true);
                }
            });
        }
    }
}
