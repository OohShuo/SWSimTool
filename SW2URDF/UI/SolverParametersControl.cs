using SW2URDF.Simulation;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SW2URDF.UI {
 public sealed class SolverParametersControl : UserControl {
  readonly HashSet<TextBox> invalid;
  public SolverParametersControl(ConstraintSettings existing,ConstraintSettings defaults,bool contact,Action<ConstraintSettings> changed,HashSet<TextBox> invalid,bool optional=true){
   this.invalid=invalid;Dock=DockStyle.Top;AutoSize=true;
   var serializer=new JavaScriptSerializer();var value=existing??serializer.Deserialize<ConstraintSettings>(serializer.Serialize(defaults));
   var layout=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,ColumnCount=1};Controls.Add(layout);
   var fields=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,ColumnCount=1};
   if(optional){var enabled=new CheckBox{Text="单独设置求解参数（否则继承全局 / MuJoCo 默认）",AutoSize=true,Checked=existing!=null};layout.Controls.Add(enabled);fields.Enabled=enabled.Checked;enabled.CheckedChanged+=(s,e)=>{fields.Enabled=enabled.Checked;if(!enabled.Checked){foreach(var box in Children(fields).OfType<TextBox>())invalid.Remove(box);}else{foreach(var box in Children(fields).OfType<TextBox>())Check(box,value,(PropertyInfo)box.Tag,invalid);}changed(enabled.Checked?value:null);};}
   layout.Controls.Add(fields);
   AddNumber(fields,value,"timeconst","solref 时间常数 s",invalid);AddNumber(fields,value,"dampratio","solref 阻尼比",invalid);
   AddNumber(fields,value,"dmin","solimp 起始阻抗",invalid);AddNumber(fields,value,"dmax","solimp 末端阻抗",invalid);AddNumber(fields,value,"width","solimp 变化宽度 m",invalid);AddNumber(fields,value,"midpoint","solimp 中点比例",invalid);AddNumber(fields,value,"power","solimp 曲线指数",invalid);
   if(contact){AddNumber(fields,value,"margin","提前接触 margin m",invalid);AddNumber(fields,value,"condim","接触维数（1 / 3 / 4 / 6）",invalid);}
  }
  public static IEnumerable<Control> Children(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Children(c))yield return child;}}
  public static void AddNumber(TableLayoutPanel panel,object value,string key,string title,HashSet<TextBox> invalid){
   var property=value.GetType().GetProperty(key);var box=new TextBox{Text=Convert.ToDouble(property.GetValue(value)).ToString("G12",CultureInfo.InvariantCulture),Dock=DockStyle.Top,Tag=property};
   panel.Controls.Add(new Label{Text=title,AutoSize=true});panel.Controls.Add(box);
   box.TextChanged+=(s,e)=>{if(box.Enabled)Check(box,value,property,invalid);};box.Disposed+=(s,e)=>invalid.Remove(box);
  }
  static void Check(TextBox box,object value,PropertyInfo property,HashSet<TextBox> invalid){
   double n;bool valid=double.TryParse(box.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)&&!double.IsNaN(n)&&!double.IsInfinity(n)&&(property.PropertyType!=typeof(int)||(n==Math.Truncate(n)&&n>=int.MinValue&&n<=int.MaxValue));
   if(valid){property.SetValue(value,property.PropertyType==typeof(int)?(object)(int)n:n);invalid.Remove(box);box.BackColor=System.Drawing.SystemColors.Window;}else{invalid.Add(box);box.BackColor=System.Drawing.Color.MistyRose;}
  }
  protected override void Dispose(bool disposing){if(disposing)foreach(var box in Children(this).OfType<TextBox>())invalid.Remove(box);base.Dispose(disposing);}
 }
}
