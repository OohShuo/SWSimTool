using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SWSimTool.Simulation
{
    public static class PythonBackend
    {
        // Explicit legacy reference harness only; installed packages omit convert.py.
        // Production native orchestration does not call this generator.
        public static Task<int> RunAsync(string python, string urdf, string json, string output,
            bool preview, string existingMjcf, Action<string> report, string meshSettingsPath = null, string exportId = null)
        {
            return Task.Run(() =>
            {
                if (existingMjcf == null) output = PackageOutput(urdf, output);
                string script = Path.Combine(Path.GetDirectoryName(typeof(PythonBackend).Assembly.Location), "mujoco_backend", "convert.py");
                if (!File.Exists(script)) throw new FileNotFoundException("Python backend is missing", script);
                string arguments = Quote(script) + (existingMjcf != null ? " --mjcf " + Quote(existingMjcf) :
                    " --urdf " + Quote(urdf) + (string.IsNullOrWhiteSpace(json)?"":" --config " + Quote(json)) + " --output " + Quote(output));
                if (existingMjcf == null) {
                    string preferences = meshSettingsPath ?? MeshExportSettings.DefaultPath;
                    if (File.Exists(preferences)) arguments += " --mesh-settings " + Quote(preferences);
                }
                if (preview) arguments += " --preview";
                var info = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                    Arguments = arguments, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                info.EnvironmentVariables["SWSIMTOOL_EXPORT_ID"] = exportId ?? Guid.NewGuid().ToString("N");
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                var log = new StringBuilder();
                object gate = new object();
                DataReceivedEventHandler receive = (s, e) => {
                    if (e.Data == null) return;
                    lock (gate) { log.AppendLine(e.Data); if (report != null) report(e.Data); }
                };
                using (var process = new Process { StartInfo = info })
                {
                    process.OutputDataReceived += receive; process.ErrorDataReceived += receive;
                    if (!process.Start()) throw new InvalidOperationException("Cannot start Python");
                    process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                    return process.ExitCode;
                }
            });
        }
        // Compatibility entry for the isolated validation harness.
        public static async void Launch(string python, string urdf, string json, bool preview)
        {
            string report = "";
            try { int code = await RunAsync(python, urdf, json, DefaultOutput(urdf), preview, null, line => report += line + Environment.NewLine);
                MessageBox.Show(report, code == 0 ? "MuJoCo 完成" : "MuJoCo 失败"); }
            catch (Exception error) { MessageBox.Show(error.Message, "MuJoCo 失败"); }
        }
        public static string DefaultOutput(string urdf)
        {
            return ExportOutputPaths.DefaultOutput(urdf);
        }
        public static string PackageOutput(string urdf, string output)
        {
            return ExportOutputPaths.PackageOutput(urdf,output);
        }
        private static string Quote(string value)
        {
            if (value == null || value.Contains("\"")) throw new ArgumentException("A path cannot contain quotes.");
            return "\"" + value + new string('\\', value.Length - value.TrimEnd('\\').Length) + "\"";
        }
    }
}
