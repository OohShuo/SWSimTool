using System;using System.Windows.Forms;namespace SWSimTool.Simulation { public static class ReferencePreviewDialog {
        // Compatibility entry for the isolated validation harness.
        public static async void Launch(string python, string urdf, string json, bool preview)
        {
            string report = "";
            try { int code = await PythonBackend.RunAsync(python, urdf, json, PythonBackend.DefaultOutput(urdf), preview, null, line => report += line + Environment.NewLine);
                MessageBox.Show(report, code == 0 ? "MuJoCo 完成" : "MuJoCo 失败"); }
            catch (Exception error) { MessageBox.Show(error.Message, "MuJoCo 失败"); }
        }
}}
