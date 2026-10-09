using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SWSimTool.UI {
    // Presentation adapter during the per-page migration. The existing page-local
    // editor remains the input controller; it is never attached through an HWND.
    // Domain drafts and persistence remain owned by AttachmentService/PageDraft.
    public sealed class NativeEditorPresenter : IDisposable {
        sealed class Slot {
            public int Id; public IPropertyManagerPageControl Native;
            public Control Source; public string[] Items; public bool Active;
        }
        readonly Control editor;
        readonly Action requireCurrent;
        readonly Dictionary<string,Slot> slots=new Dictionary<string,Slot>();
        readonly Dictionary<int,Slot> ids=new Dictionary<int,Slot>();
        readonly List<Tuple<Control,IPropertyManagerPageGroup,string>> regions=new List<Tuple<Control,IPropertyManagerPageGroup,string>>();
        readonly Timer timer=new Timer {Interval=250};
        readonly TabControl tabs;

        int nextId=2000;
        bool syncing,disposed,sealedControls;
        // Control.Visible incorporates the visibility of a native-less parent.
        // Read the local flag instead, then apply the selected section explicitly.
        static readonly MethodInfo getState=typeof(Control).GetMethod("GetState",BindingFlags.Instance|BindingFlags.NonPublic);
        static readonly MethodInfo buttonClick=typeof(Button).GetMethod("OnClick",BindingFlags.Instance|BindingFlags.NonPublic);
        public NativeEditorPresenter(Control editor,Func<int,string,int,object> addGroup,Action requireCurrent) {
            this.editor=editor;this.requireCurrent=requireCurrent;
            try {
                tabs=editor.Controls.OfType<TabControl>().Single();
                if(tabs.SelectedIndex<0&&tabs.TabPages.Count>0)tabs.SelectedIndex=0;
                int groupId=31;
                foreach(Control top in editor.Controls.Cast<Control>().Where(c=>c!=tabs&&c.Dock!=DockStyle.Bottom))
                    regions.Add(Tuple.Create(top,(IPropertyManagerPageGroup)addGroup(groupId++,"所属对象",-1),"top"+groupId));
                foreach(TabPage tab in tabs.TabPages)
                    regions.Add(Tuple.Create((Control)tab,(IPropertyManagerPageGroup)addGroup(groupId++,tab.Text,tabs.TabPages.IndexOf(tab)),"tab"+groupId));
                foreach(Control footer in editor.Controls.Cast<Control>().Where(c=>c.Dock==DockStyle.Bottom))
                    regions.Add(Tuple.Create(footer,(IPropertyManagerPageGroup)addGroup(groupId++,"预览与保存",-1),"footer"+groupId));
                Synchronize();sealedControls=true;
                timer.Tick+=(s,e)=>{try{Synchronize();}catch(Exception ex){timer.Stop();Error?.Invoke(ex);}};
                timer.Start();
            } catch {Dispose();throw;}
        }
        public Action<Exception> Error {get;set;}
        static bool LocalVisible(Control c)=>(bool)getState.Invoke(c,new object[]{2});
        static IEnumerable<Control> Ordered(Control parent) {
            var table=parent as TableLayoutPanel;
            return table==null?parent.Controls.Cast<Control>():parent.Controls.Cast<Control>().OrderBy(c=>table.GetRow(c)).ThenBy(c=>table.GetColumn(c));
        }
        static swPropertyManagerPageControlType_e? Kind(Control c) {
            if(c is TextBox)return swPropertyManagerPageControlType_e.swControlType_Textbox;
            if(c is ComboBox)return swPropertyManagerPageControlType_e.swControlType_Combobox;
            if(c is ListBox)return swPropertyManagerPageControlType_e.swControlType_Listbox;
            if(c is CheckBox)return swPropertyManagerPageControlType_e.swControlType_Checkbox;
            if(c is Button)return swPropertyManagerPageControlType_e.swControlType_Button;
            if(c is Label)return swPropertyManagerPageControlType_e.swControlType_Label;
            return null;
        }
        void Visit(Control source,IPropertyManagerPageGroup group,string path,bool visible,bool enabled) {
            visible&=source is TabPage||LocalVisible(source);enabled&=source.Enabled;
            var kind=Kind(source);
            if(kind.HasValue) {
                string key=path+":"+kind.Value;
                Slot slot;
                if(!slots.TryGetValue(key,out slot)) {
                    if(sealedControls)throw new InvalidOperationException("原生字段模板缺失："+key+"。请关闭页面后重试；未写入配置。");
                    int id=nextId++;
                    var native=(IPropertyManagerPageControl)group.AddControl2(id,(short)kind.Value,source.Text,(short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,(int)(swAddControlOptions_e.swControlOptions_Visible|swAddControlOptions_e.swControlOptions_Enabled|swAddControlOptions_e.swControlOptions_SmallGapAbove),source.Text);
                    slot=new Slot{Id=id,Native=native};slots.Add(key,slot);ids.Add(id,slot);
                    if(source is ComboBox)((IPropertyManagerPageCombobox)native).Style=(int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;
                    if(source is ListBox)((IPropertyManagerPageListbox)native).Height=55;
                }
                slot.Source=source;slot.Active=visible&&InSelectedTab(source);
                if(source is TextBox) {var box=(IPropertyManagerPageTextbox)slot.Native;if(box.Text!=source.Text)box.Text=source.Text;}
                else if(source is ComboBox) {
                    var box=(ComboBox)source;var native=(IPropertyManagerPageCombobox)slot.Native;
                    var items=box.Items.Cast<object>().Select(x=>box.GetItemText(x)).ToArray();
                    if(slot.Items==null||!slot.Items.SequenceEqual(items)){native.Clear();if(items.Length>0)native.AddItems(items);slot.Items=items;}
                    if(native.CurrentSelection!=box.SelectedIndex)native.CurrentSelection=(short)box.SelectedIndex;
                } else if(source is ListBox) {
                    var box=(ListBox)source;var native=(IPropertyManagerPageListbox)slot.Native;
                    var items=box.Items.Cast<object>().Select(x=>box.GetItemText(x)).ToArray();
                    if(slot.Items==null||!slot.Items.SequenceEqual(items)){native.Clear();if(items.Length>0)native.AddItems(items);slot.Items=items;}
                    if(native.CurrentSelection!=box.SelectedIndex)native.CurrentSelection=(short)box.SelectedIndex;
                } else if(source is CheckBox) {var native=(IPropertyManagerPageCheckbox)slot.Native;if(native.Caption!=source.Text)native.Caption=source.Text;if(native.Checked!=((CheckBox)source).Checked)native.Checked=((CheckBox)source).Checked;}
                else if(source is Button){var native=(IPropertyManagerPageButton)slot.Native;if(native.Caption!=source.Text)native.Caption=source.Text;}
                else {var native=(IPropertyManagerPageLabel)slot.Native;if(native.Caption!=source.Text)native.Caption=source.Text;}
                if(slot.Native.Visible!=visible)slot.Native.Visible=visible;bool interactive=visible&&enabled;if(slot.Native.Enabled!=interactive)slot.Native.Enabled=interactive;
                return;
            }
            int index=0;foreach(var child in Ordered(source))Visit(child,group,path+"/"+index++,visible,enabled);
        }
        bool InSelectedTab(Control source){for(var c=source;c!=null;c=c.Parent)if(c is TabPage)return c==tabs.SelectedTab;return true;}
        public void Synchronize() {
            if(disposed||syncing)return;
            syncing=true;
            try {
                foreach(var slot in slots.Values){slot.Active=false;slot.Source=null;}
                foreach(var region in regions) {
                    bool visible=region.Item1 is TabPage||LocalVisible(region.Item1);
                    if(region.Item2.Visible!=visible)region.Item2.Visible=visible;
                    Visit(region.Item1,region.Item2,region.Item3,visible,editor.Enabled);
                }
                foreach(var slot in slots.Values.Where(s=>s.Source==null)){if(slot.Native.Visible)slot.Native.Visible=false;if(slot.Native.Enabled)slot.Native.Enabled=false;}
            } finally {syncing=false;}
        }
        void Dispatch(int id,Action<Control> action) {
            if(syncing||disposed)return;
            requireCurrent();
            Slot slot;if(!ids.TryGetValue(id,out slot)||!slot.Active||slot.Source==null||slot.Source.IsDisposed||!slot.Native.Enabled)return;
            syncing=true;try{action(slot.Source);}finally{syncing=false;Synchronize();}
        }
        public void OnText(int id,string value)=>Dispatch(id,c=>((TextBox)c).Text=value);
        public void OnCheck(int id,bool value)=>Dispatch(id,c=>((CheckBox)c).Checked=value);
        public void OnButton(int id)=>Dispatch(id,c=>buttonClick.Invoke(c,new object[]{EventArgs.Empty}));
        public void OnList(int id,int value)=>Dispatch(id,c=>{var box=(ListBox)c;if(value>=-1&&value<box.Items.Count)box.SelectedIndex=value;});
        public void OnChoice(int id,int value) {
            if(syncing||disposed)return;
            if(DropdownWheelGuard.IsWheelInput){Synchronize();return;}
            Dispatch(id,c=>{var box=(ComboBox)c;if(value>=-1&&value<box.Items.Count)box.SelectedIndex=value;});
        }
        public bool OnTab(int id){if(disposed||!editor.Enabled)return false;requireCurrent();int index=id-100;if(index<0||index>=tabs.TabPages.Count)return true;tabs.SelectedIndex=index;Synchronize();return true;}
        public void Guard(Action action){try{action();}catch(Exception ex){Error?.Invoke(ex is TargetInvocationException&&ex.InnerException!=null?ex.InnerException:ex);}}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Dispose();Error=null;ids.Clear();slots.Clear();regions.Clear();}
    }
}
