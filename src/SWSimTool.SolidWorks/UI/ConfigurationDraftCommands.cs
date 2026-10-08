using System;
using System.Windows.Forms;
using SWSimTool.Simulation;
using SWSimTool.URDF;
namespace SWSimTool.UI {
    internal static class ConfigurationDraftCommands {
        internal static string Rebuild(AttachmentService service,SimulationProject draft,LinkNode tree=null){
            service.RequireCurrentDocument();
            if(MessageBox.Show("将使用当前配置页面中的全部设置（包含尚未保存的修改），替换当前装配、当前 SolidWorks 配置下已保存的插件配置。\n旧保存内容不会合并或补回。执行前自动备份；零件、配合、草图和参考几何体保持不变。\n是否从当前配置重建并覆盖？","从当前配置重建并覆盖",MessageBoxButtons.OKCancel,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.OK)return null;
            return service.RebuildCurrentDraft(draft,tree);
        }
        internal static void Add(Control footer,AttachmentService service,SimulationProject draft,Action flush,Action<string> completed,Action<Action> guard){
            var button=new Button{Text="从当前配置重建并覆盖",AutoSize=true};footer.Controls.Add(button);
            button.Click+=(sender,args)=>guard(()=>{
                service.RequireCurrentDocument();(footer.Parent as ContainerControl)?.ValidateChildren();flush();
                string backup=Rebuild(service,draft);if(backup==null)return;
                ConfigurationDependencies.Plan(service.Project).ApplyTo(draft);
                draft.solver=service.Project.solver;draft.base_mode=service.Project.base_mode;draft.joint_defaults=service.Project.joint_defaults;
                completed(backup);
            });
        }
    }
}
