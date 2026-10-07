using System;
using System.Windows.Forms;
using SWSimTool.UI;
class MeshExportSettingsUI
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MeshExportSettingsForm(args[0]));
    }
}
