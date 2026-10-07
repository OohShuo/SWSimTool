using System;
using System.Windows.Forms;
using SWSimTool.UI;
class MuJoCoToolsUI
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MuJoCoToolsForm(args[0], args.Length > 1 ? args[1] : null));
    }
}
