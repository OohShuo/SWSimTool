using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SWSimTool.Simulation {
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
