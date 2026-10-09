using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;

namespace SWSimTool.UI {
    // Observe only this UI thread. Never suppress messages in other applications
    // or unrelated SW controls; our own selection callbacks restore their values.
    public sealed class DropdownWheelGuard : IDisposable {
        [ThreadStatic] static State state;
        sealed class State {
            public int Users;public bool Wheel;public IntPtr Hook;public HookProc Callback;
            public IntPtr Observe(int code,IntPtr removed,IntPtr address){
                if(code>=0&&removed==new IntPtr(1)){
                    try{var message=(NativeMessage)Marshal.PtrToStructure(address,typeof(NativeMessage));Wheel=message.Message==0x020A||message.Message==0x020E;}catch{Wheel=false;}
                }
                return CallNextHookEx(Hook,code,removed,address);
            }
        }
        [StructLayout(LayoutKind.Sequential)] struct NativeMessage {public IntPtr Window;public uint Message;public UIntPtr WParam;public IntPtr LParam;public uint Time;public int X,Y;public uint Private;}
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc callback,IntPtr module,uint thread);
        [DllImport("user32.dll",SetLastError=true)] static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        readonly State owned;
        readonly Dictionary<int,IPropertyManagerPageCombobox> boxes=new Dictionary<int,IPropertyManagerPageCombobox>();
        readonly Dictionary<int,short> selections=new Dictionary<int,short>();
        bool disposed,restoring;
        public static bool IsWheelInput=>state!=null&&state.Wheel;
        public DropdownWheelGuard(){
            if(state==null){var created=new State();created.Callback=created.Observe;created.Hook=SetWindowsHookEx(3,created.Callback,IntPtr.Zero,GetCurrentThreadId());if(created.Hook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"无法安装下拉框滚轮保护。");state=created;}
            owned=state;owned.Users++;
        }
        public void Register(int id,IPropertyManagerPageCombobox box){boxes.Add(id,box);selections.Add(id,box.CurrentSelection);}
        public bool RejectSelection(int id){
            if(restoring)return true;
            IPropertyManagerPageCombobox box;if(!boxes.TryGetValue(id,out box))return false;
            if(IsWheelInput){restoring=true;try{box.CurrentSelection=selections[id];}finally{restoring=false;}return true;}
            selections[id]=box.CurrentSelection;return false;
        }
        public void Dispose(){if(disposed)return;if(!ReferenceEquals(state,owned))throw new InvalidOperationException("下拉框保护必须在创建它的 UI 线程释放。");if(owned.Users==1){if(!UnhookWindowsHookEx(owned.Hook))throw new Win32Exception(Marshal.GetLastWin32Error());state=null;owned.Wheel=false;}owned.Users--;disposed=true;boxes.Clear();selections.Clear();}
    }
}