using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using SWSimTool.RobotModel;

namespace SWSimTool.Simulation
{
    public static class NativeBackend
    {
        static string Hash(string path) {using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        public static async Task<int> PreviewAsync(string python,string xml,Action<string> report,CancellationToken cancellation=default(CancellationToken)) {
            var result=await new PythonToolBackend().PreviewAsync(new ModelToolRequest(new ToolContext(python,null,Timeout.InfiniteTimeSpan,cancellation,report),xml)).ConfigureAwait(false);
            if(!result.Success)NativeExportPipeline.ReportFailure(result,report);return result.ExitCode;
        }
        public static SWSimTool.RobotModel.RobotModel LoadLocal(string urdf,string jsonPath)
        {
            var serializer=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
            var json=string.IsNullOrWhiteSpace(jsonPath)?"{\"collision\":{\"disable_internal\":true}}":File.ReadAllText(jsonPath);
            var config=serializer.Deserialize<Dictionary<string,object>>(json);object name,hash,product;
            if(!string.IsNullOrWhiteSpace(jsonPath)&&(!config.TryGetValue("product",out product)||Convert.ToString(product)!="SWSimTool"))throw new InvalidDataException("Only SWSimTool sidecar files are supported. Export the configuration from the current plugin.");
            if(config.TryGetValue("urdf",out name)&&Convert.ToString(name)!=Path.GetFileName(urdf))throw new InvalidDataException("Sidecar belongs to a different URDF");
            if(config.TryGetValue("urdf_sha256",out hash)&&!string.IsNullOrEmpty(Convert.ToString(hash))&&!string.Equals(Convert.ToString(hash),Hash(urdf),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("URDF changed since sidecar export; re-export the pair");
            var core=LegacySimulationConfigImporter.IdentifyImportedCore(UrdfRobotModelImporter.Load(urdf),json);
            return new SWSimTool.RobotModel.RobotModel(core,LegacySimulationConfigImporter.Import(json,core));
        }
        public static Task<int> RunAsync(string python,SWSimTool.RobotModel.RobotModel model,string output,bool preview,Action<string> report,string meshSettingsPath=null,string exportId=null,CancellationToken cancellation=default(CancellationToken))
            => RunWithServices(python,model,output,preview,report,meshSettingsPath,exportId,cancellation);
        public static Task<int> RunWithServices(string python,SWSimTool.RobotModel.RobotModel model,string output,bool preview,Action<string> report,string meshSettingsPath=null,string exportId=null,CancellationToken cancellation=default(CancellationToken),IMeshPreparationService meshService=null,IMuJoCoValidationService validationService=null,IPreviewService previewService=null)
        {
            var backend=new PythonToolBackend();var storage=new NativeExportStorage();
            return NativeExportPipeline.RunAsync(python,model,output,preview,report,meshSettingsPath,exportId,cancellation,meshService??backend,validationService??backend,previewService??backend,storage,storage,new NativeMjcfWriter(),
                value=>report?.Invoke("Native export metrics: "+new JavaScriptSerializer().Serialize(new{export_id=value.ExportId,counts=value.Counts})));
        }
    }
}
