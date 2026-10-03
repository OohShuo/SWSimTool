using System;
using System.IO;
using System.Threading.Tasks;
using SW2URDF.Simulation;
class TestMuJoCoTools
{
    static int Main(string[] args)
    {
        string folder = args[1], preferences = Path.Combine(folder, "settings-test.json");
        var settings = new MuJoCoSettings { Python = args[0], Urdf = Path.Combine(folder, "test.urdf"), Sidecar = Path.Combine(folder, "test.sim.json"), Output = PythonBackend.PackageOutput(Path.Combine(folder, "test.urdf"), Path.Combine(folder, "custom.xml")) };
        settings.Save(preferences); settings.Save(preferences);
        var loaded = MuJoCoSettings.Load(preferences);
        if (loaded.Output != settings.Output || loaded.Python != settings.Python) throw new Exception("Settings roundtrip failed");
        string report = "";
        int code = PythonBackend.RunAsync(loaded.Python, loaded.Urdf, loaded.Sidecar, loaded.Output, false, null, line => report += line + "\n").GetAwaiter().GetResult();
        if (code != 0 || !File.Exists(loaded.Output) || !report.Contains("MJCF saved")) throw new Exception(report);
        Console.WriteLine("PASS: user settings roundtrip, local Python process, custom output and live log");
        code = PythonBackend.RunAsync(loaded.Python, "missing.urdf", loaded.Sidecar, loaded.Output, false, null, null).GetAwaiter().GetResult();
        if (code == 0) throw new Exception("Missing input should fail");
        Console.WriteLine("PASS: backend failure propagated");
        return 0;
    }
}
