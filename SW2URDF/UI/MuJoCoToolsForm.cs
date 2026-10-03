using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using SW2URDF.Simulation;
namespace SW2URDF.UI
{
    public sealed class MuJoCoToolsForm : Form
    {
        private readonly TextBox python = new TextBox(), urdf = new TextBox(), sidecar = new TextBox(), output = new TextBox(), existing = new TextBox();
        private readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        private readonly Label status = new Label { AutoSize = true, Text = "请选择本地文件，无需打开 SolidWorks 工程。" };
        private readonly TableLayoutPanel inputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        private readonly string settingsPath;
        public MuJoCoToolsForm() : this(MuJoCoSettings.DefaultPath) { }
        public MuJoCoToolsForm(string preferencesPath)
        {
            settingsPath = preferencesPath;
            Text = "MuJoCo 工具"; ClientSize = new Size(840, 510); MinimumSize = new Size(700, 420);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            AddPath("Python", python, "Python|python.exe|程序|*.exe", false);
            AddPath("URDF", urdf, "URDF|*.urdf", false);
            AddPath("附加配置", sidecar, "附加配置|*.sim.json|JSON|*.json", false);
            AddPath("MJCF 保存位置", output, "MJCF|*.mjcf.xml|XML|*.xml", true);
            AddPath("已有 MJCF", existing, "MJCF / XML|*.xml", false);
            AddAction("保存 MJCF", () => Run(false, false));
            AddAction("转换并预览", () => Run(true, false));
            AddAction("预览已有 MJCF", () => Run(true, true));
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
            footer.Controls.Add(status);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            body.Controls.Add(log); body.Controls.Add(footer); body.Controls.Add(actions); body.Controls.Add(inputs); Controls.Add(body);
            try {
                var settings = MuJoCoSettings.Load(settingsPath);
                python.Text = settings.Python; urdf.Text = settings.Urdf; sidecar.Text = settings.Sidecar;
                output.Text = settings.Output; existing.Text = settings.ExistingMjcf;
            } catch (Exception error) { python.Text = "python"; Append("读取设置失败：" + error.Message); }
            urdf.TextChanged += (s, e) => {
                try { if (!string.IsNullOrWhiteSpace(urdf.Text)) { sidecar.Text = Path.ChangeExtension(urdf.Text.Trim(), ".sim.json"); output.Text = Path.ChangeExtension(urdf.Text.Trim(), ".mjcf.xml"); } }
                catch (ArgumentException) { }
            };
            FormClosing += (s, e) => SaveSettings();
        }
        private void AddPath(string label, TextBox field, string filter, bool save)
        {
            int row = inputs.RowCount++;
            field.Dock = DockStyle.Fill;
            inputs.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            inputs.Controls.Add(field, 1, row);
            var browse = new Button { Text = "浏览…", AutoSize = true };
            inputs.Controls.Add(browse, 2, row);
            browse.Click += (s, e) => {
                using (FileDialog dialog = save ? (FileDialog)new SaveFileDialog { DefaultExt = "xml", AddExtension = true } : new OpenFileDialog()) {
                    dialog.Filter = filter; dialog.FileName = field.Text.Trim();
                    if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName;
                }
            };
        }
        private void AddAction(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 32 };
            button.Click += (s, e) => action(); actions.Controls.Add(button);
        }
        private void Append(string line)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(Append), line); } catch (InvalidOperationException) { } return; }
            log.AppendText(line + Environment.NewLine);
        }
        private void SaveSettings()
        {
            try { new MuJoCoSettings { Python = python.Text.Trim(), Urdf = urdf.Text.Trim(), Sidecar = sidecar.Text.Trim(), Output = output.Text.Trim(), ExistingMjcf = existing.Text.Trim() }.Save(settingsPath); }
            catch (Exception error) { Append("保存设置失败：" + error.Message); }
        }
        private async void Run(bool preview, bool direct)
        {
            try {
                if (string.IsNullOrWhiteSpace(python.Text)) throw new ArgumentException("请填写本地 Python 命令或 python.exe 路径。");
                if (direct) RequireFile(existing.Text);
                else {
                    RequireFile(urdf.Text); RequireFile(sidecar.Text);
                    if (string.IsNullOrWhiteSpace(output.Text)) throw new ArgumentException("请选择 MJCF 保存位置。");
                    string destination = Path.GetFullPath(output.Text.Trim());
                    foreach (string source in new[] { urdf.Text, sidecar.Text })
                        if (string.Equals(destination, Path.GetFullPath(source.Trim()), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("MJCF 保存位置不能覆盖输入文件。");
                }
                SaveSettings(); log.Clear(); inputs.Enabled = actions.Enabled = false;
                status.Text = preview ? "正在启动预览；关闭 viewer 后可继续操作。" : "正在保存 MJCF…";
                int code = await PythonBackend.RunAsync(python.Text.Trim(), urdf.Text.Trim(), sidecar.Text.Trim(), output.Text.Trim(), preview, direct ? existing.Text.Trim() : null, Append);
                if (!IsDisposed) status.Text = code == 0 ? (preview ? "预览已结束。" : "MJCF 已保存：" + output.Text) : "运行失败，详情见日志。";
            } catch (Exception error) { if (!IsDisposed) status.Text = "运行失败。"; Append(error.Message); }
            finally { if (!IsDisposed) inputs.Enabled = actions.Enabled = true; }
        }
        private static void RequireFile(string path)
        {
            if (!File.Exists(path.Trim())) throw new FileNotFoundException("找不到文件：" + path);
        }
    }
}
