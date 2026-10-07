using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using SW2URDF.RobotModel;

namespace SW2URDF.Simulation
{
    // Pure resolved geometry; it contains no CAD objects or mutable project records.
    public sealed class ResolvedSimulationGeometry
    {
        public readonly ReadOnlyCollection<SiteSnapshot> Sites;
        public readonly ReadOnlyCollection<CollisionGeometrySnapshot> Collisions;
        public ResolvedSimulationGeometry(IEnumerable<SiteSnapshot> sites, IEnumerable<CollisionGeometrySnapshot> collisions)
        {
            Sites=Array.AsReadOnly(sites.ToArray());
            Collisions=Array.AsReadOnly(collisions.ToArray());
        }
    }

    public static class SimulationConfigBuilder
    {
        // Name-only resolution is limited to legacy project input. An existing ID is authoritative.
        internal static string Reference(string id,string name,IDictionary<string,string> objects,bool optional=false)
        {
            if(!string.IsNullOrWhiteSpace(id)) {
                if(!objects.ContainsKey(id))throw new InvalidDataException("Unknown stable reference: "+id);
                return id;
            }
            if(optional&&string.IsNullOrWhiteSpace(name))return null;
            var matches=objects.Where(x=>x.Value==name).ToArray();
            if(matches.Length!=1)throw new InvalidDataException("Unknown or ambiguous legacy reference: "+name);
            return matches[0].Key;
        }
        static ConstraintSnapshot Constraint(ConstraintSettings c)
        {
            return c==null?null:new ConstraintSnapshot(c.timeconst,c.dampratio,c.dmin,c.dmax,c.width,c.midpoint,c.power,c.margin,c.condim);
        }
        public static SimulationConfigSnapshot Build(SimulationProject p,RobotCoreSnapshot core,ResolvedSimulationGeometry geometry)
        {
            if(p==null||core==null||geometry==null)throw new ArgumentNullException();
            p.ValidateSolver();
            var links=core.Links.ToDictionary(x=>x.Id,x=>x.Name);
            var joints=core.Joints.ToDictionary(x=>x.Id,x=>x.Name);
            var sites=geometry.Sites.ToDictionary(x=>x.Id,x=>x.Name);
            Func<string,string,string> link=(id,name)=>Reference(id,name,links);
            Func<string,string,string> joint=(id,name)=>Reference(id,name,joints);
            Func<string,string,string> site=(id,name)=>Reference(id,name,sites);
            var solver=p.solver==null?null:new SolverSnapshot(p.solver.timestep,p.solver.iterations,p.solver.tolerance,p.solver.noslip_iterations,p.solver.impratio,p.solver.enabled,Constraint(p.solver.equality),Constraint(p.solver.contact));
            var collision=p.collision??new CollisionConfiguration();
            var modes=collision.link_modes.ToDictionary(x=>Reference(links.ContainsKey(x.Key)?x.Key:null,x.Key,links),x=>x.Value);
            var pairs=collision.allowed_pairs.Select(x=>new ContactPairSnapshot(Link1:link(x.link1_id,x.link1),Link1Id:link(x.link1_id,x.link1),Link2:link(x.link2_id,x.link2),Link2Id:link(x.link2_id,x.link2),Solver:Constraint(x.solver)));
            var collisionSnapshot=new CollisionSnapshot(collision.disable_internal,geometry.Collisions,pairs,modes);
            var settings=p.joints.Select(x=>new JointSettingsSnapshot(Joint:joint(x.joint_id,x.joint),JointId:joint(x.joint_id,x.joint),Type:x.type,LimitMode:x.limit_mode,SpringMode:x.spring_mode,Damping:x.damping,Frictionloss:x.frictionloss,Armature:x.armature,Stiffness:x.stiffness,Springref:x.springref,Ref:x.@ref,Margin:x.margin,Lower:x.lower,Upper:x.upper,Pos:x.pos,Axis:x.axis,LimitSolver:Constraint(x.limit_solver),FrictionSolver:Constraint(x.friction_solver)));
            var limits=p.joint_force_limits.Select(x=>new JointForceSnapshot(joint(x.joint_id,x.joint),joint(x.joint_id,x.joint),x.lower,x.upper));
            var actuators=p.actuators.Select(x=>new ActuatorSnapshot(x.id,x.name,joint(x.joint_id,x.joint),joint(x.joint_id,x.joint),x.type,x.gear,x.gain,x.ctrl_min,x.ctrl_max,x.force_min,x.force_max));
            var sensors=p.sensors.Select(x=>new SensorSnapshot(x.id,x.name,site(x.site_id,x.site),site(x.site_id,x.site),x.type,x.cutoff,x.fovy,x.noise));
            var equalities=p.equalities.Select(x=>new EqualitySnapshot(
                Id:x.id,Name:x.name,Type:x.type,Binding:x.binding,
                Site1:x.type!="joint"&&x.binding=="site"?site(x.site1_id,x.site1):null,
                Site1Id:x.type!="joint"&&x.binding=="site"?site(x.site1_id,x.site1):null,
                Site2:x.type!="joint"&&x.binding=="site"?site(x.site2_id,x.site2):null,
                Site2Id:x.type!="joint"&&x.binding=="site"?site(x.site2_id,x.site2):null,
                Joint1:x.type=="joint"?joint(x.joint1_id,x.joint1):null,Joint1Id:x.type=="joint"?joint(x.joint1_id,x.joint1):null,
                Joint2:x.type=="joint"?Reference(x.joint2_id,x.joint2,joints,true):null,Joint2Id:x.type=="joint"?Reference(x.joint2_id,x.joint2,joints,true):null,
                Body1:x.type!="joint"&&x.binding=="body"?link(x.body1_id,x.body1):null,Body1Id:x.type!="joint"&&x.binding=="body"?link(x.body1_id,x.body1):null,
                Body2:x.type!="joint"&&x.binding=="body"?Reference(x.body2_id,x.body2,links,true):null,Body2Id:x.type!="joint"&&x.binding=="body"?Reference(x.body2_id,x.body2,links,true):null,
                PoseMode:x.pose_mode,Torquescale:x.torquescale,Active:x.active,Polycoef:x.polycoef,Anchor:x.anchor,Position:x.position,Orientation:x.orientation,Solver:Constraint(x.solver)));
            var forces=p.site_forces.Select(x=>new SiteForceSnapshot(x.id,x.name,x.type,site(x.site1_id,x.site1),site(x.site1_id,x.site1),site(x.site2_id,x.site2),site(x.site2_id,x.site2),x.length_mode,x.stiffness,x.damping,x.magnitude,x.rest_length,x.enabled));
            return new SimulationConfigSnapshot(p.base_mode,p.joint_defaults,solver,collisionSnapshot,settings,limits,actuators,sensors,equalities,forces,geometry.Sites);
        }
    }
}
