using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SW2URDF.Simulation
{
    public static class PythonBackend
    {
        public static Task<int> RunAsync(string python, string urdf, string json, string output,
            bool preview, string existingMjcf, Action<string> report)
        {
            return Task.Run(() =>
            {
                string script = Path.Combine(Path.GetDirectoryName(typeof(PythonBackend).Assembly.Location), "mujoco_backend", "convert.py");
                if (!File.Exists(script)) throw new FileNotFoundException("Python backend is missing", script);
                string arguments = Quote(script) + (existingMjcf != null ? " --mjcf " + Quote(existingMjcf) :
                    " --urdf " + Quote(urdf) + " --config " + Quote(json) + " --output " + Quote(output));
                if (preview) arguments += " --preview";
                var info = new ProcessStartInfo(python) { UseShellExecute = false, CreateNoWindow = true,
                    Arguments = arguments, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
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
                    string logPath = Path.ChangeExtension(existingMjcf ?? output, ".mujoco.log");
                    try { File.WriteAllText(logPath, log.ToString(), Encoding.UTF8); }
                    catch (Exception error) { if (report != null) report("日志未保存：" + error.Message); }
                    return process.ExitCode;
                }
            });
        }
        // Compatibility entry for the isolated validation harness.
        public static async void Launch(string python, string urdf, string json, bool preview)
        {
            string report = "";
            try { int code = await RunAsync(python, urdf, json, Path.ChangeExtension(urdf, ".mjcf.xml"), preview, null, line => report += line + Environment.NewLine);
                MessageBox.Show(report, code == 0 ? "MuJoCo 完成" : "MuJoCo 失败"); }
            catch (Exception error) { MessageBox.Show(error.Message, "MuJoCo 失败"); }
        }
        private static string Quote(string value)
        {
            if (value == null || value.Contains("\"")) throw new ArgumentException("A path cannot contain quotes.");
            return "\"" + value + new string('\\', value.Length - value.TrimEnd('\\').Length) + "\"";
        }
    }
}
