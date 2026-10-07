using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading;
using SW2URDF.Simulation;
namespace SW2URDF.UI
{
    public enum MuJoCoToolMode {Local,Project,Preview}
    public sealed class MuJoCoToolsForm : Form
    {
        private readonly TextBox python = new TextBox(), urdf = new TextBox(), sidecar = new TextBox(), output = new TextBox(), existing = new TextBox();
        private readonly TextBox log = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill };
        private readonly Label status = new Label { AutoSize = true, Text = "请选择本地文件，无需打开 SolidWorks 工程。" };
        private readonly TableLayoutPanel inputs = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        private readonly FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        private readonly string settingsPath;
        private readonly string meshSettingsPath;
        readonly MuJoCoToolMode toolMode;
        readonly Func<ProjectExport> exportProject;
        bool busy;
        bool closeWhenStopped;
        CancellationTokenSource operation;
        public MuJoCoToolsForm() : this(MuJoCoSettings.DefaultPath) { }
        public MuJoCoToolsForm(string preferencesPath, string meshPreferencesPath = null,MuJoCoToolMode mode=MuJoCoToolMode.Local,Func<ProjectExport> projectExporter=null)
        {
            toolMode=mode;exportProject=projectExporter;
            settingsPath = preferencesPath;
            meshSettingsPath = meshPreferencesPath;
            Text = "SW2MuJoCo — "+(mode==MuJoCoToolMode.Project?"从当前工程导出 MJCF":mode==MuJoCoToolMode.Local?"从本地 URDF 导出 MJCF":"预览已有 MJCF"); ClientSize = new Size(840, 510); MinimumSize = new Size(700, 420);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            AddPath("Python", python, "Python|python.exe|程序|*.exe", false);
            if(mode==MuJoCoToolMode.Local){AddPath("URDF", urdf, "URDF|*.urdf", false);AddPath("附加配置（可选）", sidecar, "附加配置|*.sim.json|JSON|*.json", false);}
            if(mode!=MuJoCoToolMode.Preview){
                AddPath("MJCF 保存位置", output, "MJCF / XML|*.xml", true);
                AddAction("导出 MJCF", () => Run(false, false));AddAction("导出并预览 MJCF", () => Run(true, false));
                AddAction("STL 减面设置…", () => { using (var form = new MeshExportSettingsForm(meshSettingsPath ?? MeshExportSettings.DefaultPath)) form.ShowDialog(this); });
            }else{AddPath("已有 MJCF", existing, "MJCF / XML|*.xml", false);AddAction("预览已有 MJCF", () => Run(true, true));}
            AddAction("保存诊断信息…",()=>{using(var dialog=new SaveFileDialog{Filter="日志|*.log",FileName="SW2MuJoCo.log"})if(dialog.ShowDialog(this)==DialogResult.OK)File.WriteAllText(dialog.FileName,log.Text);});
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 8) };
            footer.Controls.Add(status);
            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            body.Controls.Add(log); body.Controls.Add(footer); body.Controls.Add(actions); body.Controls.Add(inputs); Controls.Add(body);
            try {
                var settings = MuJoCoSettings.Load(settingsPath);
                python.Text = settings.Python; urdf.Text = settings.Urdf; sidecar.Text = settings.Sidecar;
                output.Text = settings.Output; existing.Text = settings.ExistingMjcf;
                if (!string.IsNullOrWhiteSpace(urdf.Text) && output.Text == Path.ChangeExtension(urdf.Text, ".mjcf.xml")) output.Text = ExportOutputPaths.DefaultOutput(urdf.Text);
            } catch (Exception error) { python.Text = "python"; Append("读取设置失败：" + error.Message); }
            urdf.TextChanged += (s, e) => {
                    try { if (!string.IsNullOrWhiteSpace(urdf.Text)) { string candidate=Path.ChangeExtension(urdf.Text.Trim(), ".sim.json");sidecar.Text=File.Exists(candidate)?candidate:""; output.Text = ExportOutputPaths.DefaultOutput(urdf.Text.Trim()); } }
                catch (ArgumentException) { }
            };
            status.Text=mode==MuJoCoToolMode.Project?"从当前装配导出，最终仅保存 XML 和 meshes/STL。":"请选择本地文件，无需打开 SolidWorks 工程。";
            FormClosing += (s, e) => {if(busy){e.Cancel=true;closeWhenStopped=true;operation?.Cancel();status.Text="正在取消并清理后端…";}else SaveSettings();};
        }
        public void SetProjectName(string name){output.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop),name+"_mjcf",name+".xml");}
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
            try {var settings=MuJoCoSettings.Load(settingsPath);settings.Python=python.Text.Trim();if(toolMode==MuJoCoToolMode.Local){settings.Urdf=urdf.Text.Trim();settings.Sidecar=sidecar.Text.Trim();settings.Output=output.Text.Trim();}if(toolMode==MuJoCoToolMode.Preview)settings.ExistingMjcf=existing.Text.Trim();settings.Save(settingsPath);}
            catch (Exception error) { Append("保存设置失败：" + error.Message); }
        }
        private async void Run(bool preview, bool direct)
        {
            ProjectExport project=null;
            try {
                if (string.IsNullOrWhiteSpace(python.Text)) throw new ArgumentException("请填写本地 Python 命令或 python.exe 路径。");
                if (direct) RequireFile(existing.Text);
                else {
                    if(toolMode==MuJoCoToolMode.Local){RequireFile(urdf.Text);if(!string.IsNullOrWhiteSpace(sidecar.Text))RequireFile(sidecar.Text);}
                    if (string.IsNullOrWhiteSpace(output.Text)) throw new ArgumentException("请选择 MJCF 保存位置。");
                    output.Text = ExportOutputPaths.PackageOutput(toolMode==MuJoCoToolMode.Project?Path.GetFileNameWithoutExtension(output.Text)+".urdf":urdf.Text.Trim(), output.Text.Trim());
                    string destination = Path.GetFullPath(output.Text.Trim());
                    foreach (string source in new[] { urdf.Text, sidecar.Text })if(!string.IsNullOrWhiteSpace(source)&&toolMode==MuJoCoToolMode.Local)
                        if (string.Equals(destination, Path.GetFullPath(source.Trim()), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("MJCF 保存位置不能覆盖输入文件。");
                }
                SaveSettings(); log.Clear();busy=true;operation=new CancellationTokenSource(); inputs.Enabled = actions.Enabled = false;
                if(toolMode==MuJoCoToolMode.Project){status.Text="正在读取当前工程配置…";project=exportProject();Append("Export "+project.ExportId+": "+(project.Plan.RebuildSource?"重建工程源数据":"复用工程源数据")+"; "+project.Plan.Dirty);}
                status.Text = preview ? "正在启动预览；关闭 viewer 后可继续操作。" : "正在保存 MJCF…";
                int code = direct ? await NativeBackend.PreviewAsync(python.Text.Trim(),existing.Text.Trim(),Append,operation.Token) : await NativeBackend.RunAsync(python.Text.Trim(),project!=null?project.NativeModel():NativeBackend.LoadLocal(urdf.Text.Trim(),sidecar.Text.Trim()),output.Text.Trim(),preview,Append,meshSettingsPath,project?.ExportId,operation.Token);
                if(code==0)project?.MarkSucceeded();
                if (!IsDisposed) status.Text = code == 0 ? (preview ? "预览已结束。" : "MJCF 已保存：" + output.Text) : "运行失败，详情见日志。";
            } catch (Exception error) { if (!IsDisposed) status.Text = "运行失败。"; Append(error.Message); }
            finally {try{project?.Dispose();}catch(Exception error){Append("临时文件清理失败："+error.Message);}operation?.Dispose();operation=null;busy=false;if (!IsDisposed){inputs.Enabled = actions.Enabled = true;if(closeWhenStopped)Close();} }
        }
        private static void RequireFile(string path)
        {
            if (!File.Exists(path.Trim())) throw new FileNotFoundException("找不到文件：" + path);
        }
    }
}
