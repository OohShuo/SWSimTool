using System;
using System.Threading;
using SWSimTool.Simulation;

// Test-only fixture adapter to the real APIs. Never part of the product payload.
internal static class LocalBackendFixture
{
    internal static int Run(string[] args)
    {
        if(args.Length==2 && args[0]=="--local-load") {
            Console.WriteLine(NativeBackend.LoadLocal(args[1],null).Core.Links.Count);
            return 0;
        }
        if(args.Length==4 && args[0]=="--local-export") {
            var model=NativeBackend.LoadLocal(args[2],null);
            return NativeBackend.RunAsync(args[1],model,args[3],false,Console.Error.WriteLine,null,null,CancellationToken.None).GetAwaiter().GetResult();
        }
        if(args.Length==3 && args[0]=="--local-validate") {
            var result=new PythonToolBackend().ValidateAsync(new ModelToolRequest(new ToolContext(args[1],null,TimeSpan.FromMinutes(2),CancellationToken.None,Console.Error.WriteLine),args[2])).GetAwaiter().GetResult();
            if(!result.Success)Console.Error.WriteLine(result.FailureKind+": "+result.Diagnostic);
            return result.Success?0:1;
        }
        throw new ArgumentException("Invalid local backend test fixture invocation");
    }
}
