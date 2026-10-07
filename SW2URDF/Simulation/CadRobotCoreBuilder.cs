using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SW2URDF.RobotModel;
using SW2URDF.URDF;
using SW2URDF.Utilities;

namespace SW2URDF.Simulation
{
    // Query DTO is transiently serialized by the lazy CAD snapshot; no COM object is stored.
    public sealed class CadLinkProperties
    {
        public double Mass {get;set;}
        public double[] Com {get;set;}
        public double[] Inertia {get;set;}
        public double[] Rgba {get;set;}
    }
    public static class CadRobotCoreBuilder
    {
        static Vector3d Vector(double[] v)=>new Vector3d(v[0],v[1],v[2]);
        static RigidTransform Pose(Matrix<double> m)=>new RigidTransform(Vector(MathOps.GetXYZ(m)),Quaterniond.FromRpy(Vector(MathOps.GetRPY(m))));
        static JointKind Kind(string value){switch(value){case "fixed":return JointKind.Fixed;case "floating":return JointKind.Floating;case "continuous":return JointKind.Continuous;case "revolute":return JointKind.Revolute;case "prismatic":return JointKind.Prismatic;default:throw new NotSupportedException("CAD joint type: "+value);}}
        static double Optional(Func<double> value,double fallback=0){try{return value();}catch(NullReferenceException){return fallback;}}
        static double Limit(Joint j,bool lower){try{return lower?j.Limit.Lower:j.Limit.Upper;}catch(NullReferenceException e){throw new InvalidDataException("关节 “"+j.Name+"” 缺少 URDF 限位。请填写上下限，或对无限位旋转关节使用 continuous。",e);}}
        static void AddBodies(Component2 component,List<Body2> bodies)
        {
            foreach(Body2 b in component.GetBodies3((int)swBodyType_e.swSolidBody,out _) as object[]??new object[0])bodies.Add(b);
            foreach(Component2 child in component.GetChildren() as object[]??new object[0])AddBodies(child,bodies);
        }
        static CadLinkProperties Query(ModelDoc2 model,LinkNode node,MathTransform coordinate)
        {
            var bodies=new List<Body2>();foreach(var c in node.Link.SWComponents)AddBodies(c,bodies);
            var rgba=node.Link.Visual?.Material.Color.GetColor()??new[]{.5,.5,.5,1};
            if(node.Link.SWComponents.Count>0){var doc=node.Link.SWComponents[0].GetModelDoc2() as ModelDoc2;var color=doc?.MaterialPropertyValues as double[];if(color!=null&&color.Length>=8)rgba=new[]{color[0],color[1],color[2],1-color[7]};}
            if(bodies.Count==0)return new CadLinkProperties{Mass=0,Com=new double[3],Inertia=new double[9],Rgba=rgba};
            // SW mass getters can reset the selected reference frame. Use separate,
            // short-lived measurements, then retain only the resulting numeric data.
            Func<bool,MassProperty> measure=local=>{var value=(MassProperty)model.Extension.CreateMassProperty();if(local)value.SetCoordinateSystem(coordinate);if(!value.AddBodies(bodies.ToArray()))throw new InvalidDataException("Cannot measure CAD link: "+node.Name);return value;};
            var inertia=(double[])measure(true).GetMomentOfInertia((int)swMomentsOfInertiaReferenceFrame_e.swMomentsOfInertiaReferenceFrame_CenterOfMass);
            var com=(double[])measure(true).CenterOfMass;
            var mass=measure(false).Mass;
            return new CadLinkProperties{Mass=mass,Com=com,Inertia=inertia,Rgba=rgba};
        }
        public static RobotCoreSnapshot Build(string name,AttachmentService service,LinkNode root,IDictionary<string,MeshSource> meshes)
        {
            var frames=service.LinkTransforms();
            var descriptors=JointDescriptor.FromTree(root).ToDictionary(x=>x.name);
            var nodes=new List<LinkNode>();Action<LinkNode> collect=null;collect=n=>{nodes.Add(n);foreach(LinkNode c in n.Nodes)collect(c);};collect(root);
            var jointIds=nodes.Where(x=>x.Parent!=null).ToDictionary(x=>x.Link.Joint.Name,x=>x.Link.Joint.StableId);
            var links=new List<LinkSnapshot>();var joints=new List<JointSnapshot>();
            foreach(var node in nodes){
                var config=node.Link;var frame=frames[node.Name];
                var properties=CadSnapshotCache.Get(service.Model).Resolve("link-properties:"+ExportFingerprint.Hash(new{id=config.StableId,frame=frame.ToRowMajorArray(),components=config.SWComponents.Select(x=>x.Name2).ToArray()}),()=>Query(service.Model,node,service.CoordinateTransform(config.Joint.CoordinateSystemName)));
                var i=properties.Inertia;var inertial=new InertialSnapshot(properties.Mass,new RigidTransform(Vector(properties.Com),Quaterniond.Identity),new SymmetricInertia(i[0],i[4],i[8],-i[1],-i[2],-i[5]));
                var geoms=new List<GeometrySnapshot>();MeshSource mesh;
                if(meshes.TryGetValue(config.StableId,out mesh)){
                    if(!string.IsNullOrWhiteSpace(config.Visual.Material.Texture.wFilename))throw new NotSupportedException("CAD textures are not implemented");
                    geoms.Add(new GeometrySnapshot(config.StableId+"/visual/0",GeometryKind.Mesh,RigidTransform.Identity,new Vector3d(),mesh,false,properties.Rgba));
                    geoms.Add(new GeometrySnapshot(config.StableId+"/collision/1",GeometryKind.Mesh,RigidTransform.Identity,new Vector3d(),mesh,true,properties.Rgba));
                }
                links.Add(new LinkSnapshot(config.StableId,node.Name,inertial,geoms));
                if(node.Parent==null)continue;
                var parent=(LinkNode)node.Parent;var j=config.Joint;var kind=config.isFixedFrame?JointKind.Fixed:Kind(j.Type);bool limited=kind==JointKind.Revolute||kind==JointKind.Prismatic;
                var axis=kind==JointKind.Fixed||kind==JointKind.Floating?new Vector3d():Vector(service.OriginalJointAxis(descriptors[j.Name]));
                MimicSnapshot mimic=null;if(j.Mimic!=null&&j.Mimic.ElementContainsData()){string id;if(!jointIds.TryGetValue(j.Mimic.JointName,out id))throw new InvalidDataException("Unknown mimic joint: "+j.Mimic.JointName);mimic=new MimicSnapshot(id,Optional(()=>j.Mimic.Multiplier,1),Optional(()=>j.Mimic.Offset));}
                joints.Add(new JointSnapshot(j.StableId,j.Name,parent.Link.StableId,config.StableId,kind,Pose(frames[parent.Name].Inverse()*frame),axis,limited?(double?)Limit(j,true):null,limited?(double?)Limit(j,false):null,Optional(()=>j.Dynamics.Damping),Optional(()=>j.Dynamics.Friction),Optional(()=>j.Limit.Effort),mimic));
            }
            return new RobotCoreSnapshot(name,links,joints);
        }
    }
    public sealed partial class AttachmentService
    {
        internal MathTransform CoordinateTransform(string name)=>exporter.AttachmentCoordinateTransform(name);
    }
}

