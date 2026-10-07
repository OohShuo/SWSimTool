using System.Windows;

namespace SWSimTool.UI
{
    public interface IMessageBox
    {
        MessageBoxResult Show(string message);
        MessageBoxResult Show(string message, string caption, MessageBoxButton buttons);
    }
}
