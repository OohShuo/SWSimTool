using SWSimTool.Simulation;
using System;
using System.Collections;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace SWSimTool.UI
{
    public sealed class SimulationConfigForm : Form
    {
        public static System.Collections.Generic.Dictionary<string,string> JointOwners(SWSimTool.URDF.LinkNode root){
            var result=new System.Collections.Generic.Dictionary<string,string>();
            Action<SWSimTool.URDF.LinkNode> visit=null;
            visit=n=>{if(!n.IsBaseNode&&!string.IsNullOrWhiteSpace(n.Link.Joint.Name))result.Add(n.Link.Joint.Name,n.Link.Name);foreach(SWSimTool.URDF.LinkNode child in n.Nodes)visit(child);};visit(root);return result;
        }
        public static string[] JointNames(TreeNodeCollection nodes)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (SWSimTool.URDF.LinkNode node in nodes)
            {
                if (!node.IsBaseNode && !string.IsNullOrWhiteSpace(node.Link.Joint.Name)) names.Add(node.Link.Joint.Name);
                names.AddRange(JointNames(node.Nodes));
            }
            return names.ToArray();
        }
        public SimulationConfigForm(AttachmentService service, string selectedLink, string[] joints)
        {
            Text = "附着点与 MuJoCo 配置 — " + selectedLink;
            Size = new Size(850, 620);
            StartPosition = FormStartPosition.CenterScreen;
            var serializer = new JavaScriptSerializer();
            var project = serializer.Deserialize<SimulationProject>(serializer.Serialize(service.Project));
            var tabs = new TabControl { Dock = DockStyle.Fill };
            Controls.Add(tabs);
            var page = new TabPage("当前 link 的点 / 坐标系");
            tabs.TabPages.Add(page);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            page.Controls.Add(layout);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "选择 SolidWorks 参考点或参考坐标系（包括已加载的子装配体）。\n点仅导出 xyz；坐标系导出 xyz 和 rpy，均相对最终 URDF link。" });
            var sources = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var source in service.Sources()) sources.Items.Add(source);
            layout.Controls.Add(sources);
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var name = new TextBox { Width = 180, Text = "site1" };
            var add = new Button { Text = "添加到 " + selectedLink, AutoSize = true };
            row.Controls.Add(name); row.Controls.Add(add); layout.Controls.Add(row);
            var list = new ListBox { Dock = DockStyle.Fill };
            Action refresh = () => { list.Items.Clear(); foreach (var a in project.attachments.Where(a => a.link == selectedLink)) list.Items.Add(a.name + " [" + a.type + "] ← " + a.source_name); };
            refresh(); layout.Controls.Add(list);
            var remove = new Button { Text = "删除选中项", Dock = DockStyle.Fill };
            layout.Controls.Add(remove);
            add.Click += (s, e) => Guard(() => {
                if (!(sources.SelectedItem is AttachmentService.Source source)) throw new InvalidOperationException("请先选择参考点或坐标系。");
                if (string.IsNullOrWhiteSpace(name.Text) || project.attachments.Any(a => a.name == name.Text.Trim())) throw new InvalidOperationException("名称不能为空或重复。");
                project.attachments.Add(service.Capture(source, selectedLink, name.Text.Trim())); refresh();
            });
            remove.Click += (s, e) => { var items = project.attachments.Where(a => a.link == selectedLink).ToList(); if (list.SelectedIndex >= 0) { project.attachments.Remove(items[list.SelectedIndex]); refresh(); } };
            AddRules(tabs, "Actuator", project.actuators, () => new ActuatorConfig { joint = joints.FirstOrDefault() },
                "type: motor / position / velocity；gain: motor 不使用，position=kp，velocity=kv。gear、控制范围、力范围按设备填写。\n可用 joints：" + string.Join(", ", joints));
            AddRules(tabs, "Sensor / Camera", project.sensors, () => new SensorConfig { site = project.attachments.FirstOrDefault(a => a.type == "frame")?.name },
                "type: imu / tof / camera；必须引用 frame。imu 生成加速度计与陀螺仪；tof 沿 +Z 测距；camera 朝 -Z。\nnoise 保持 0（噪声需采样层实现）；cutoff 为输出裁剪值；fovy 为视场角（度）。");
            AddRules(tabs, "闭链 Equality", project.equalities, () => new EqualityConfig(),
                "type: connect / weld；site1、site2 填附着点名称。connect 约束位置；weld 需要两个 frame。\n转换前检查两点位于预期连接位置，约束添加后再验证动态稳定性。");
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 70 };
            var save = new Button { Text = "保存配置", AutoSize = true }; bottom.Controls.Add(save);
            var cancel = new Button { Text = "取消", AutoSize = true }; bottom.Controls.Add(cancel);
            Controls.Add(bottom);
            save.Click += (s, e) => Guard(() => { var previous = service.Project; service.Project = project; try { service.Save(); } catch { service.Project = previous; throw; } DialogResult = DialogResult.OK; Close(); });
            cancel.Click += (s, e) => Close();
        }
        private static void AddRules(TabControl tabs, string title, IList items, Func<object> create, string help)
        {
            var page = new TabPage(title); tabs.TabPages.Add(page);
            var grid = new PropertyGrid { Dock = DockStyle.Fill };
            var list = new ListBox { Dock = DockStyle.Left, Width = 230 };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40 };
            var add = new Button { Text = "添加" }; var remove = new Button { Text = "删除" };
            buttons.Controls.Add(add); buttons.Controls.Add(remove);
            page.Controls.Add(grid); page.Controls.Add(list); page.Controls.Add(buttons);
            page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 75, Text = help });
            Action refresh = () => { list.Items.Clear(); foreach (var item in items) list.Items.Add(item); };
            refresh();
            list.SelectedIndexChanged += (s, e) => grid.SelectedObject = list.SelectedItem;
            grid.PropertyValueChanged += (s, e) => { int selected = list.SelectedIndex; refresh(); list.SelectedIndex = selected; };
            add.Click += (s, e) => { items.Add(create()); refresh(); list.SelectedIndex = items.Count - 1; };
            remove.Click += (s, e) => { if (list.SelectedIndex >= 0) { items.RemoveAt(list.SelectedIndex); grid.SelectedObject = null; refresh(); } };
        }
        private static void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(e.Message, "MuJoCo 配置", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
    }
}
