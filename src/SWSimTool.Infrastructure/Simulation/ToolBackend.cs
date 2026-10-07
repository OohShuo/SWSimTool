using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using JavaScriptSerializer = SWSimTool.Persistence.PortableJsonSerializer;
#else
using System.Web.Script.Serialization;
#endif

namespace SWSimTool.Simulation
{
    // Only paths, mesh-processing preferences and tool context cross this boundary.
    // No RobotModel, CAD object, URDF conversion or simulation semantics here.
    public sealed class PythonToolBackend:IMeshPreparationService,IMuJoCoValidationService,IPreviewService
    {
        readonly string script;
        public PythonToolBackend(string supportScript=null){script=supportScript??Path.Combine(Path.GetDirectoryName(typeof(PythonToolBackend).Assembly.Location),"mujoco_backend","native_support.py");}
        ToolResult Execute(ToolContext context,string arguments)
        {
            try{
                if(Path.IsPathRooted(context.Python)&&!File.Exists(context.Python))return ToolResult.Failure(ToolFailure.EnvironmentMissing,"Local Python is missing: "+context.Python);
                var result=BackendProcess.Execute(context.Python,script,arguments,context.Report,context.ExportId,context.Cancellation,context.Timeout);
                if(result.FailureKind!=ToolFailure.Exit)return result;
                var line=result.Diagnostic.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).LastOrDefault(x=>x.StartsWith("SWSIMTOOL_TOOL_ERROR:",StringComparison.Ordinal));
                if(line==null)return new ToolResult(result.ExitCode,ToolFailure.ToolFailed,result.Diagnostic);
                Dictionary<string,object> data;
                try{data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line.Substring("SWSIMTOOL_TOOL_ERROR:".Length));if(data==null||!data.ContainsKey("category"))throw new InvalidDataException();}
                catch{return ToolResult.Failure(ToolFailure.Protocol,"Malformed structured tool error: "+result.Diagnostic);}
                ToolFailure category;
                if(!Enum.TryParse(Convert.ToString(data["category"]),out category)||!new[]{ToolFailure.EnvironmentMissing,ToolFailure.DependencyMissing,ToolFailure.InvalidInput,ToolFailure.InputChanged,ToolFailure.BudgetExceeded,ToolFailure.ToolFailed,ToolFailure.ValidationFailed}.Contains(category))return ToolResult.Failure(ToolFailure.Protocol,"Invalid tool error category");
                return new ToolResult(result.ExitCode,category,result.Diagnostic);
            }
            catch(Exception error){return ToolResult.Failure(ToolFailure.Start,error.Message);}
        }
        public Task<MeshPreparationResult> PrepareAsync(MeshPreparationRequest request)=>Task.Run(()=>{
            try {
                if(request.Context.Cancellation.IsCancellationRequested)return new MeshPreparationResult(ToolResult.Failure(ToolFailure.Cancelled,"Tool cancelled before start"));
                var serializer=new JavaScriptSerializer{MaxJsonLength=64*1024*1024};
                var result=Path.Combine(request.Work,"prepared-"+Guid.NewGuid().ToString("N")+".json");
                var manifest=Path.Combine(request.Work,"assets-"+Guid.NewGuid().ToString("N")+".json");
                // Callers own the workspace. Service owns only these two protocol files.
                try {
                    var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var ids=new HashSet<string>();
                    foreach(var mesh in request.Meshes){
                        if(string.IsNullOrWhiteSpace(mesh.Id)||!ids.Add(mesh.Id)||!seen.Add(mesh.Relative))throw new InvalidDataException("Duplicate asset identity/path");
                        var target=AssetPath(request.Staging,mesh.Relative);
                        if(mesh.Source.StartsWith(request.Staging.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Source cannot be inside staging");
                        if(NativeAssetPlanner.Hash(mesh.Source)!=mesh.Sha256)return new MeshPreparationResult(ToolResult.Failure(ToolFailure.InputChanged,"Source mesh changed"));
                    }
                    File.WriteAllText(manifest,serializer.Serialize(new{staging=request.Staging,result,meshes=request.Meshes.Select(m=>new{id=m.Id,source=m.Source,relative=m.Relative,sha256=m.Sha256}).ToArray()}),new UTF8Encoding(false));
                    var settings=request.Settings??MeshExportSettings.DefaultPath;
                    var settingsHash=File.Exists(settings)?NativeAssetPlanner.Hash(settings):null;
                    MeshExportSettings preferences;
                    try{preferences=MeshExportSettings.Load(settings);preferences.Validate();}
                    catch(FileNotFoundException error){return new MeshPreparationResult(ToolResult.Failure(ToolFailure.DependencyMissing,error.Message));}
                    catch(ArgumentException error){return new MeshPreparationResult(ToolResult.Failure(ToolFailure.InvalidInput,error.Message));}
                    var budget=preferences.Enabled?preferences.MaximumTriangles:200000;
                    var run=Execute(request.Context,"--prepare "+BackendProcess.Quote(manifest)+(settingsHash==null?"":" --mesh-settings "+BackendProcess.Quote(settings)));
                    if(!run.Success)return new MeshPreparationResult(run);
                    if((settingsHash!=null&&(!File.Exists(settings)||NativeAssetPlanner.Hash(settings)!=settingsHash))||request.Meshes.Any(m=>NativeAssetPlanner.Hash(m.Source)!=m.Sha256))
                        return new MeshPreparationResult(ToolResult.Failure(ToolFailure.InputChanged,"Mesh/settings changed during preparation"));
                    var response=serializer.Deserialize<Dictionary<string,object>>(File.ReadAllText(result));
                    var assets=((IEnumerable)response["assets"]).Cast<Dictionary<string,object>>().ToArray();
                    if(assets.Length!=request.Meshes.Count)throw new InvalidDataException("Asset result count mismatch");
                    var found=new HashSet<string>();
                    foreach(var asset in assets){
                        var id=Convert.ToString(asset["id"]);var expected=request.Meshes.SingleOrDefault(m=>m.Id==id);
                        if(expected==null||!found.Add(id)||Convert.ToString(asset["relative"])!=expected.Relative)throw new InvalidDataException("Prepared asset identity/path mismatch");
                        var path=AssetPath(request.Staging,expected.Relative);
                        if(NativeAssetPlanner.Hash(path)!=Convert.ToString(asset["sha256"]))throw new InvalidDataException("Prepared asset hash mismatch");
                        using(var stream=File.OpenRead(path))using(var reader=new BinaryReader(stream)){
                            if(stream.Length<84)throw new InvalidDataException("Prepared STL is not binary");
                            stream.Position=80;var triangles=reader.ReadUInt32();
                            if(triangles>budget)return new MeshPreparationResult(ToolResult.Failure(ToolFailure.BudgetExceeded,"Prepared STL exceeds requested triangle budget"));
                            if(triangles<1||stream.Length!=84L+50L*triangles)throw new InvalidDataException("Prepared STL has invalid length");
                        }
                    }
                    var counts=((Dictionary<string,object>)response["counts"]).ToDictionary(p=>p.Key,p=>StrictCount(p.Value));
                    if(!counts.ContainsKey("mesh_simplification")||!counts.ContainsKey("mesh_prepare"))throw new InvalidDataException("Missing preparation counters");
                    return new MeshPreparationResult(run,counts);
                }finally{foreach(var path in new[]{manifest,result})if(File.Exists(path))File.Delete(path);}
            }catch(Exception error){return new MeshPreparationResult(ToolResult.Failure(ToolFailure.Protocol,error.Message));}
        });
        static int StrictCount(object value){if(!(value is int)||((int)value)<0)throw new InvalidDataException("Invalid tool counter");return (int)value;}
        static string AssetPath(string staging,string relative){var path=Path.GetFullPath(Path.Combine(staging,relative));if(Path.IsPathRooted(relative)||!path.StartsWith(staging.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||Path.GetExtension(path).ToLowerInvariant()!=".stl")throw new InvalidDataException("Invalid prepared asset path");return path;}
        Task<ToolResult> ModelOperation(ModelToolRequest request,string operation)=>Task.Run(()=>{
            try{
                if(request.Context.Cancellation.IsCancellationRequested)return ToolResult.Failure(ToolFailure.Cancelled,"Tool cancelled before start");
                // Official compile/preview must never silently repair candidate XML.
                var before=NativeAssetPlanner.Hash(request.Xml);var result=Execute(request.Context,operation+" "+BackendProcess.Quote(request.Xml));
                if(!File.Exists(request.Xml)||NativeAssetPlanner.Hash(request.Xml)!=before)return ToolResult.Failure(ToolFailure.InputChanged,"Tool modified candidate XML");
                return result;
            }catch(FileNotFoundException error){return ToolResult.Failure(ToolFailure.InvalidInput,error.Message);}
            catch(Exception error){return ToolResult.Failure(ToolFailure.Protocol,error.Message);}
        });
        public Task<ToolResult> ValidateAsync(ModelToolRequest request)=>ModelOperation(request,"--validate");
        public Task<ToolResult> PreviewAsync(ModelToolRequest request)=>ModelOperation(request,"--preview");
    }
}
