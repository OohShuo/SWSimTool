using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace SW2URDF.RobotModel
{
    public enum JointKind { Fixed, Revolute, Continuous, Prismatic, Floating }
    public enum GeometryKind { Box, Sphere, Cylinder, Mesh }
    public sealed class MeshSource
    {
        public string Id { get; private set; }
        public string SourcePath { get; private set; }
        public Vector3d Scale { get; private set; }
        public MeshSource(string id,string sourcePath,Vector3d scale) { Id=id;SourcePath=sourcePath;Scale=scale; }
    }
    public sealed class GeometrySnapshot
    {
        public string Id { get; private set; }
        public GeometryKind Kind { get; private set; }
        public RigidTransform LinkFromGeometry { get; private set; }
        public Vector3d Dimensions { get; private set; } // box full XYZ; sphere radius X; cylinder radius X/full length Y
        public MeshSource Mesh { get; private set; }
        public bool IsCollision { get; private set; }
        readonly double[] rgba;
        public double[] Rgba { get { return (double[])rgba.Clone(); } }
        public GeometrySnapshot(string id,GeometryKind kind,RigidTransform pose,Vector3d dimensions,MeshSource mesh,bool collision,double[] color)
        { Id=id;Kind=kind;LinkFromGeometry=pose;Dimensions=dimensions;Mesh=mesh;IsCollision=collision;rgba=color==null?new[]{.5,.5,.5,1}:(double[])color.Clone();if(rgba.Length!=4||rgba.Any(v=>Vector3d.Finite(v)<0||v>1))throw new InvalidDataException("Invalid RGBA"); }
    }
    public sealed class InertialSnapshot
    {
        public double Mass { get; private set; }
        public RigidTransform LinkFromInertial { get; private set; }
        public SymmetricInertia InertiaAtComInInertialFrame { get; private set; }
        public SymmetricInertia InertiaAtComInLinkFrame { get { return InertiaAtComInInertialFrame.Rotated(LinkFromInertial.Rotation); } }
        public InertialSnapshot(double mass,RigidTransform pose,SymmetricInertia inertia) { Mass=Vector3d.Finite(mass);LinkFromInertial=pose;InertiaAtComInInertialFrame=inertia; }
    }
    public sealed class LinkSnapshot
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public InertialSnapshot Inertial { get; private set; }
        public ReadOnlyCollection<GeometrySnapshot> Geometries { get; private set; }
        public LinkSnapshot(string id,string name,InertialSnapshot inertial,IEnumerable<GeometrySnapshot> geometries) { Id=id;Name=name;Inertial=inertial;Geometries=Array.AsReadOnly(geometries.ToArray()); }
    }
    public sealed class JointSnapshot
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string ParentLinkId { get; private set; }
        public string ChildLinkId { get; private set; }
        public JointKind Kind { get; private set; }
        public RigidTransform ParentLinkFromJoint { get; private set; }
        public Vector3d AxisInJointFrame { get; private set; }
        public double? Lower { get; private set; }
        public double? Upper { get; private set; }
        public double Damping { get; private set; }
        public double FrictionLoss { get; private set; }
        public double EffortLimit { get; private set; }
        public JointSnapshot(string id,string name,string parent,string child,JointKind kind,RigidTransform pose,Vector3d axis,double? lower,double? upper,double damping,double friction,double effort=0)
        { Id=id;Name=name;ParentLinkId=parent;ChildLinkId=child;Kind=kind;ParentLinkFromJoint=pose;AxisInJointFrame=kind==JointKind.Fixed||kind==JointKind.Floating?axis:axis.Normalized();Lower=lower;Upper=upper;Damping=Vector3d.Finite(damping);FrictionLoss=Vector3d.Finite(friction);EffortLimit=Vector3d.Finite(effort); }
    }
    public sealed class RobotCoreSnapshot
    {
        public string Name { get; private set; }
        public ReadOnlyCollection<LinkSnapshot> Links { get; private set; }
        public ReadOnlyCollection<JointSnapshot> Joints { get; private set; }
        public RobotCoreSnapshot(string name,IEnumerable<LinkSnapshot> links,IEnumerable<JointSnapshot> joints)
        { Name=name;Links=Array.AsReadOnly(links.ToArray());Joints=Array.AsReadOnly(joints.ToArray());RobotModelValidator.Validate(this); }
        public LinkSnapshot Root { get { var children=new HashSet<string>(Joints.Select(j=>j.ChildLinkId));return Links.Single(l=>!children.Contains(l.Id)); } }
    }
    public static class RobotModelValidator
    {
        public static void Validate(RobotCoreSnapshot core)
        {
            Unique(core.Links.Select(l=>l.Id),"link ID");Unique(core.Links.Select(l=>l.Name),"link name");Unique(core.Joints.Select(j=>j.Id),"joint ID");Unique(core.Joints.Select(j=>j.Name),"joint name");
            Unique(core.Links.SelectMany(l=>l.Geometries).Select(g=>g.Id),"geometry ID");
            foreach(var group in core.Links.SelectMany(l=>l.Geometries).Where(g=>g.Mesh!=null).Select(g=>g.Mesh).GroupBy(m=>m.Id))if(group.Select(m=>m.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=1)throw new InvalidDataException("Conflicting mesh source identity: "+group.Key);
            var ids=new HashSet<string>(core.Links.Select(l=>l.Id));var child=new HashSet<string>();
            foreach(var j in core.Joints){if(!ids.Contains(j.ParentLinkId)||!ids.Contains(j.ChildLinkId)||j.ParentLinkId==j.ChildLinkId||!child.Add(j.ChildLinkId))throw new InvalidDataException("Invalid joint topology: "+j.Name);
                if(!Enum.IsDefined(typeof(JointKind),j.Kind)||j.Damping<0||j.FrictionLoss<0||j.EffortLimit<0)throw new InvalidDataException("Invalid joint kind/dynamics");ValidatePose(j.ParentLinkFromJoint);
                if(j.Lower.HasValue!=j.Upper.HasValue||(j.Lower.HasValue&&(Vector3d.Finite(j.Lower.Value)>=Vector3d.Finite(j.Upper.Value))))throw new InvalidDataException("Invalid joint range: "+j.Name);
                if((j.Kind==JointKind.Revolute||j.Kind==JointKind.Prismatic)&&!j.Lower.HasValue)throw new InvalidDataException("Limited joint requires range: "+j.Name);
            }
            var roots=core.Links.Where(l=>!child.Contains(l.Id)).ToArray();if(roots.Length!=1)throw new InvalidDataException("Exactly one root link required");
            var visited=new HashSet<string>();Action<string> visit=null;visit=id=>{if(!visited.Add(id))throw new InvalidDataException("Cyclic topology");foreach(var j in core.Joints.Where(j=>j.ParentLinkId==id))visit(j.ChildLinkId);};visit(roots[0].Id);
            if(visited.Count!=ids.Count)throw new InvalidDataException("Disconnected or cyclic topology");
            foreach(var l in core.Links){if(l.Inertial!=null){if(l.Inertial.Mass<0)throw new InvalidDataException("Negative mass");ValidatePose(l.Inertial.LinkFromInertial);l.Inertial.InertiaAtComInInertialFrame.Validate(l.Inertial.Mass);}
                Unique(l.Geometries.Select(g=>g.Id),"geometry ID");foreach(var g in l.Geometries){var d=g.Dimensions;
                    if(!Enum.IsDefined(typeof(GeometryKind),g.Kind))throw new InvalidDataException("Unknown geometry kind");ValidatePose(g.LinkFromGeometry);
                    if(g.Kind==GeometryKind.Mesh){if(g.Mesh==null||string.IsNullOrWhiteSpace(g.Mesh.Id)||string.IsNullOrWhiteSpace(g.Mesh.SourcePath)||g.Mesh.Scale.ToArray().Any(v=>v<=0))throw new InvalidDataException("Invalid mesh source");}
                    else if(d.X<=0||(g.Kind==GeometryKind.Box&&(d.Y<=0||d.Z<=0))||(g.Kind==GeometryKind.Cylinder&&d.Y<=0))throw new InvalidDataException("Invalid geometry dimensions");}}
        }
        internal static void ValidatePose(RigidTransform pose) { var q=pose.Rotation;var n=q.W*q.W+q.X*q.X+q.Y*q.Y+q.Z*q.Z;if(Math.Abs(n-1)>1e-12)throw new InvalidDataException("Pose requires a normalized, nonzero quaternion"); }
        internal static void Unique(IEnumerable<string> values,string label) { var seen=new HashSet<string>();foreach(var value in values)if(string.IsNullOrWhiteSpace(value)||!seen.Add(value))throw new InvalidDataException("Empty or duplicate "+label+": "+value); }
    }
}
