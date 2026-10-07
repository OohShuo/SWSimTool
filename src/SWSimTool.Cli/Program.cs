using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SWSimTool.Persistence;
using SWSimTool.Simulation;

namespace SWSimTool.Cli
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler cancel = (sender, e) => { e.Cancel = true; cancellation.Cancel(); };
                Console.CancelKeyPress += cancel;
                try
                {
                    if (args.Length == 0 || args[0] == "--help")
                    {
                        Console.WriteLine("SWSimTool: inspect --urdf FILE [--config JSON] | export --urdf FILE --output XML [--config JSON] [--mesh-settings JSON] [--python EXE] | validate --mjcf XML [--python EXE]");
                        return args.Length == 0 ? 2 : 0;
                    }
                    var options = new Dictionary<string, string>(StringComparer.Ordinal);
                    for (int i = 1; i < args.Length; i += 2)
                    {
                        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 == args.Length || !options.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Options require unique --name value pairs");
                    }
                    var allowed = args[0] == "validate" ? new[] { "--mjcf", "--python" } : args[0] == "inspect" ? new[] { "--urdf", "--config" } : new[] { "--urdf", "--config", "--output", "--mesh-settings", "--python" };
                    if (options.Keys.Any(k => !allowed.Contains(k))) throw new ArgumentException("Unknown option");
                    Func<string, string> require = name => options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Missing " + name);
                    Func<string, string> optional = name => options.TryGetValue(name, out var value) ? value : null;
                    var python = optional("--python") ?? "python";
                    if (args[0] == "validate")
                    {
                        var result = await new PythonToolBackend().ValidateAsync(new ModelToolRequest(new ToolContext(python, null, TimeSpan.FromMinutes(2), cancellation.Token, Console.Error.WriteLine), require("--mjcf")));
                        if (!result.Success) Console.Error.WriteLine(result.FailureKind + ": " + result.Diagnostic);
                        return result.Success ? 0 : cancellation.IsCancellationRequested ? 130 : 1;
                    }
                    if (args[0] != "inspect" && args[0] != "export") throw new ArgumentException("Unknown command");
                    var model = NativeBackend.LoadLocal(require("--urdf"), optional("--config"));
                    if (args[0] == "inspect")
                    {
                        Console.WriteLine(new PortableJsonSerializer().Serialize(new { model = model.Core.Name,
                            links = model.Core.Links.Count, joints = model.Core.Joints.Count }));
                        return 0;
                    }
                    var code = await NativeBackend.RunAsync(python, model, require("--output"), false, Console.Error.WriteLine, optional("--mesh-settings"), null, cancellation.Token);
                    return cancellation.IsCancellationRequested ? 130 : code;
                }
                catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
                catch (Exception error) { Console.Error.WriteLine(error.GetType().Name + ": " + error.Message); return 1; }
                finally { Console.CancelKeyPress -= cancel; }
            }
        }
    }
}
