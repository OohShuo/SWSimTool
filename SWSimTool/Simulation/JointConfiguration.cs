using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SWSimTool.Simulation {
 public sealed class JointConfiguration {
  public string joint_id {get;set;}
  public string joint {get;set;}
  public string type {get;set;}="inherit";
  public double? damping {get;set;}
  public double? frictionloss {get;set;}
  public double? armature {get;set;}
  public string spring_mode {get;set;}="inherit";
  public double? stiffness {get;set;}
  public double? springref {get;set;}
  public double? @ref {get;set;}
  public double[] pos {get;set;}
  public double[] axis {get;set;}
  public CollisionReference position_reference {get;set;}
  public CollisionReference axis_reference {get;set;}
  public string limit_mode {get;set;}="inherit";
  public double? lower {get;set;}
  public double? upper {get;set;}
  public double? margin {get;set;}
  public ConstraintSettings limit_solver {get;set;}
  public ConstraintSettings friction_solver {get;set;}
  public static ConstraintSettings LimitDefaults()=>new ConstraintSettings{timeconst=.003,dmin=.99,dmax=.995};
  public void Validate(){
   if(string.IsNullOrWhiteSpace(joint)||!new[]{"inherit","hinge","slide"}.Contains(type)||!new[]{"inherit","none","custom"}.Contains(limit_mode)||!new[]{"inherit","off","custom"}.Contains(spring_mode))throw new InvalidDataException("关节名称、类型或限位模式无效。");
   foreach(var value in new[]{damping,frictionloss,armature,stiffness,margin})if(value.HasValue&&(!Finite(value.Value)||value<0))throw new InvalidDataException("关节阻尼、摩擦、惯量、刚度、提前量必须是非负有限数字。");
   foreach(var value in new[]{springref,@ref,lower,upper})if(value.HasValue&&!Finite(value.Value))throw new InvalidDataException("关节位置参数必须为有限数字。");
   if(limit_mode=="custom"&&(!lower.HasValue||!upper.HasValue||lower>=upper))throw new InvalidDataException("请填写关节限位上下限，且下限小于上限。");
   limit_solver?.Validate(false);friction_solver?.Validate(false);
  }
  static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);
 }
 public sealed class JointForceLimit {
  public string joint_id {get;set;}
  public string joint {get;set;}
  public double? lower {get;set;}
  public double? upper {get;set;}
  public void Validate(){if(string.IsNullOrWhiteSpace(joint)||!lower.HasValue||!upper.HasValue||double.IsNaN(lower.Value)||double.IsNaN(upper.Value)||double.IsInfinity(lower.Value)||double.IsInfinity(upper.Value)||lower>=upper)throw new InvalidDataException("请填写关节总驱动力上下限，且下限小于上限。");}
 }
 public sealed class JointDescriptor {
  public string id,name,parent,child,type,axis_name;
  public double[] axis;
  public double? lower,upper;
  public static List<JointDescriptor> FromTree(SWSimTool.URDF.LinkNode root){
   var result=new List<JointDescriptor>();Action<SWSimTool.URDF.LinkNode> visit=null;
   visit=n=>{foreach(SWSimTool.URDF.LinkNode c in n.Nodes){var j=c.Link.Joint;var d=new JointDescriptor{id=j.StableId,name=j.Name,parent=n.Link.Name,child=c.Link.Name,type=j.Type,axis_name=j.AxisName,axis=j.Axis.GetXYZ()};try{d.lower=j.Limit.Lower;d.upper=j.Limit.Upper;}catch(NullReferenceException){}result.Add(d);visit(c);}};visit(root);return result;
  }
 }
}
