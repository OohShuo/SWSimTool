using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using SW2URDF.RobotModel;

namespace SW2URDF.Simulation
{
    internal static class NativeToolSteps
    {
        internal static string Support=>Path.Combine(Path.GetDirectoryName(typeof(NativeToolSteps).Assembly.Location),"mujoco_backend","native_support.py");
        internal static int Prepare(string python,NativeAssetPlan plan,NativeExportWorkspace workspace,string settings,Action<string> report,string exportId,out Dictionary<string,object> counts)
        {
            counts=null;var serializer=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
            var result=Path.Combine(workspace.Work,"prepared.json");var manifest=Path.Combine(workspace.Work,"assets.json");
            File.WriteAllText(manifest,serializer.Serialize(plan.Manifest(workspace.Staging,result)),new UTF8Encoding(false));
            var preferences=settings??MeshExportSettings.DefaultPath;
            var args="--prepare "+BackendProcess.Quote(manifest)+(File.Exists(preferences)?" --mesh-settings "+BackendProcess.Quote(preferences):"");
            var code=BackendProcess.Run(python,Support,args,report,exportId);
            if(code==0)counts=(Dictionary<string,object>)serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(result))["counts"];
            return code;
        }
        internal static int Validate(string python,string xml,Action<string> report,string exportId)=>BackendProcess.Run(python,Support,"--validate "+BackendProcess.Quote(xml),report,exportId);
        internal static int Preview(string python,string xml,Action<string> report,string exportId)=>BackendProcess.Run(python,Support,"--preview "+BackendProcess.Quote(xml),report,exportId);
    }
    internal static class NativeExportPipeline
    {
        static readonly object publishGate=new object();
        internal static Task<int> RunAsync(string python,SW2URDF.RobotModel.RobotModel model,string output,bool preview,Action<string> report,string meshSettingsPath,string exportId)
        {
            return Task.Run(()=>{
                output=PythonBackend.PackageOutput(model.Core.Name+".urdf",output);
                if(!string.Equals(Path.GetExtension(output),".xml",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("MJCF output must have an .xml extension");
                var root=Path.GetDirectoryName(Path.GetFullPath(output));
                using(var workspace=new NativeExportWorkspace(Path.GetDirectoryName(root))) {
                    var plan=NativeAssetPlanner.Create(model,root);
                    var xml=MjcfExporter.Generate(model,new PreparedAssets(plan.Assets),new ExportContext(model.Core.Name));
                    Dictionary<string,object> counts;
                    if(NativeToolSteps.Prepare(python,plan,workspace,meshSettingsPath,report,exportId,out counts)!=0)return 1;
                    var stagedXml=Path.Combine(workspace.Staging,Path.GetFileName(output));
                    File.WriteAllText(stagedXml,xml,new UTF8Encoding(false));counts["mjcf_generation"]=1;
                    if(NativeToolSteps.Validate(python,stagedXml,report,exportId)!=0)return 1;
                    counts["mujoco_validation"]=1;plan.VerifySources();
                    lock(publishGate)PackagePublisher.Publish(workspace.Staging,output);
                    report?.Invoke("Native export metrics: "+new JavaScriptSerializer().Serialize(new{export_id=exportId,counts}));
                    report?.Invoke("MJCF package saved: "+output);
                    return preview?NativeToolSteps.Preview(python,output,report,exportId):0;
                }
            });
        }
    }
}
