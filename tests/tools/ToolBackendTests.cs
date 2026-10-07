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
            result=run("exit");Check(result.ExitCode==7&&result.FailureKind==ToolFailure.ToolFailed&&result.Diagnostic.Contains("expected error"),"Nonzero exit and stderr retained");
            Check(run("bad-error-json").FailureKind==ToolFailure.Protocol,"Malformed structured error is protocol failure");
            Check(run("modify").FailureKind==ToolFailure.InputChanged,"Compile tool cannot rewrite candidate XML");
            File.WriteAllText(xml,"ok");result=fake.PreviewAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),xml)).Result;
            Check(result.Success&&result.Diagnostic.Contains("FAKE_PREVIEW_CALL"),"Preview contract dispatched without opening real viewer");
            var cancelled=new CancellationTokenSource();cancelled.Cancel();
            Check(fake.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),cancelled.Token),xml)).Result.FailureKind==ToolFailure.Cancelled,"Pre-cancel never starts tool");
            Check(new PythonToolBackend(Path.Combine(root,"missing.py")).ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),xml)).Result.FailureKind==ToolFailure.Start,"Missing script is structured start error");
            Check(fake.ValidateAsync(new ModelToolRequest(new ToolContext(Path.Combine(root,"missing.exe"),null,TimeSpan.FromSeconds(5)),xml)).Result.FailureKind==ToolFailure.EnvironmentMissing,"Missing runtime is structured environment error");
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
            var changedSettings=Path.Combine(root,"changed-settings.json");File.WriteAllText(changedSettings,"{\"Enabled\":false,\"MaximumTriangles\":100}");
            Check(fake.PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),work,staging,changedSettings,new[]{new MeshToolInput("settings-change",source,"meshes/one.stl",NativeAssetPlanner.Hash(source))})).Result.FailureKind==ToolFailure.InputChanged,"Settings mutation during execution is explicit input change");
            var real=new PythonToolBackend();File.WriteAllText(xml,"<mujoco><worldbody><body><joint/><geom type='sphere' size='.1'/></body></worldbody></mujoco>");
            Check(real.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),xml)).Result.Success,"Official MuJoCo compiler accepts valid C# input");
            File.WriteAllText(xml,"<mujoco><worldbody><wrong/></worldbody></mujoco>");
            Check(real.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),xml)).Result.FailureKind==ToolFailure.ValidationFailed,"Official MuJoCo compiler rejects invalid input with validation category");
            Check(FaultTool(root,"mujoco").ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),xml)).Result.FailureKind==ToolFailure.DependencyMissing,"Python present but MuJoCo missing is dependency failure");
            Check(real.ValidateAsync(new ModelToolRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),Path.Combine(root,"missing.xml"))).Result.FailureKind==ToolFailure.InvalidInput,"Missing XML is invalid input");
            WriteOctahedron(source);var meshSettings=Path.Combine(root,"fault-settings.json");
            foreach(var backend in new[]{"fast-simplification","pymeshlab"}) {
                File.WriteAllText(meshSettings,"{\"Enabled\":true,\"MaximumTriangles\":4,\"CacheEnabled\":false,\"Backend\":\""+backend+"\"}");
                var dependency=backend=="pymeshlab"?"pymeshlab":"fast_simplification";
                var missing=FaultTool(root,dependency).PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),work,staging,meshSettings,new[]{new MeshToolInput("octa",source,"meshes/octa.stl",NativeAssetPlanner.Hash(source))})).Result;
                Check(missing.FailureKind==ToolFailure.DependencyMissing,"Selected simplifier missing: "+backend+"; "+missing.Diagnostic);
            }
            File.WriteAllText(meshSettings,"{\"Enabled\":true,\"MaximumTriangles\":4,\"CacheEnabled\":false,\"Backend\":\"fast-simplification\"}");
            var beforeMesh=NativeAssetPlanner.Hash(source);
            var unmet=FaultTool(root,"budget").PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(15),CancellationToken.None),work,staging,meshSettings,new[]{new MeshToolInput("octa",source,"meshes/octa.stl",beforeMesh)})).Result;
            Check(unmet.FailureKind==ToolFailure.BudgetExceeded&&NativeAssetPlanner.Hash(source)==beforeMesh,"Simplifier cannot meet budget: explicit failure and original preserved; "+unmet.Diagnostic);
            unmet=fake.PrepareAsync(new MeshPreparationRequest(context(TimeSpan.FromSeconds(5),CancellationToken.None),work,staging,meshSettings,new[]{new MeshToolInput("octa",source,"meshes/octa.stl",beforeMesh)})).Result;
            Check(unmet.FailureKind==ToolFailure.BudgetExceeded,"Independent C# output guard rejects oversize STL");
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
    static PythonToolBackend FaultTool(string root,string fault){
        var path=Path.Combine(root,"fault-"+fault+".py");var support=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mujoco_backend","native_support.py");
        var literal=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(support);
        var code="import sys, os, runpy, importlib.abc\nsys.path.insert(0, os.path.dirname("+literal+"))\n";
        if(fault=="budget")code+="import simplify_stl\nsimplify_stl.reduce_python=lambda vertices, faces, target, backend: (vertices, faces)\n";
        else code+="class Block(importlib.abc.MetaPathFinder):\n def find_spec(self, fullname, path=None, target=None):\n  if fullname == '"+fault+"': raise ModuleNotFoundError('Controlled missing dependency: '+fullname)\nsys.meta_path.insert(0, Block())\n";
        code+="runpy.run_path("+literal+", run_name='__main__')\n";File.WriteAllText(path,code);return new PythonToolBackend(path);
    }
    static void WriteOctahedron(string path){
        var vertices=new[]{new[]{1f,0f,0f},new[]{-1f,0f,0f},new[]{0f,1f,0f},new[]{0f,-1f,0f},new[]{0f,0f,1f},new[]{0f,0f,-1f}};
        var faces=new[]{new[]{0,2,4},new[]{2,1,4},new[]{1,3,4},new[]{3,0,4},new[]{2,0,5},new[]{1,2,5},new[]{3,1,5},new[]{0,3,5}};
        using(var stream=File.Create(path))using(var writer=new BinaryWriter(stream)){writer.Write(new byte[80]);writer.Write((uint)8);foreach(var face in faces){writer.Write(0f);writer.Write(0f);writer.Write(0f);foreach(var index in face)foreach(var v in vertices[index])writer.Write(v);writer.Write((ushort)0);}}
    }
    sealed class RejectValidation:IMuJoCoValidationService {public Task<ToolResult> ValidateAsync(ModelToolRequest request)=>Task.FromResult(ToolResult.Failure(ToolFailure.Exit,"controlled compiler failure"));}
    sealed class CancelValidation:IMuJoCoValidationService {readonly CancellationTokenSource stop;internal CancelValidation(CancellationTokenSource source){stop=source;}public Task<ToolResult> ValidateAsync(ModelToolRequest request){stop.Cancel();return Task.FromResult(new ToolResult(0,ToolFailure.None,"controlled cancellation"));}}
}
