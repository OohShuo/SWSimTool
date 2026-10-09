using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace SWSimTool.UI {
    // Page-owned native input bindings. No shared business context stores these controls.
    public sealed class NativeParameterFields : IDisposable {
        sealed class Field {
            public int Id; public string Key; public IPropertyManagerPageControl Control,Label;
            public Action Load; public Action<string> Text; public Action<int> Choice;
            public Action<bool> Check; public Action Click; public Func<bool> Visible;
            public Func<string> Caption; public string Error;
        }
        readonly IPropertyManagerPageGroup group;
        readonly List<Field> fields=new List<Field>();
        readonly Dictionary<int,Field> byId=new Dictionary<int,Field>();
        readonly Action requireCurrent;
        int nextId;bool loading,disposed;
        public Action Changed {get;set;}
        public NativeParameterFields(IPropertyManagerPageGroup group,Action requireCurrent,int firstId=1000){this.group=group;this.requireCurrent=requireCurrent;nextId=firstId;}
        object Add(swPropertyManagerPageControlType_e type,string caption,out int id){
            id=nextId++;
            return group.AddControl2(id,(short)type,caption,(short)swPropertyManagerPageControlLeftAlign_e.swControlAlign_Indent,(int)(swAddControlOptions_e.swControlOptions_Visible|swAddControlOptions_e.swControlOptions_Enabled|swAddControlOptions_e.swControlOptions_SmallGapAbove),caption);
        }
        Field Create(string key,Func<string> caption,swPropertyManagerPageControlType_e type,Func<bool> visible,bool label=true){
            if(fields.Any(f=>f.Key==key))throw new ArgumentException("Duplicate native field key: "+key);
            var f=new Field{Key=key,Caption=caption,Visible=visible??(()=>true)};int id;
            if(label)f.Label=(IPropertyManagerPageControl)Add(swPropertyManagerPageControlType_e.swControlType_Label,caption(),out id);
            f.Control=(IPropertyManagerPageControl)Add(type,label?"":caption(),out id);f.Id=id;fields.Add(f);byId.Add(id,f);return f;
        }
        public void Note(string key,Func<string> text,Func<bool> visible=null){var f=Create(key,text,swPropertyManagerPageControlType_e.swControlType_Label,visible,false);}
        public void Number(string key,Func<string> caption,Func<double?> read,Action<double?> write,bool optional=true,Func<bool> visible=null){
            var f=Create(key,caption,swPropertyManagerPageControlType_e.swControlType_Textbox,visible);var box=(IPropertyManagerPageTextbox)f.Control;
            Action validate=()=>{double number;string text=box.Text;bool blank=string.IsNullOrWhiteSpace(text);bool ok=(blank&&optional)||double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out number)&&!double.IsNaN(number)&&!double.IsInfinity(number);f.Error=ok?null:caption()+"：请输入有限数字";};
            f.Load=()=>{box.Text=read()?.ToString("G12",CultureInfo.InvariantCulture)??"";validate();};
            f.Text=text=>{box.Text=text;validate();if(f.Error==null)write(string.IsNullOrWhiteSpace(text)?(double?)null:double.Parse(text,NumberStyles.Float,CultureInfo.InvariantCulture));};
        }
        public void Choice(string key,Func<string> caption,string[] choices,Func<string> read,Action<string> write,Func<bool> visible=null){
            var f=Create(key,caption,swPropertyManagerPageControlType_e.swControlType_Combobox,visible);var box=(IPropertyManagerPageCombobox)f.Control;
            box.Style=(int)swPropMgrPageComboBoxStyle_e.swPropMgrPageComboBoxStyle_EditBoxReadOnly;box.AddItems(choices);
            f.Load=()=>box.CurrentSelection=(short)Array.IndexOf(choices,read());
            f.Choice=index=>{if(index<0||index>=choices.Length)throw new ArgumentException("请选择有效选项："+caption());write(choices[index]);};
        }
        public void Check(string key,Func<string> caption,Func<bool> read,Action<bool> write,Func<bool> visible=null){
            var f=Create(key,caption,swPropertyManagerPageControlType_e.swControlType_Checkbox,visible,false);var box=(IPropertyManagerPageCheckbox)f.Control;
            f.Load=()=>box.Checked=read();f.Check=write;
        }
        public void Button(string key,string caption,Action clicked,Func<bool> visible=null){var f=Create(key,()=>caption,swPropertyManagerPageControlType_e.swControlType_Button,visible,false);f.Click=clicked;}
        public bool HasErrors=>fields.Any(f=>f.Visible()&&f.Error!=null);
        public void Validate(){Require();var errors=fields.Where(f=>f.Visible()&&f.Error!=null).Select(f=>f.Error).ToArray();if(errors.Length>0)throw new InvalidOperationException(string.Join("\n",errors));}
        void Require(){if(disposed)throw new ObjectDisposedException(nameof(NativeParameterFields));requireCurrent?.Invoke();}
        public void LoadValues(params string[] keys){Require();bool previous=loading;loading=true;try{foreach(var f in fields)if(keys.Length==0||keys.Contains(f.Key))f.Load?.Invoke();}finally{loading=previous;}Refresh();}
        public void Refresh(){if(disposed)return;bool previous=loading;loading=true;try{foreach(var f in fields){bool visible=f.Visible();f.Control.Visible=visible;f.Control.Enabled=visible;if(f.Label!=null){f.Label.Visible=visible;((IPropertyManagerPageLabel)f.Label).Caption=f.Caption();}else if(f.Control is IPropertyManagerPageLabel)((IPropertyManagerPageLabel)f.Control).Caption=f.Caption();else if(f.Control is IPropertyManagerPageCheckbox)((IPropertyManagerPageCheckbox)f.Control).Caption=f.Caption();}}finally{loading=previous;}}
        void Dispatch(int id,Action<Field> action){if(loading||disposed)return;Require();Field f;if(!byId.TryGetValue(id,out f))return;if(!f.Visible())return;loading=true;try{action(f);}finally{loading=false;}Refresh();Changed?.Invoke();}
        public void OnText(int id,string value)=>Dispatch(id,f=>f.Text?.Invoke(value));
        public void OnChoice(int id,int value)=>Dispatch(id,f=>{if(DropdownWheelGuard.IsWheelInput)f.Load?.Invoke();else f.Choice?.Invoke(value);});
        public void OnCheck(int id,bool value)=>Dispatch(id,f=>f.Check?.Invoke(value));
        public void OnButton(int id)=>Dispatch(id,f=>f.Click?.Invoke());
        public void Dispose(){disposed=true;Changed=null;byId.Clear();fields.Clear();}
    }
}
