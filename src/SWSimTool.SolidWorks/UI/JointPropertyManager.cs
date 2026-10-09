using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;
using SWSimTool.Simulation;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SWSimTool.UI
{
    [ComVisible(true)]
    public sealed class JointPropertyManager : PropertyManagerPage2Handler9
    {
        PropertyManagerPage2 page;
        NativeJointEditor editor;
        bool retry;
        readonly PropertyManagerTransition transition=new PropertyManagerTransition();
        public Action Closed { get; set; }
        public JointPropertyManager(AttachmentService service, System.Collections.Generic.List<JointDescriptor> joints)
        {try{
            // Export/coordinate-frame construction can leave components selected.
            // Do not seed the reference picker with those unrelated selections.
            service.Model.ClearSelection2(true);
            int error=0;
            page=(PropertyManagerPage2)service.App.CreatePropertyManagerPage("SWSimTool 关节配置",(int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton | (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton,this,ref error);
            if(page==null||error!=0)throw new InvalidOperationException("无法创建左侧关节配置页面："+error);
            var group=(IPropertyManagerPageGroup)page.AddGroupBox(10,"关节参数",(int)(swAddGroupBoxOptions_e.swGroupBoxOptions_Visible|swAddGroupBoxOptions_e.swGroupBoxOptions_Expanded));
            editor=new NativeJointEditor(service,joints,group);
            service.PageDraft.Close=()=>page.Close(false);
        }catch{editor?.Dispose();transition.Dispose();throw;}}
        public void Show(){try{page.Show2(0);}catch{editor.Dispose();transition.Dispose();throw;}}
        void IPropertyManagerPage2Handler9.AfterActivation() {  }
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id) { editor?.Guard(()=>editor.Fields.OnButton(Id)); }
        void IPropertyManagerPage2Handler9.OnClose(int Reason) { if(Reason==(int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay) try{var result=editor.Save();if(!result.RefreshSucceeded)MessageBox.Show(result.Message,"配置已保存，刷新失败");}catch(Exception e){retry=true;MessageBox.Show(e.Message,"关节配置未保存");} }
        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id) {  }
        bool IPropertyManagerPage2Handler9.OnHelp() { return true; }
        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id) { return false; }
        void IPropertyManagerPage2Handler9.OnLostFocus(int Id) {  }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) {  }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) {  }
        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count) {  }
        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText) { return true; }
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text) { editor?.Guard(()=>editor.Fields.OnText(Id,Text)); }
        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status) { return 0; }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) { editor?.Guard(()=>editor.Fields.OnCheck(Id,Checked)); }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) {  }
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item) { editor?.Guard(()=>editor.Fields.OnChoice(Id,Item)); }
        void IPropertyManagerPage2Handler9.OnGroupCheck(int Id, bool Checked) {  }
        void IPropertyManagerPage2Handler9.OnGroupExpand(int Id, bool Expanded) {  }
        void IPropertyManagerPage2Handler9.OnListboxSelectionChanged(int Id, int Item) {  }
        bool IPropertyManagerPage2Handler9.OnNextPage() { return true; }
        void IPropertyManagerPage2Handler9.OnOptionCheck(int Id) {  }
        void IPropertyManagerPage2Handler9.OnPopupMenuItem(int Id) {  }
        void IPropertyManagerPage2Handler9.OnPopupMenuItemUpdate(int Id, ref int retval) {  }
        bool IPropertyManagerPage2Handler9.OnPreview() { return true; }
        bool IPropertyManagerPage2Handler9.OnPreviousPage() { return true; }
        void IPropertyManagerPage2Handler9.OnRedo() {  }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutCreated(int Id) {  }
        void IPropertyManagerPage2Handler9.OnSelectionboxCalloutDestroyed(int Id) {  }
        void IPropertyManagerPage2Handler9.OnSliderPositionChanged(int Id, double Value) {  }
        void IPropertyManagerPage2Handler9.OnSliderTrackingCompleted(int Id, double Value) {  }
        bool IPropertyManagerPage2Handler9.OnTabClicked(int Id) { return true; }
        void IPropertyManagerPage2Handler9.OnUndo() {  }
        void IPropertyManagerPage2Handler9.OnWhatsNew() {  }
        void IPropertyManagerPage2Handler9.OnListboxRMBUp(int Id, int PosX, int PosY) {  }
        void IPropertyManagerPage2Handler9.OnNumberBoxTrackingCompleted(int Id, double Value) {  }
        void IPropertyManagerPage2Handler9.AfterClose() { if(retry){retry=false;transition.Post(()=>page.Show2(0));}else {editor.Dispose();editor=null;page=null;var closed=Closed;Closed=null;transition.Post(()=>{try{closed?.Invoke();}finally{transition.Dispose();}});} }
        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status) { return 0; }
    }
}
