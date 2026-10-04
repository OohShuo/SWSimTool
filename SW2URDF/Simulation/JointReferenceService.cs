using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.Simulation {
 public sealed partial class AttachmentService {
  public double[] JointPosition(CollisionReference reference,string child)=>MathOps.GetXYZ(LinkTransforms()[child].Inverse()*ReferenceFrame(reference));
  public double[] JointAxis(CollisionReference reference,string child){
   Matrix<double> placement;var obj=ResolveReference(reference,out placement);var feature=obj as Feature;
   double[] global;
   if(feature!=null&&feature.GetTypeName2()=="CoordSys"){
    var frame=ReferenceFrame(reference,true);global=Enumerable.Range(0,3).Select(i=>frame[i,2]).ToArray();
   }else{
    var value=feature!=null?feature.GetSpecificFeature2():obj;double[] local;
    if(value is RefAxis){var p=(double[])((RefAxis)value).GetRefAxisParams();local=Enumerable.Range(0,3).Select(i=>p[i]-p[i+3]).ToArray();}
    else if(value is Edge&&((Curve)((Edge)value).GetCurve()).IsLine()){
     var a=(double[])((Vertex)((Edge)value).GetStartVertex()).GetPoint();var b=(double[])((Vertex)((Edge)value).GetEndVertex()).GetPoint();local=Enumerable.Range(0,3).Select(i=>b[i]-a[i]).ToArray();
    }else throw new InvalidOperationException("方向参考请选择基准轴、直边或坐标系（+Z）。");
    global=Enumerable.Range(0,3).Select(i=>Enumerable.Range(0,3).Sum(k=>placement[i,k]*local[k])).ToArray();
   }
   var link=LinkTransforms()[child];var result=Enumerable.Range(0,3).Select(i=>Enumerable.Range(0,3).Sum(k=>link[k,i]*global[k])).ToArray();double length=Math.Sqrt(result.Sum(x=>x*x));
   if(length<1e-12)throw new InvalidOperationException("关节轴向不能为零。");return result.Select(x=>x/length).ToArray();
  }
  public double[] OriginalJointAxis(JointDescriptor joint){
   if(!string.IsNullOrWhiteSpace(joint.axis_name)&&joint.axis_name!="Automatically Generate"&&joint.axis_name!="None"){
    string name=joint.axis_name;ModelDoc2 doc=Model;Component2 component=null;int start=name.IndexOf('<'),end=name.LastIndexOf('>');
    if(start>=0&&end>start){string componentName=name.Substring(start+1,end-start-1);name=name.Substring(0,start).Trim();var assembly=Model as AssemblyDoc;if(assembly!=null)component=((object[])assembly.GetComponents(false)??new object[0]).Cast<Component2>().FirstOrDefault(c=>c.Name2==componentName);if(component==null)throw new InvalidOperationException("关节轴零部件参考失效："+joint.axis_name);doc=component.GetModelDoc2() as ModelDoc2;}
    Feature feature=null;if(doc!=null)for(var candidate=doc.FirstFeature();candidate!=null;candidate=candidate.GetNextFeature()){if(candidate.Name==name){feature=candidate;break;}}if(feature==null)throw new InvalidOperationException("关节轴参考失效："+joint.axis_name);
    // Resolve with the same component placement without changing document selection.
    var parameters=(double[])((RefAxis)feature.GetSpecificFeature2()).GetRefAxisParams();var direction=Enumerable.Range(0,3).Select(i=>parameters[i]-parameters[i+3]).ToArray();
    var placement=component==null?MathOps.GetTransformation(new double[3],new double[3]):MathOps.GetTransformation(component.Transform2);var frame=LinkTransforms()[joint.child];
    var global=Enumerable.Range(0,3).Select(i=>Enumerable.Range(0,3).Sum(k=>placement[i,k]*direction[k])).ToArray();var axis=Enumerable.Range(0,3).Select(i=>Enumerable.Range(0,3).Sum(k=>frame[k,i]*global[k])).ToArray();double length=Math.Sqrt(axis.Sum(x=>x*x));if(length<1e-12)throw new InvalidOperationException("原关节轴无效。");return axis.Select(x=>x/length).ToArray();
   }
   if(joint.axis==null||joint.axis.Sum(x=>x*x)<1e-24)throw new InvalidOperationException("原关节轴尚未定义，请在 URDF 配置中指定。");return joint.axis;
  }
  public void ResolveJointReferences(SimulationProject project){
   Dictionary<string,string> owners;
   if(collisionTree!=null)owners=SW2URDF.UI.SimulationConfigForm.JointOwners(collisionTree);
   else{owners=new Dictionary<string,string>();Action<SW2URDF.URDF.Link> visit=null;visit=link=>{foreach(var child in link.Children){owners[child.Joint.Name]=child.Name;visit(child);}};visit(exporter.URDFRobot.BaseLink);}
   foreach(var joint in project.joints){string child;if(!owners.TryGetValue(joint.joint,out child))throw new InvalidOperationException("关节参考失效："+joint.joint);if(joint.position_reference!=null)joint.pos=JointPosition(joint.position_reference,child);if(joint.axis_reference!=null)joint.axis=JointAxis(joint.axis_reference,child);}
  }
  public IEnumerable<CollisionGeometry> JointPreview(JointDescriptor descriptor,JointConfiguration config){
   var axis=config.axis_reference!=null?JointAxis(config.axis_reference,descriptor.child):config.axis??OriginalJointAxis(descriptor);var position=config.position_reference!=null?JointPosition(config.position_reference,descriptor.child):config.pos??new double[3];var frame=AxisFrame(position,axis);
   var middle=frame*MathOps.GetTranslation(new[]{0.0,0,.025});
   var result=new List<CollisionGeometry>{new CollisionGeometry{id="joint-axis",name=descriptor.name,link=descriptor.child,type="cylinder",size=new[]{.001,.05},xyz=MathOps.GetXYZ(middle),rpy=MathOps.GetRPY(middle)}};
   bool hinge=(config.type=="inherit"?descriptor.type!="prismatic":config.type=="hinge");bool limited=config.limit_mode=="custom"||(config.limit_mode=="inherit"&&descriptor.type!="continuous");var lower=config.limit_mode=="custom"?config.lower:descriptor.lower;var upper=config.limit_mode=="custom"?config.upper:descriptor.upper;
   if(limited&&lower.HasValue&&upper.HasValue&&upper>lower){for(int i=0;i<=20;i++){double q=lower.Value+(upper.Value-lower.Value)*i/20-(config.@ref??0);var point=frame*MathOps.GetTranslation(hinge?new[]{.025*Math.Cos(q),.025*Math.Sin(q),0}:new[]{0.0,0,q});result.Add(new CollisionGeometry{id="joint-limit-"+i,name=descriptor.name,link=descriptor.child,type="sphere",size=new[]{.0015},xyz=MathOps.GetXYZ(point),rpy=new double[3]});}}
   return result;
  }
 }
}
