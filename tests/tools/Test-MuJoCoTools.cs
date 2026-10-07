using System;
using System.IO;
using SW2URDF.Simulation;
internal static class MuJoCoSettingsTests
{
    internal static void Run()
    {
        string folder=Path.Combine(Path.GetTempPath(),"SW2MuJoCo-settings-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            string path=Path.Combine(folder,"settings.json");
            if(MuJoCoSettings.Load(path).Python!="python")throw new Exception("Missing preferences must use defaults");
            var settings=new MuJoCoSettings {Python="python with spaces",Urdf="机械臂.urdf",Sidecar="机械臂.sim.json",Output="custom.xml",ExistingMjcf="existing.xml"};
            settings.Save(path);settings.Save(path);var loaded=MuJoCoSettings.Load(path);
            if(loaded.Python!=settings.Python||loaded.Urdf!=settings.Urdf||loaded.Sidecar!=settings.Sidecar||loaded.Output!=settings.Output||loaded.ExistingMjcf!=settings.ExistingMjcf)throw new Exception("Settings roundtrip failed");
            if(File.Exists(path+".tmp"))throw new Exception("Preference staging left behind");
            Console.WriteLine("PASS: preference defaults, Unicode/path roundtrip and atomic replacement");
        } finally {Directory.Delete(folder,true);}
    }
}