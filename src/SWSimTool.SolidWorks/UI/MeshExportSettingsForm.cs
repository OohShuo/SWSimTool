using System;
using System.Drawing;
using System.Windows.Forms;
using SWSimTool.Simulation;
namespace SWSimTool.UI
{
    public sealed class MeshExportSettingsForm : Form
    {
        public MeshExportSettingsForm() : this(MeshExportSettings.DefaultPath) { }
        public MeshExportSettingsForm(string path)
        {
            Text = "STL 超限减面设置"; ClientSize = new Size(680, 290); MinimumSize = new Size(620, 330);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterParent;
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12), AutoSize = true };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            var enabled = new CheckBox { Text = "仅对超限的 STL 启用减面", AutoSize = true };
            table.Controls.Add(enabled, 0, 0); table.SetColumnSpan(enabled, 3);
            var maximum = new NumericUpDown { Minimum = 4, Maximum = 200000, Increment = 1000, ThousandsSeparator = true, Dock = DockStyle.Fill };
            var backend = new NoWheelComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            backend.Items.AddRange(new object[] { "pymeshlab", "fast-simplification", "blender" });
            var python = new TextBox { Dock = DockStyle.Fill }; var blender = new TextBox { Dock = DockStyle.Fill };
            AddRow(table, 1, "每个 STL 三角形上限", maximum);
            AddRow(table, 2, "减面后端", backend);
            AddRow(table, 4, "Blender", blender);
            Browse(table, 4, blender, "Blender|blender.exe|程序|*.exe");
            var help = new Label { Text = "应用到保存 MJCF 和转换并预览，SW 导出不减面。\n使用 MuJoCo 工具中选择的 Python；原始 STL 不修改。\n未超限则复制，超限则减面；处理完才交给 MuJoCo。", AutoSize = true };
            table.Controls.Add(help, 0, 5); table.SetColumnSpan(help, 3);
            var status = new Label { AutoSize = true, ForeColor = Color.DarkRed };
            table.Controls.Add(status, 0, 6); table.SetColumnSpan(status, 3);
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
            var save = new Button { Text = "保存设置", AutoSize = true }; var cancel = new Button { Text = "取消", AutoSize = true };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); table.Controls.Add(buttons, 0, 7); table.SetColumnSpan(buttons, 3);
            Controls.Add(table);
            try { var settings = MeshExportSettings.Load(path); enabled.Checked = settings.Enabled;
                maximum.Value = Math.Max(4, Math.Min(200000, settings.MaximumTriangles));
                backend.SelectedItem = settings.Backend; python.Text = settings.Python; blender.Text = settings.Blender;
            } catch (Exception error) { status.Text = "读取设置失败：" + error.Message; python.Text = "python"; maximum.Value = 100000; backend.SelectedIndex = 0; }
            if (backend.SelectedIndex < 0) backend.SelectedIndex = 0;
            Action update = () => { maximum.Enabled = backend.Enabled = python.Enabled = enabled.Checked; blender.Enabled = enabled.Checked && (string)backend.SelectedItem == "blender"; };
            enabled.CheckedChanged += (s, e) => update(); backend.SelectedIndexChanged += (s, e) => update(); update();
            save.Click += (s, e) => { try { new MeshExportSettings { Enabled = enabled.Checked, MaximumTriangles = (int)maximum.Value, Backend = (string)backend.SelectedItem, Python = python.Text.Trim(), Blender = blender.Text.Trim() }.Save(path); DialogResult = DialogResult.OK; Close(); } catch (Exception error) { status.Text = error.Message; } };
            cancel.Click += (s, e) => Close();
        }
        private static void AddRow(TableLayoutPanel table, int row, string label, Control field)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row); table.Controls.Add(field, 1, row);
        }
        private void Browse(TableLayoutPanel table, int row, TextBox field, string filter)
        {
            var button = new Button { Text = "浏览…", AutoSize = true }; table.Controls.Add(button, 2, row);
            button.Click += (s, e) => { using (var dialog = new OpenFileDialog { Filter = filter }) { if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.FileName; } };
        }
    }
}
