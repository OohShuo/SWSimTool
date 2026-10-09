using System;
using System.Drawing;
using System.Windows.Forms;

namespace SWSimTool.UI {
    // Keep standard ComboBox editing/key behavior; suppress only wheel selection.
    public class NoWheelComboBox : ComboBox {
        protected override void WndProc(ref Message message){
            if(message.Msg==0x020A||message.Msg==0x020E){
                if(message.Msg==0x020A){int delta=unchecked((short)((message.WParam.ToInt64()>>16)&0xffff));
                    for(Control parent=Parent;parent!=null;parent=parent.Parent){var scroll=parent as ScrollableControl;if(scroll!=null&&scroll.AutoScroll){Point position=scroll.AutoScrollPosition;scroll.AutoScrollPosition=new Point(-position.X,Math.Max(0,-position.Y-delta/120*60));break;}}
                }
                message.Result=IntPtr.Zero;return;
            }
            base.WndProc(ref message);
        }
    }
}