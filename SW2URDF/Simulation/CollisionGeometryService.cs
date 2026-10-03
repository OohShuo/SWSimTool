using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;
using SolidWorks.Interop.sldworks;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.Simulation
{
    public sealed partial class AttachmentService
    {
        public ModelDoc2 Model => exporter.ActiveSWModel;
        public SldWorks App => (SldWorks)exporter.iSwApp;
        public Dictionary<string, Matrix<double>> LinkTransforms()
        {
            var frames = new Dictionary<string, Matrix<double>>();
            var root = exporter.URDFRobot.BaseLink;
            if (root == null) throw new InvalidOperationException("请先配置 URDF link 树。");
            AddTransforms(root, MathOps.GetTransformation(exporter.AttachmentCoordinateTransform(root.Joint.CoordinateSystemName)), frames);
            return frames;
        }
        public CollisionReference CaptureSelection(int index = 1)
        {
            var selection = (SelectionMgr)Model.SelectionManager;
            object entity = selection.GetSelectedObject6(index, -1);
            if (entity == null) throw new InvalidOperationException("请在模型中选择点、坐标系或面，再点击拾取。");
            var component = selection.GetSelectedObjectsComponent4(index, -1) as Component2;
            var pid = Model.Extension.GetPersistReference3(entity) as byte[];
            if (pid == null || pid.Length == 0) throw new InvalidOperationException("该选择不支持持久引用。");
            // Validate type before retaining a reference.
            if (!(entity is Feature) && !(entity is Vertex) && !(entity is SketchPoint) && !(entity is RefPoint) && !(entity is Face2))
                throw new InvalidOperationException("请选择参考点、顶点、草图点、坐标系或几何面。");
            var result = new CollisionReference { pid = Convert.ToBase64String(pid),
                component_pid = component == null ? null : Convert.ToBase64String((byte[])Model.Extension.GetPersistReference3(component)),
                label = (entity is Feature ? ((Feature)entity).Name : entity.GetType().Name) + (component == null ? "" : " <" + component.Name2 + ">") };
            return result;
        }
        object ResolveReference(CollisionReference reference, out Matrix<double> placement)
        {
            int error;
            object value = Model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference.pid), out error);
            if (value == null) throw new InvalidOperationException("参考失效，请重新拾取：" + reference.label);
            placement = DenseMatrix.CreateIdentity(4);
            if (!string.IsNullOrEmpty(reference.component_pid))
            {
                var component = Model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference.component_pid), out error) as Component2;
                if (component == null) throw new InvalidOperationException("零部件参考失效：" + reference.label);
                placement = MathOps.GetTransformation(component.Transform2);
            }
            return value;
        }
        Matrix<double> ReferenceFrame(CollisionReference reference, bool oriented = false)
        {
            Matrix<double> placement;
            var value = ResolveReference(reference, out placement);
            var feature = value as Feature;
            if (feature != null)
            {
                if (feature.GetTypeName2() == "CoordSys")
                {
                    // The coordinate-system definition belongs to the selected component's document.
                    ModelDoc2 doc = Model;
                    if (!string.IsNullOrEmpty(reference.component_pid))
                    {
                        int error;
                        doc = ((Component2)Model.Extension.GetObjectByPersistReference3(Convert.FromBase64String(reference.component_pid), out error)).GetModelDoc2() as ModelDoc2;
                    }
                    return placement * MathOps.GetTransformation(doc.Extension.GetCoordinateSystemTransformByName(feature.Name));
                }
                value = feature.GetSpecificFeature2();
            }
            if (oriented) throw new InvalidOperationException("方向参考必须为坐标系。");
            double[] point;
            if (value is RefPoint) point = (double[])((MathPoint)((RefPoint)value).GetRefPoint()).ArrayData;
            else if (value is Vertex) point = (double[])((Vertex)value).GetPoint();
            else if (value is SketchPoint)
            {
                var p = (SketchPoint)value;
                var sketch = (Sketch)p.GetSketch();
                var local = MathOps.GetTranslation(new[] { p.X, p.Y, p.Z });
                return placement * MathOps.GetTransformation(sketch.ModelToSketchTransform).Inverse() * local;
            }
            else throw new InvalidOperationException("该参考不是点或坐标系。");
            return placement * MathOps.GetTranslation(point);
        }
        static Matrix<double> AxisFrame(double[] center, double[] direction)
        {
            var z = DenseVector.OfArray(direction);
            if(z.L2Norm()<=1e-12)throw new InvalidOperationException("方向向量不能为零。");
            z /= z.L2Norm();
            var hint = DenseVector.OfArray(Math.Abs(z[0]) < .8 ? new[] { 1.0, 0, 0 } : new[] { 0.0, 1, 0 });
            var x = hint - hint.DotProduct(z) * z; x /= x.L2Norm();
            var y = DenseVector.OfArray(new[] { z[1]*x[2]-z[2]*x[1], z[2]*x[0]-z[0]*x[2], z[0]*x[1]-z[1]*x[0] });
            var frame = MathOps.GetTranslation(center);
            for (int i=0;i<3;i++) { frame[i,0]=x[i]; frame[i,1]=y[i]; frame[i,2]=z[i]; }
            return frame;
        }
        public void ResolveCollision(CollisionGeometry geometry)
        {
            var frames = LinkTransforms();
            Matrix<double> link;
            if (!frames.TryGetValue(geometry.link, out link)) throw new InvalidOperationException("碰撞几何体所属 link 不存在：" + geometry.link);
            var refs = geometry.references;
            Matrix<double> basis = null;
            Func<int, double[]> point = i => MathOps.GetXYZ(ReferenceFrame(refs[i]));
            Action<int> require = n => { if (refs == null || refs.Count != n) throw new InvalidOperationException("该定义方式需要 " + n + " 个参考，请按顺序拾取。"); };
            switch (geometry.definition)
            {
                case "manual": break;
                case "frame": require(1); basis = ReferenceFrame(refs[0], true); break;
                case "center": require(1); basis = link.Clone(); var c=point(0); for(int i=0;i<3;i++) basis[i,3]=c[i]; break;
                case "center_frame": require(2); basis = ReferenceFrame(refs[1], true); c=point(0); for(int i=0;i<3;i++) basis[i,3]=c[i]; break;
                case "radius_points":
                    require(2); var p = point(0); var q=point(1); geometry.size=new[] { DenseVector.OfArray(q).Subtract(DenseVector.OfArray(p)).L2Norm() }; basis=MathOps.GetTranslation(p); break;
                case "endpoints":
                    require(2); p=point(0); q=point(1); var delta=q.Zip(p,(va,vb)=>va-vb).ToArray();
                    double length=DenseVector.OfArray(delta).L2Norm(); if(length <= 1e-9) throw new InvalidOperationException("两点不能重合。");
                    geometry.size[1]=length; basis=AxisFrame(p.Zip(q,(va,vb)=>(va+vb)/2).ToArray(),delta); break;
                case "frame_total":
                    require(1); geometry.size[1]=geometry.length_input-2*geometry.size[0]; basis=ReferenceFrame(refs[0],true); break;
                case "sphere_face":
                    require(1); Matrix<double> placement; var face=ResolveReference(refs[0],out placement) as Face2;
                    if(face==null || !((Surface)face.GetSurface()).IsSphere()) throw new InvalidOperationException("请选择球面。");
                    var sp=(double[])((Surface)face.GetSurface()).SphereParams; geometry.size=new[]{sp[3]}; basis=placement*MathOps.GetTranslation(sp.Take(3).ToArray()); break;
                case "cylinder_face":
                    require(3); face=ResolveReference(refs[0],out placement) as Face2;
                    if(face==null || !((Surface)face.GetSurface()).IsCylinder()) throw new InvalidOperationException("第一个参考必须为圆柱面，随后拾取两个轴向范围点。");
                    var cy=(double[])((Surface)face.GetSurface()).CylinderParams;
                    var axis=placement*AxisFrame(cy.Take(3).ToArray(),cy.Skip(3).Take(3).ToArray());
                    var inverse=axis.Inverse();
                    var v1=MathOps.GetXYZ(inverse*MathOps.GetTranslation(point(1))); var v2=MathOps.GetXYZ(inverse*MathOps.GetTranslation(point(2)));
                    geometry.size=new[]{cy[6],Math.Abs(v2[2]-v1[2])}; basis=axis*MathOps.GetTranslation(new[]{0.0,0,(v1[2]+v2[2])/2}); break;
                case "rectangle_face":
                    require(1); face=ResolveReference(refs[0],out placement) as Face2;
                    if(face==null || !((Surface)face.GetSurface()).IsPlane()) throw new InvalidOperationException("请选择矩形平面。");
                    var edges=((object[])face.GetEdges()).Cast<Edge>().ToArray();
                    if(edges.Length!=4 || edges.Any(e=> !((Curve)e.GetCurve()).IsLine())) throw new InvalidOperationException("需要四条直边组成的矩形面，不能包含孔或圆角。");
                    var vertices=edges.SelectMany(e=>new[]{(Vertex)e.GetStartVertex(),(Vertex)e.GetEndVertex()}).Select(v=>(double[])v.GetPoint()).ToList();
                    var origin=vertices[0]; var vectors=vertices.Select(v=>DenseVector.OfArray(v).Subtract(DenseVector.OfArray(origin))).Where(v=>v.L2Norm()>1e-9).GroupBy(v=>string.Join(",",v.Select(coord=>Math.Round(coord,9)))).Select(g=>g.First()).OrderBy(v=>v.L2Norm()).ToArray();
                    if(vectors.Length!=3) throw new InvalidOperationException("矩形顶点无效。");
                    var a=vectors[0]; var b=vectors[1]; var d=vectors[2];
                    if(Math.Abs(a.DotProduct(b))>1e-7*a.L2Norm()*b.L2Norm() || (a+b-d).L2Norm()>1e-7) throw new InvalidOperationException("所选平面不是矩形。");
                    var normal=(double[])face.Normal; basis=AxisFrame(origin.Zip(d,(o,t)=>o+t/2).ToArray(),normal);
                    var x=a/a.L2Norm(); var z=DenseVector.OfArray(normal); z/=z.L2Norm(); var y=DenseVector.OfArray(new[]{z[1]*x[2]-z[2]*x[1],z[2]*x[0]-z[0]*x[2],z[0]*x[1]-z[1]*x[0]});
                    for(int i=0;i<3;i++){basis[i,0]=x[i];basis[i,1]=y[i];basis[i,3]+=z[i]*geometry.thickness*geometry.extrusion/2;}
                    geometry.size=new[]{a.L2Norm(),b.L2Norm(),geometry.thickness}; basis=placement*basis; break;
                default: throw new InvalidOperationException("未知定义方式。");
            }
            if (basis != null)
            {
                var relative=link.Inverse()*basis*MathOps.GetTransformation(geometry.offset_xyz,geometry.offset_rpy);
                // Validate a candidate before replacing the last valid pose.
                var xyz=MathOps.GetXYZ(relative); var rpy=MathOps.GetRPY(relative);
                if(xyz.Concat(rpy).Any(v=>double.IsNaN(v)||double.IsInfinity(v))) throw new InvalidOperationException("参考位姿无效。");
                geometry.xyz=xyz; geometry.rpy=geometry.type=="sphere"?new double[3]:rpy;
            }
            geometry.Validate();
        }
    }
}
