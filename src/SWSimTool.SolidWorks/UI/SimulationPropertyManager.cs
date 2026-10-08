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
    public sealed class SimulationPropertyManager : PropertyManagerPage2Handler9
    {
        PropertyManagerPage2 page;
        readonly string title;
        PropertyManagerPageSelectionbox selection;
        SimulationEditorControl editor;
        bool retry;
        readonly PropertyManagerTransition transition=new PropertyManagerTransition();
        public Action Closed { get; set; }
        public SimulationPropertyManager(AttachmentService service, string selectedLink, System.Collections.Generic.Dictionary<string,string> joints,bool constraintsOnly=false)
        {try{
            // Export/coordinate-frame construction can leave components selected.
            // Do not seed the reference picker with those unrelated selections.
            service.Model.ClearSelection2(true);
            title=constraintsOnly?"SWSimTool 约束配置":"SWSimTool 仿真配置";
            int error=0;
            page=(PropertyManagerPage2)service.App.CreatePropertyManagerPage(title,(int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_OkayButton | (int)swPropertyManagerPageOptions_e.swPropertyManagerOptions_CancelButton,this,ref error);
            if(page==null||error!=0)throw new InvalidOperationException("无法创建左侧配置页面："+error);
            if(!constraintsOnly){selection=(PropertyManagerPageSelectionbox)page.AddControl2(1,(short)swPropertyManagerPageControlType_e.swControlType_Selectionbox,"模型参考拾取",0,3,"点击下方拾取按钮，再选择模型几何对象");
            selection.SingleEntityOnly=true;
            selection.SetSelectionFilters(new[]{(int)swSelectType_e.swSelVERTICES,(int)swSelectType_e.swSelDATUMPOINTS,(int)swSelectType_e.swSelSKETCHPOINTS,(int)swSelectType_e.swSelCOORDSYS,(int)swSelectType_e.swSelFACES,(int)swSelectType_e.swSelEDGES});}
            var window=(PropertyManagerPageWindowFromHandle)page.AddControl2(2,(short)swPropertyManagerPageControlType_e.swControlType_WindowFromHandle,title,0,3,"");
            window.Height=270;
            editor=new SimulationEditorControl(service,selectedLink,joints,constraintsOnly);
            if(!constraintsOnly)editor.BeginSelection=()=>{service.Model.ClearSelection2(true);selection.SetSelectionFocus();};
            window.SetWindowHandlex64(editor.Handle.ToInt64());service.PageDraft.Close=()=>page.Close(false);
        }catch{editor?.Dispose();transition.Dispose();throw;}}
        public void Show(){try{page.Show2(0);}catch{editor.Dispose();transition.Dispose();throw;}}
        void IPropertyManagerPage2Handler9.AfterActivation() {  }
        void IPropertyManagerPage2Handler9.OnButtonPress(int Id) {  }
        void IPropertyManagerPage2Handler9.OnClose(int Reason) { if(Reason==(int)swPropertyManagerPageCloseReasons_e.swPropertyManagerPageClose_Okay) try{editor.Save();}catch(Exception e){retry=true;MessageBox.Show(e.Message,title+"未保存");} }
        void IPropertyManagerPage2Handler9.OnGainedFocus(int Id) {  }
        bool IPropertyManagerPage2Handler9.OnHelp() { return true; }
        bool IPropertyManagerPage2Handler9.OnKeystroke(int Wparam, int Message, int Lparam, int Id) { return false; }
        void IPropertyManagerPage2Handler9.OnLostFocus(int Id) {  }
        void IPropertyManagerPage2Handler9.OnNumberboxChanged(int Id, double Value) {  }
        void IPropertyManagerPage2Handler9.OnSelectionboxFocusChanged(int Id) {  }
        void IPropertyManagerPage2Handler9.OnSelectionboxListChanged(int Id, int Count) { if(Count>0)editor?.CaptureSelected(); }
        bool IPropertyManagerPage2Handler9.OnSubmitSelection(
            int Id, object Selection, int SelType, ref string ItemText) { return true; }
        void IPropertyManagerPage2Handler9.OnTextboxChanged(int Id, string Text) {  }
        int IPropertyManagerPage2Handler9.OnWindowFromHandleControlCreated(int Id, bool Status) { return 0; }
        void IPropertyManagerPage2Handler9.OnCheckboxCheck(int Id, bool Checked) {  }
        void IPropertyManagerPage2Handler9.OnComboboxEditChanged(int Id, string Text) {  }
        void IPropertyManagerPage2Handler9.OnComboboxSelectionChanged(int Id, int Item) {  }
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
        void IPropertyManagerPage2Handler9.AfterClose() { if(retry){retry=false;transition.Post(()=>page.Show2(0));}else {editor.Dispose();editor=null;page=null;selection=null;var closed=Closed;Closed=null;transition.Post(()=>{try{closed?.Invoke();}finally{transition.Dispose();}});} }
        int IPropertyManagerPage2Handler9.OnActiveXControlCreated(int Id, bool Status) { return 0; }
    }
}
