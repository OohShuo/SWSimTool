using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SW2URDF.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SW2URDF.Simulation
{
    public sealed class CollisionPreview : IDisposable
    {
        readonly AttachmentService service;
        readonly List<Body2> bodies = new List<Body2>();
        object host;
        public CollisionPreview(AttachmentService service) { this.service=service; }
        public void Clear()
        {
            foreach(var body in bodies) { try { body.Hide(host); } catch { } }
            bodies.Clear(); service.Model.GraphicsRedraw2();
        }
        public void Show(IEnumerable<CollisionGeometry> geometries, string selected)
        {
            Clear();
            Matrix<double> toHost=MathNet.Numerics.LinearAlgebra.Double.DenseMatrix.CreateIdentity(4);
            host=service.Model;
            var assembly=service.Model as AssemblyDoc;
            if(assembly!=null)
            {
                var component=((object[])assembly.GetComponents(true) ?? new object[0]).Cast<Component2>().FirstOrDefault(c=>c.GetModelDoc2()!=null);
                if(component==null) throw new InvalidOperationException("预览需要一个已加载的顶层零部件。");
                host=component; toHost=MathOps.GetTransformation(component.Transform2).Inverse();
            }
            var frames=service.LinkTransforms(); var modeler=(Modeler)service.App.GetModeler();
            foreach(var g in geometries)
            {
                g.Validate(); var pose=toHost*frames[g.link]*MathOps.GetTransformation(g.xyz,g.rpy);
                var raw=new double[16]; for(int i=0;i<3;i++) for(int j=0;j<3;j++) raw[i*3+j]=pose[j,i];
                for(int i=0;i<3;i++) raw[9+i]=pose[i,3]; raw[12]=1;
                var transform=(MathTransform)((MathUtility)service.App.GetMathUtility()).CreateTransform(raw);
                Action<Body2> display=body=>
                {
                    if(body==null) throw new InvalidOperationException("SW 无法创建临时预览形状。");
                    body.ApplyTransform(transform);
                    int result=body.Display3(host,g.id==selected?0x00AAFF:0xFFAA33,0);
                    if(result!=0) throw new InvalidOperationException("临时预览显示失败："+result);
                    // Scope appearance to temporary faces. Body-level material
                    // overrides can also recolour the host component in SW 2025.
                    foreach(Face2 face in (object[])body.GetFaces())
                        face.MaterialPropertyValues=new[]{g.id==selected?1.0:.2,.65,g.id==selected?0:1,.4,.7,.2,.1,.65,0};
                    bodies.Add(body);
                };
                Func<double,double,Body2> sphere=(radius,z)=>
                {
                    var surface=(Surface)modeler.CreateSphericalSurface2(new[]{0.0,0,z},new[]{0.0,0,1},new[]{1.0,0,0},radius);
                    // Periodic surfaces require an array with ONE null trimming curve,
                    // not a null SAFEARRAY (which faults inside the SW modeler).
                    return (Body2)surface.CreateTrimmedSheet5(new object[] { null },true,.00001);
                };
                // SW uses the centre of the starting FACE, while MJCF uses
                // the centre of the volume. Centre the temporary box on Z.
                if(g.type=="box") display(modeler.CreateBodyFromBox3(new[]{0.0,0,-g.size[2]/2,0,0,1,g.size[0],g.size[1],g.size[2]}));
                else if(g.type=="sphere") display(sphere(g.size[0],0));
                else
                {
                    double length=g.size[1]; display((Body2)modeler.CreateBodyFromCyl(new[]{0.0,0,-length/2,0,0,1,g.size[0],length}));
                    if(g.type=="capsule") { display(sphere(g.size[0],-length/2)); display(sphere(g.size[0],length/2)); }
                }
            }
            service.Model.GraphicsRedraw2();
        }
        public void Dispose() { Clear(); }
    }
}
