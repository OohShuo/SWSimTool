using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace SWSimTool.Simulation
{
    public enum ToolFailure { None, Start, Exit, Cancelled, Timeout, Protocol, InputChanged, EnvironmentMissing, DependencyMissing, InvalidInput, BudgetExceeded, ToolFailed, ValidationFailed }
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

}
