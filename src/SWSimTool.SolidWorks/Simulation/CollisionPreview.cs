using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SWSimTool.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SWSimTool.Simulation
{
    public sealed class CollisionPreview : IDisposable
    {
        readonly AttachmentService service;
        readonly List<Body2> bodies = new List<Body2>();
        object host;
        sealed class Entry { public string type; public double[] size; public Matrix<double> pose; public bool selected; public List<Body2> bodies=new List<Body2>(); }
        readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();
        public int CreatedBodyCount { get; private set; }
        public int TransformedBodyCount { get; private set; }
        public int RedrawCount { get; private set; }
        public CollisionPreview(AttachmentService service) { this.service=service; }
        public void Clear()
        {
            foreach(var body in bodies) { try { body.Hide(host); } catch { } }
            bool changed=bodies.Count>0;
            bodies.Clear();entries.Clear();if(changed){service.Model.GraphicsRedraw2();RedrawCount++;}
        }
        public void Show(IEnumerable<CollisionGeometry> geometries, string selected)
        {
            bool changed=false;
            var values=geometries.ToArray();var wanted=new HashSet<string>(values.Select(g=>g.id));
            foreach(var id in entries.Keys.Where(id=>!wanted.Contains(id)).ToArray())
            {
                foreach(var body in entries[id].bodies){body.Hide(host);bodies.Remove(body);}entries.Remove(id);changed=true;
            }
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
            foreach(var g in values)
            {
                g.Validate(); var pose=toHost*frames[g.link]*MathOps.GetTransformation(g.xyz,g.rpy);
                Entry previous;bool selectedNow=g.id==selected;
                if(entries.TryGetValue(g.id,out previous)&&previous.type==g.type&&previous.size.SequenceEqual(g.size))
                {
                    if((pose-previous.pose).L2Norm()>1e-10)
                    {
                        var delta=MakeTransform(pose*previous.pose.Inverse());
                        foreach(var body in previous.bodies){body.ApplyTransform(delta);TransformedBodyCount++;}
                        previous.pose=pose;changed=true;
                    }
                    if(previous.selected!=selectedNow)
                    {
                        foreach(var body in previous.bodies){body.Display3(host,selectedNow?0x00AAFF:0xFFAA33,0);Appearance(body,selectedNow);}
                        previous.selected=selectedNow;changed=true;
                    }
                    continue;
                }
                if(previous!=null){foreach(var body in previous.bodies){body.Hide(host);bodies.Remove(body);}entries.Remove(g.id);}
                var entry=new Entry{type=g.type,size=(double[])g.size.Clone(),pose=pose,selected=selectedNow};
                entries[g.id]=entry;changed=true;
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
                    Appearance(body,selectedNow);
                    bodies.Add(body);entry.bodies.Add(body);CreatedBodyCount++;
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
            if(changed){service.Model.GraphicsRedraw2();RedrawCount++;}
        }
        void Appearance(Body2 body,bool selected)
        {
            foreach(Face2 face in (object[])body.GetFaces())face.MaterialPropertyValues=new[]{selected?1.0:.2,.65,selected?0:1,.4,.7,.2,.1,.65,0};
        }
        MathTransform MakeTransform(Matrix<double> pose)
        {
            var raw=new double[16];for(int i=0;i<3;i++)for(int j=0;j<3;j++)raw[i*3+j]=pose[j,i];
            for(int i=0;i<3;i++)raw[9+i]=pose[i,3];raw[12]=1;
            return (MathTransform)((MathUtility)service.App.GetMathUtility()).CreateTransform(raw);
        }
        public void Dispose() { Clear(); }
    }
}
