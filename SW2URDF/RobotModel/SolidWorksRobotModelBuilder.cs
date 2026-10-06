using System;
using System.Collections.Generic;
using System.IO;
using SW2URDF.URDF;

namespace SW2URDF.RobotModel
{
    public static class SolidWorksRobotModelBuilder
    {
        // Transitional input adapter: use the already resolved/localized CAD result in memory.
        // No XML serialization, CAD query, retained COM object, or mesh processing occurs here.
        public static RobotCoreSnapshot FromResolvedRobot(Robot robot,IDictionary<string,MeshSource> linkMeshes)
        {
            if(robot==null||robot.BaseLink==null)throw new ArgumentNullException("robot");
            var links=new List<LinkSnapshot>();var joints=new List<JointSnapshot>();var visited=new HashSet<Link>();
            Action<Link,Link> visit=null;visit=(link,parent)=>{
                if(!visited.Add(link))throw new InvalidDataException("Cyclic resolved CAD tree");var name=link.Name;InertialSnapshot inertial=null;
                if(link.Inertial!=null){var source=link.Inertial;var i=source.Inertia;inertial=new InertialSnapshot(source.Mass.Value,Pose(source.Origin),new SymmetricInertia(i.Ixx,i.Iyy,i.Izz,i.Ixy,i.Ixz,i.Iyz));}
                var geometries=new List<GeometrySnapshot>();MeshSource mesh;
                if(linkMeshes.TryGetValue(name,out mesh)){
                    if(link.Visual!=null&&!string.IsNullOrWhiteSpace(link.Visual.Material.Texture.wFilename))throw new NotSupportedException("Candidate CAD textures are not implemented");
                    if(link.Visual!=null)geometries.Add(new GeometrySnapshot(link.StableId+"/visual/0",GeometryKind.Mesh,Pose(link.Visual.Origin),new Vector3d(),mesh,false,link.Visual.Material.Color.GetColor()));
                    if(link.Collision!=null)geometries.Add(new GeometrySnapshot(link.StableId+"/collision/1",GeometryKind.Mesh,Pose(link.Collision.Origin),new Vector3d(),mesh,true,null));
                }
                links.Add(new LinkSnapshot(link.StableId,name,inertial,geometries));
                if(parent!=null){var j=link.Joint;if(j==null)throw new InvalidDataException("Missing incoming joint");if(j.Mimic!=null&&j.Mimic.ElementContainsData())throw new NotSupportedException("Candidate CAD mimic is not implemented");var kind=Kind(j.Type);var limited=kind==JointKind.Revolute||kind==JointKind.Prismatic;
                    joints.Add(new JointSnapshot(j.StableId,j.Name,parent.StableId,link.StableId,kind,Pose(j.Origin),V(j.Axis.GetXYZ()),limited?(double?)j.Limit.Lower:null,limited?(double?)j.Limit.Upper:null,Optional(()=>j.Dynamics.Damping),Optional(()=>j.Dynamics.Friction),Optional(()=>j.Limit.Effort)));
                }
                foreach(var child in link.Children)visit(child,link);
            };visit(robot.BaseLink,null);
            return new RobotCoreSnapshot(string.IsNullOrWhiteSpace(robot.Name)?"robot":robot.Name,links,joints);
        }
        static double Optional(Func<double> read) { try{return read();}catch(NullReferenceException){return 0;} }
        static Vector3d V(double[] value) { if(value==null||value.Length!=3)throw new InvalidDataException("Invalid resolved XYZ");return new Vector3d(value[0],value[1],value[2]); }
        static RigidTransform Pose(Origin origin) { return origin==null?RigidTransform.Identity:new RigidTransform(V(origin.GetXYZ()),Quaterniond.FromRpy(V(origin.GetRPY()))); }
        static JointKind Kind(string kind) { switch(kind){case "fixed":return JointKind.Fixed;case "revolute":return JointKind.Revolute;case "continuous":return JointKind.Continuous;case "prismatic":return JointKind.Prismatic;case "floating":return JointKind.Floating;default:throw new NotSupportedException("Candidate CAD joint type: "+kind);} }
    }
}
