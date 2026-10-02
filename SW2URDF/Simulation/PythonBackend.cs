using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace SW2URDF.Simulation
{
    public static class PythonBackend
    {
        public static void Launch(string python, string urdf, string json, bool preview)
        {
            string script = Path.Combine(Path.GetDirectoryName(typeof(PythonBackend).Assembly.Location), "mujoco_backend", "convert.py");
            if (!File.Exists(script)) throw new FileNotFoundException("Python backend is missing", script);
            var info = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = !preview,
                Arguments = Quote(script) + " --urdf " + Quote(urdf) + " --config " + Quote(json) + " --output " + Quote(Path.ChangeExtension(urdf, ".mjcf.xml")) + (preview ? " --preview" : ""),
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8 };
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            var process = new Process { StartInfo = info, EnableRaisingEvents = true };
            string log = "";
            object gate = new object();
            process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (gate) log += e.Data + Environment.NewLine; };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (gate) log += e.Data + Environment.NewLine; };
            process.Exited += (s, e) => { process.WaitForExit(); string report; lock (gate) report = log; File.WriteAllText(Path.ChangeExtension(urdf, ".mujoco.log"), report); MessageBox.Show(report, process.ExitCode == 0 ? "MuJoCo 完成" : "MuJoCo 失败"); process.Dispose(); };
            if (!process.Start()) throw new InvalidOperationException("Cannot start Python");
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        private static string Quote(string value)
        {
            if (value.Contains("\"")) throw new ArgumentException("A path cannot contain quotes.");
            return "\"" + value + "\"";
        }
    }
}
