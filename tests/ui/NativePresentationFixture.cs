using System;using System.Collections;using System.Collections.Generic;using System.Linq;using System.Reflection;using System.Windows.Forms;using Moq;using SolidWorks.Interop.sldworks;using SolidWorks.Interop.swconst;using SWSimTool.UI;
public static class NativePresentationTest {
 static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static object Slot(NativeEditorPresenter p,Control source){return ((IDictionary)typeof(NativeEditorPresenter).GetField("slots",flags).GetValue(p)).Values.Cast<object>().Single(s=>Object.ReferenceEquals(s.GetType().GetField("Source").GetValue(s),source));}
 static int Id(NativeEditorPresenter p,Control source){var slot=Slot(p,source);return (int)slot.GetType().GetField("Id").GetValue(slot);}
 static IPropertyManagerPageControl Native(NativeEditorPresenter p,Control source){var slot=Slot(p,source);return (IPropertyManagerPageControl)slot.GetType().GetField("Native").GetValue(slot);}
 static readonly List<NativeEditorPresenter> active=new List<NativeEditorPresenter>();
 public static void CheckAll(){foreach(var p in active)p.Synchronize();}
 public static NativeEditorPresenter Create(Control source,Action require=null){
  var ids=new HashSet<int>();
  var presenter=new NativeEditorPresenter(source,(id,title,tab)=>{
   var g=new Mock<IPropertyManagerPageGroup>();g.SetupAllProperties();
   g.Setup(x=>x.AddControl2(It.IsAny<int>(),It.IsAny<short>(),It.IsAny<string>(),It.IsAny<short>(),It.IsAny<int>(),It.IsAny<string>())).Returns((int i,short type,string caption,short align,int options,string tip)=>{
    Check(ids.Add(i),"Duplicate native control ID");
    switch((swPropertyManagerPageControlType_e)type){
     case swPropertyManagerPageControlType_e.swControlType_Label:{var m=new Mock<IPropertyManagerPageLabel>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
     case swPropertyManagerPageControlType_e.swControlType_Textbox:{var m=new Mock<IPropertyManagerPageTextbox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
     case swPropertyManagerPageControlType_e.swControlType_Combobox:{var m=new Mock<IPropertyManagerPageCombobox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
     case swPropertyManagerPageControlType_e.swControlType_Listbox:{var m=new Mock<IPropertyManagerPageListbox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
     case swPropertyManagerPageControlType_e.swControlType_Checkbox:{var m=new Mock<IPropertyManagerPageCheckbox>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
     case swPropertyManagerPageControlType_e.swControlType_Button:{var m=new Mock<IPropertyManagerPageButton>();m.SetupAllProperties();m.As<IPropertyManagerPageControl>().SetupAllProperties();return (object)m.Object;}
     default:throw new Exception("Embedded/custom control in native parameter group");
    }
   });return g.Object;
  },require??(()=>{}));active.Add(presenter);return presenter;
 }
 public static void Text(NativeEditorPresenter p,TextBox source,string value){p.Synchronize();p.OnText(Id(p,source),value);}
 public static void Choice(NativeEditorPresenter p,ComboBox source,object value){p.Synchronize();p.OnChoice(Id(p,source),source.Items.IndexOf(value));}
 public static void List(NativeEditorPresenter p,ListBox source,int index){p.Synchronize();p.OnList(Id(p,source),index);}
 public static void Press(NativeEditorPresenter p,Button source){p.Synchronize();p.OnButton(Id(p,source));}
 public static void Toggle(NativeEditorPresenter p,CheckBox source,bool value){p.Synchronize();p.OnCheck(Id(p,source),value);}
 public static void Exercise(NativeEditorPresenter p,Control root){
  p.Synchronize();Check(!root.IsHandleCreated,"Native presenter must not create an editor HWND");
  foreach(var source in All(root).Where(x=>x is TextBox||x is ComboBox||x is CheckBox||x is Button||x is ListBox||x is Label)){
   var native=Native(p,source);Check(native!=null,"Missing native field");
   if(native.Visible&&native.Enabled&&source is TextBox){var text=(TextBox)source;string value=text.Text;p.OnText(Id(p,text),value);Check(text.Text==value,"Text callback altered input");}
  }
 }
 static IEnumerable<Control> All(Control root){foreach(Control c in root.Controls){yield return c;foreach(var d in All(c))yield return d;}}
 public static void Run(){
  using(var root=new UserControl()){
   var tabs=new TabControl();root.Controls.Add(tabs);TabPage first=new TabPage("parameters"),second=new TabPage("constraints");tabs.TabPages.Add(first);tabs.TabPages.Add(second);
   var box=new TextBox{Text="7"};first.Controls.Add(box);var choices=new ComboBox();choices.Items.AddRange(new[]{"motor","position"});choices.SelectedIndex=0;first.Controls.Add(choices);var optional=new TextBox{Text="12",Visible=false};first.Controls.Add(optional);choices.SelectedIndexChanged+=(s,e)=>optional.Visible=choices.SelectedIndex==1;
   var list=new ListBox();list.Items.AddRange(new[]{"A","B"});list.SelectedIndex=0;first.Controls.Add(list);var check=new CheckBox();first.Controls.Add(check);var button=new Button{Text="add"};first.Controls.Add(button);int clicked=0;button.Click+=(s,e)=>clicked++;var dynamic=new Panel();second.Controls.Add(dynamic);var template=new TextBox{Text="template",Visible=false};dynamic.Controls.Add(template);
   bool stale=false;using(var p=Create(root,()=>{if(stale)throw new InvalidOperationException("stale");})){
    Exercise(p,root);var steady=Native(p,box);Mock.Get(steady).Invocations.Clear();p.Synchronize();Check(!Mock.Get(steady).Invocations.Any(x=>x.Method.Name.StartsWith("set_")),"Unchanged synchronization must not reset native control state");var original=Native(p,optional);Check(!original.Visible,"Conditional field visible initially");
    p.OnChoice(Id(p,choices),1);Check(original.Visible,"Conditional native field failed to appear");p.OnText(Id(p,optional),"24");p.OnChoice(Id(p,choices),0);p.OnChoice(Id(p,choices),1);Check(optional.Text=="24"&&Object.ReferenceEquals(original,Native(p,optional)),"Toggle lost input/recreated native controls");
    p.OnList(Id(p,list),1);Check(list.SelectedIndex==1,"Native list did not select object");p.OnCheck(Id(p,check),true);Check(check.Checked,"Native check did not update");p.OnButton(Id(p,button));Check(clicked==1,"Native button did not dispatch");
    var nativeBox=(IPropertyManagerPageTextbox)Native(p,box);string nativeText=nativeBox.Text;int calls=0;Mock.Get(nativeBox).SetupGet(x=>x.Text).Returns(()=>nativeText);Mock.Get(nativeBox).SetupSet(x=>x.Text=It.IsAny<string>()).Callback<string>(v=>{nativeText=v;calls++;p.OnText(Id(p,box),v);});box.Text="8";p.Synchronize();Check(calls==1&&box.Text=="8","Synchronous native callback recursion");
    p.OnTab(101);Check(tabs.SelectedIndex==1,"Native tab did not switch controller section");template.Dispose();var old=new TextBox{Text="first"};dynamic.Controls.Add(old);p.Synchronize();var reuse=Native(p,old);old.Dispose();var replacement=new TextBox{Text="second"};dynamic.Controls.Add(replacement);p.Synchronize();Check(Object.ReferenceEquals(reuse,Native(p,replacement)),"Object switch recreated native slot");p.OnText(Id(p,replacement),"changed");Check(replacement.Text=="changed"&&old.IsDisposed,"Old callbacks targeted disposed object");
    stale=true;bool rejected=false;try{p.OnText(Id(p,replacement),"pollution");}catch(InvalidOperationException){rejected=true;}Check(rejected&&replacement.Text=="changed","Stale page changed draft");stale=false;
    Check(!root.IsHandleCreated,"Presenter created editor window");var extra=new TextBox();dynamic.Controls.Add(extra);bool missing=false;try{p.Synchronize();}catch(InvalidOperationException){missing=true;}Check(missing,"Missing template must not allocate controls after page initialization");extra.Dispose();p.Synchronize();p.Dispose();p.OnText(0,"after disposal");
   }
  }
  Console.WriteLine("PASS: native presentation, conditional reuse, list/button/text/check dispatch, reentry, dynamic object slots and stale/disposed callbacks");
 }
}