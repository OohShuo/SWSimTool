using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using SWSimTool.RobotModel;

namespace SWSimTool.Simulation
{
    public static class NativeExportPipeline
    {
        static readonly object publishGate=new object();
        public static Task<int> RunAsync(string python,SWSimTool.RobotModel.RobotModel model,string output,bool preview,Action<string> report,string meshSettingsPath,string exportId,CancellationToken cancellation=default(CancellationToken),IMeshPreparationService meshService=null,IMuJoCoValidationService validationService=null,IPreviewService previewService=null,IAssetStore assets=null,IPackageStore packages=null,IMjcfWriter writer=null,Action<ExportMetrics> metrics=null,Action<Action> publication=null)
        {
            return Task.Run(async()=>{
                if(meshService==null||validationService==null||previewService==null||assets==null||packages==null||writer==null)throw new ArgumentNullException("Export services must be explicitly supplied by the composition root");
                if(cancellation.IsCancellationRequested){ReportFailure(ToolResult.Failure(ToolFailure.Cancelled,"Export cancelled"),report);return 1;}
                output=ExportOutputPaths.PackageOutput(model.Core.Name+".urdf",output);
                if(!string.Equals(Path.GetExtension(output),".xml",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("MJCF output must have an .xml extension");
                var root=Path.GetDirectoryName(Path.GetFullPath(output));
                using(var workspace=packages.CreateWorkspace(Path.GetDirectoryName(root))) {
                    var plan=assets.Plan(model,root);
                    var xml=writer.Generate(model,new PreparedAssets(plan.Assets),new ExportContext(model.Core.Name));
                    var meshRequest=new MeshPreparationRequest(new ToolContext(python,exportId,TimeSpan.FromMinutes(10),cancellation,report),workspace.Work,workspace.Staging,meshSettingsPath,
                        plan.Sources.Select((m,i)=>new MeshToolInput(m.Id,m.SourcePath,plan.Assets[i].RelativePath,assets.Hash(m.SourcePath))));
                    var prepared=await meshService.PrepareAsync(meshRequest).ConfigureAwait(false);
                    if(!prepared.Success){ReportFailure(prepared,report);return 1;}
                    var counts=prepared.Counts.ToDictionary(p=>p.Key,p=>p.Value);
                    var stagedXml=Path.Combine(workspace.Staging,Path.GetFileName(output));
                    assets.WriteUtf8(stagedXml,xml);counts["mjcf_generation"]=1;
                    var preparedHashes=plan.Assets.ToDictionary(a=>Path.Combine(workspace.Staging,a.RelativePath),a=>assets.Hash(Path.Combine(workspace.Staging,a.RelativePath)));
                    var validation=await validationService.ValidateAsync(new ModelToolRequest(new ToolContext(python,exportId,TimeSpan.FromMinutes(2),cancellation,report),stagedXml)).ConfigureAwait(false);
                    if(!validation.Success){ReportFailure(validation,report);return 1;}
                    if(preparedHashes.Any(p=>!assets.Exists(p.Key)||assets.Hash(p.Key)!=p.Value)){ReportFailure(ToolResult.Failure(ToolFailure.InputChanged,"Validation modified prepared meshes"),report);return 1;}
                    counts["mujoco_validation"]=1;plan.VerifySources();
                    lock(publishGate){if(cancellation.IsCancellationRequested){ReportFailure(ToolResult.Failure(ToolFailure.Cancelled,"Export cancelled before publication"),report);return 1;}Action publish=()=>{cancellation.ThrowIfCancellationRequested();packages.Publish(workspace.Staging,output);};if(publication==null)publish();else publication(publish);}
                    metrics?.Invoke(new ExportMetrics(exportId,counts));
                    report?.Invoke("MJCF package saved: "+output);
                    if(!preview)return 0;
                    var shown=await previewService.PreviewAsync(new ModelToolRequest(new ToolContext(python,exportId,Timeout.InfiniteTimeSpan,cancellation,report),output)).ConfigureAwait(false);
                    if(!shown.Success)ReportFailure(shown,report);return shown.ExitCode;
                }
            });
        }
        internal static void ReportFailure(ToolResult result,Action<string> report)=>report?.Invoke("Tool failed ["+result.FailureKind+"]: "+result.Diagnostic);
    }
}
