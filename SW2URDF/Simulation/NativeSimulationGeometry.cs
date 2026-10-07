using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SW2URDF.RobotModel;
using SW2URDF.Utilities;

namespace SW2URDF.Simulation
{
    public sealed partial class AttachmentService
    {
        public ResolvedSimulationGeometry ResolveNativeGeometry(RobotCoreSnapshot core)
        {
            var links=core.Links.ToDictionary(x=>x.Id,x=>x.Name);
            var sites=new List<SiteSnapshot>();
            var names=new HashSet<string>();
            foreach(var item in Project.attachments) {
                if(string.IsNullOrWhiteSpace(item.name)||!names.Add(item.name))throw new InvalidDataException("Attachment names must be nonempty and unique.");
                if(item.type!="point"&&item.type!="frame")throw new InvalidDataException("Invalid site type: "+item.type);
                var link=SimulationConfigBuilder.Reference(item.link_id,item.link,links);
                var pose=LinkTransforms()[links[link]].Inverse()*Resolve(item);
                var xyz=MathOps.GetXYZ(pose);var rpy=item.type=="frame"?MathOps.GetRPY(pose):new double[3];
                sites.Add(new SiteSnapshot(item.id,item.name,link,item.type=="frame",new RigidTransform(new Vector3d(xyz[0],xyz[1],xyz[2]),Quaterniond.FromRpy(new Vector3d(rpy[0],rpy[1],rpy[2])))));
            }
            var collisions=new List<CollisionGeometrySnapshot>();
            foreach(var item in (Project.collision??new CollisionConfiguration()).geometries) {
                var link=SimulationConfigBuilder.Reference(item.link_id,item.link,links);
                ResolveCollision(item);
                collisions.Add(new CollisionGeometrySnapshot(item.id,item.name,link,link,item.type,item.size,item.xyz,item.rpy));
            }
            return new ResolvedSimulationGeometry(sites,collisions);
        }
    }
}
