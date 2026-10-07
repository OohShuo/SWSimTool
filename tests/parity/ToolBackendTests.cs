using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SW2URDF.Simulation;

internal static class ToolBackendTests
{
    static int checks;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}
    internal static void Run(string python,string script)
    {
        var root=Path.Combine(Path.GetTempPath(),"SW2MuJoCo 工具 空格-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            var fake=new PythonToolBackend(Path.GetFullPath(script));var xml=Path.Combine(root,"测试 model.xml");
            Func<TimeSpan,CancellationToken,ToolContext> context=(time,token)=>new ToolContext(python,"tool-test",time,token);
            Func<string,ToolResult> run=mode=>{File.WriteAllText(xml,mode);return fake.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),xml)).GetAwaiter().GetResult();};
            var result=run("ok");Check(result.Success&&result.Diagnostic.Contains("中文路径"),"Unicode path and UTF-8 bounded diagnostics");
            result=run("exit");Check(result.ExitCode==7&&result.FailureKind==ToolFailure.Exit&&result.Diagnostic.Contains("expected error"),"Nonzero exit and stderr retained");
            Check(run("modify").FailureKind==ToolFailure.InputChanged,"Compile tool cannot rewrite candidate XML");
            File.WriteAllText(xml,"ok");result=fake.PreviewAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),xml)).Result;
            Check(result.Success&&result.Diagnostic.Contains("FAKE_PREVIEW_CALL"),"Preview contract dispatched without opening real viewer");
            var cancelled=new CancellationTokenSource();cancelled.Cancel();
            Check(fake.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),cancelled.Token),xml)).Result.FailureKind==ToolFailure.Cancelled,"Pre-cancel never starts tool");
            Check(new PythonToolBackend(Path.Combine(root,"missing.py")).ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),xml)).Result.FailureKind==ToolFailure.Start,"Missing script is structured start error");
            Check(fake.ValidateAsync(new ModelToolRequest(new ToolContext(Path.Combine(root,"missing.exe"),null,TimeSpan.FromSeconds(5)),xml)).Result.FailureKind==ToolFailure.Start,"Missing runtime is structured start error");
            foreach(var cancel in new[]{false,true}) {
                File.WriteAllText(xml,"sleep");var token=new CancellationTokenSource();if(cancel)token.CancelAfter(1200);
                result=fake.ValidateAsync(new ModelToolRequest(context(cancel?Timeout.InfiniteTimeSpan:TimeSpan.FromMilliseconds(1200),token.Token),xml)).Result;
                Check(result.FailureKind==(cancel?ToolFailure.Cancelled:ToolFailure.Timeout),cancel?"Running tool cancellation":"Running tool timeout");
                var childPid=int.Parse(File.ReadAllText(Path.Combine(root,"child.pid")));bool alive=false;
                try{using(var child=Process.GetProcessById(childPid))alive=!child.HasExited;}catch(ArgumentException){}
                Check(!alive,"Owned child tree is terminated");token.Dispose();
            }
            var work=Path.Combine(root,"work");var staging=Path.Combine(root,"stage");Directory.CreateDirectory(work);Directory.CreateDirectory(staging);
            var source=Path.Combine(root,"source.stl");var stl=new byte[284];stl[80]=4;File.WriteAllBytes(source,stl);
            Func<string,MeshPreparationResult> mesh=mode=>fake.PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),work,staging,Path.Combine(root,"no-settings"),new[]{new MeshToolInput(mode,source,"meshes/one.stl",NativeAssetPlanner.Hash(source))})).Result;
            Check(mesh("ok").Success,"Typed mesh request and verified result mapping");
            foreach(var mode in new[]{"bad-id","bad-hash","bad-count","bad-json"})Check(mesh(mode).FailureKind==ToolFailure.Protocol,"Reject invalid mesh result: "+mode);
            Check(mesh("source-change").FailureKind==ToolFailure.InputChanged,"Reject source mutation during mesh tool");File.WriteAllBytes(source,stl);
            File.WriteAllBytes(source,new byte[]{1,2,3});Check(mesh("invalid-stl").FailureKind==ToolFailure.Protocol,"Reject malformed binary STL even with matching hash");File.WriteAllBytes(source,stl);
            var escaped=new MeshToolInput("escape",source,"../outside.stl",NativeAssetPlanner.Hash(source));
            Check(fake.PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),work,staging,null,new[]{escaped})).Result.FailureKind==ToolFailure.Protocol,"Reject escaping asset path");
            var duplicate=new MeshToolInput("same",source,"meshes/one.stl",NativeAssetPlanner.Hash(source));
            Check(fake.PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),work,staging,null,new[]{duplicate,duplicate})).Result.FailureKind==ToolFailure.Protocol,"Reject duplicate asset identities");
            Check(!Directory.EnumerateFiles(work).Any(),"Protocol files cleaned on success and failure");
            var real=new PythonToolBackend();File.WriteAllText(xml,"<mujoco><worldbody><body><joint/><geom type='sphere' size='.1'/></body></worldbody></mujoco>");
            Check(real.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),xml)).Result.Success,"Official MuJoCo compiler accepts valid C# input");
            File.WriteAllText(xml,"<mujoco><worldbody><wrong/></worldbody></mujoco>");
            Check(real.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),xml)).Result.FailureKind==ToolFailure.Exit,"Official MuJoCo compiler rejects invalid input");
            var urdf=Path.Combine(root,"robot.urdf");File.WriteAllText(urdf,"<robot name='tool'><link name='base'/></robot>");
            var model=NativeBackend.LoadLocal(urdf,null);var output=Path.Combine(root,"robot_mjcf","robot.xml");
            var settingsPath=Path.Combine(root,"no-settings");
            var code=NativeExportPipeline.RunAsync(python,model,output,false,Console.WriteLine,settingsPath,"test").Result;
            Check(code==0,"Real typed services publish valid package");var original=File.ReadAllText(output);
            code=NativeExportPipeline.RunAsync(python,model,output,false,null,settingsPath,"test",validationService:new RejectValidation()).Result;
            Check(code!=0&&File.ReadAllText(output)==original,"Validation failure preserves previous package");
            using(var stop=new CancellationTokenSource()) {
                code=NativeExportPipeline.RunAsync(python,model,output,false,null,settingsPath,"test",stop.Token,validationService:new CancelValidation(stop)).Result;
                Check(code!=0&&File.ReadAllText(output)==original,"Cancellation before publication preserves previous package");
            }
            Check(!Directory.EnumerateDirectories(root,".sw2mujoco-*").Any(),"Export staging cleaned after failure");
            Console.WriteLine("Tool contract checks: "+checks);
        }finally{Directory.Delete(root,true);}
    }
    sealed class RejectValidation:IMuJoCoValidationService {public Task<ToolResult> ValidateAsync(ModelToolRequest request)=>Task.FromResult(ToolResult.Failure(ToolFailure.Exit,"controlled compiler failure"));}
    sealed class CancelValidation:IMuJoCoValidationService {readonly CancellationTokenSource stop;internal CancelValidation(CancellationTokenSource source){stop=source;}public Task<ToolResult> ValidateAsync(ModelToolRequest request){stop.Cancel();return Task.FromResult(new ToolResult(0,ToolFailure.None,"controlled cancellation"));}}
}
