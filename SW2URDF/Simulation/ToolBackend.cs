using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace SW2URDF.Simulation
{
    public enum ToolFailure { None, Start, Exit, Cancelled, Timeout, Protocol, InputChanged }
    public class ToolResult
    {
        public int ExitCode {get;}
        public ToolFailure FailureKind {get;}
        public string Diagnostic {get;}
        public bool Success => FailureKind==ToolFailure.None&&ExitCode==0;
        public ToolResult(int code,ToolFailure failure,string diagnostic){ExitCode=code;FailureKind=failure;Diagnostic=diagnostic??"";}
        public static ToolResult Failure(ToolFailure failure,string diagnostic)=>new ToolResult(1,failure,diagnostic);
    }
    public sealed class ToolContext
    {
        public string Python {get;} public string ExportId {get;}
        public TimeSpan Timeout {get;} public CancellationToken Cancellation {get;}
        public Action<string> Report {get;}
        public ToolContext(string python,string exportId,TimeSpan timeout,CancellationToken cancellation=default(CancellationToken),Action<string> report=null)
        {Python=python;ExportId=exportId??Guid.NewGuid().ToString("N");Timeout=timeout;Cancellation=cancellation;Report=report;}
    }
    public sealed class MeshToolInput
    {
        public string Id {get;} public string Source {get;} public string Relative {get;} public string Sha256 {get;}
        public MeshToolInput(string id,string source,string relative,string sha256){Id=id;Source=Path.GetFullPath(source);Relative=relative;Sha256=sha256;}
    }
    public sealed class MeshPreparationRequest
    {
        public ToolContext Context {get;} public string Work {get;} public string Staging {get;} public string Settings {get;}
        public IReadOnlyList<MeshToolInput> Meshes {get;}
        public MeshPreparationRequest(ToolContext context,string work,string staging,string settings,IEnumerable<MeshToolInput> meshes)
        {Context=context;Work=Path.GetFullPath(work);Staging=Path.GetFullPath(staging);Settings=settings;Meshes=Array.AsReadOnly(meshes.ToArray());}
    }
    public sealed class MeshPreparationResult:ToolResult
    {
        public IReadOnlyDictionary<string,int> Counts {get;}
        public MeshPreparationResult(ToolResult result,IDictionary<string,int> counts=null):base(result.ExitCode,result.FailureKind,result.Diagnostic)
        {Counts=new ReadOnlyDictionary<string,int>(new Dictionary<string,int>(counts??new Dictionary<string,int>()));}
    }
    public sealed class ModelToolRequest
    {
        public ToolContext Context {get;} public string Xml {get;}
        public ModelToolRequest(ToolContext context,string xml){Context=context;Xml=Path.GetFullPath(xml);}
    }
    public interface IMeshPreparationService {Task<MeshPreparationResult> PrepareAsync(MeshPreparationRequest request);}
    public interface IMuJoCoValidationService {Task<ToolResult> ValidateAsync(ModelToolRequest request);}
    public interface IPreviewService {Task<ToolResult> PreviewAsync(ModelToolRequest request);}

    // Only paths, mesh-processing preferences and tool context cross this boundary.
    // No RobotModel, CAD object, URDF conversion or simulation semantics here.
    public sealed class PythonToolBackend:IMeshPreparationService,IMuJoCoValidationService,IPreviewService
    {
        readonly string script;
        public PythonToolBackend(string supportScript=null){script=supportScript??Path.Combine(Path.GetDirectoryName(typeof(PythonToolBackend).Assembly.Location),"mujoco_backend","native_support.py");}
        ToolResult Execute(ToolContext context,string arguments)
        {
            try{return BackendProcess.Execute(context.Python,script,arguments,context.Report,context.ExportId,context.Cancellation,context.Timeout);}
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
                    var preferences=MeshExportSettings.Load(settings);preferences.Validate();
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
                            if(triangles<1||triangles>budget||stream.Length!=84L+50L*triangles)throw new InvalidDataException("Prepared STL exceeds budget or has invalid length");
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
            }catch(Exception error){return ToolResult.Failure(ToolFailure.Protocol,error.Message);}
        });
        public Task<ToolResult> ValidateAsync(ModelToolRequest request)=>ModelOperation(request,"--validate");
        public Task<ToolResult> PreviewAsync(ModelToolRequest request)=>ModelOperation(request,"--preview");
    }
}
