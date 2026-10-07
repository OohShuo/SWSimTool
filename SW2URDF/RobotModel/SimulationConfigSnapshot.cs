using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
namespace SW2URDF.RobotModel {
public sealed class ConstraintSnapshot {
public static readonly ConstraintSnapshot Default = new ConstraintSnapshot();
public readonly double? Timeconst;
public readonly double? Dampratio;
public readonly double? Dmin;
public readonly double? Dmax;
public readonly double? Width;
public readonly double? Midpoint;
public readonly double? Power;
public readonly double? Margin;
public readonly double? Condim;
        public ConstraintSnapshot(
            double? Timeconst=null,
            double? Dampratio=null,
            double? Dmin=null,
            double? Dmax=null,
            double? Width=null,
            double? Midpoint=null,
            double? Power=null,
            double? Margin=null,
            double? Condim=null)
        {
            this.Timeconst=Timeconst;
            this.Dampratio=Dampratio;
            this.Dmin=Dmin;
            this.Dmax=Dmax;
            this.Width=Width;
            this.Midpoint=Midpoint;
            this.Power=Power;
            this.Margin=Margin;
            this.Condim=Condim;
        }
}
public sealed class SolverSnapshot {
public readonly double? Timestep;
public readonly double? Iterations;
public readonly double? Tolerance;
public readonly double? NoslipIterations;
public readonly double? Impratio;
public readonly bool? Enabled;
public readonly ConstraintSnapshot Equality;
public readonly ConstraintSnapshot Contact;
        public SolverSnapshot(
            double? Timestep=null,
            double? Iterations=null,
            double? Tolerance=null,
            double? NoslipIterations=null,
            double? Impratio=null,
            bool? Enabled=null,
            ConstraintSnapshot Equality=null,
            ConstraintSnapshot Contact=null)
        {
            this.Timestep=Timestep;
            this.Iterations=Iterations;
            this.Tolerance=Tolerance;
            this.NoslipIterations=NoslipIterations;
            this.Impratio=Impratio;
            this.Enabled=Enabled;
            this.Equality=Equality;
            this.Contact=Contact;
        }
}
public sealed class JointSettingsSnapshot {
public readonly string Joint;
public readonly string JointId;
public readonly string Type;
public readonly string LimitMode;
public readonly string SpringMode;
public readonly double? Damping;
public readonly double? Frictionloss;
public readonly double? Armature;
public readonly double? Stiffness;
public readonly double? Springref;
public readonly double? Ref;
public readonly double? Margin;
public readonly double? Lower;
public readonly double? Upper;
public readonly ReadOnlyCollection<double> Pos;
public readonly ReadOnlyCollection<double> Axis;
public readonly ConstraintSnapshot LimitSolver;
public readonly ConstraintSnapshot FrictionSolver;
        public JointSettingsSnapshot(
            string Joint=null,
            string JointId=null,
            string Type=null,
            string LimitMode=null,
            string SpringMode=null,
            double? Damping=null,
            double? Frictionloss=null,
            double? Armature=null,
            double? Stiffness=null,
            double? Springref=null,
            double? Ref=null,
            double? Margin=null,
            double? Lower=null,
            double? Upper=null,
            IEnumerable<double> Pos=null,
            IEnumerable<double> Axis=null,
            ConstraintSnapshot LimitSolver=null,
            ConstraintSnapshot FrictionSolver=null)
        {
            this.Joint=Joint;
            this.JointId=JointId;
            this.Type=Type;
            this.LimitMode=LimitMode;
            this.SpringMode=SpringMode;
            this.Damping=Damping;
            this.Frictionloss=Frictionloss;
            this.Armature=Armature;
            this.Stiffness=Stiffness;
            this.Springref=Springref;
            this.Ref=Ref;
            this.Margin=Margin;
            this.Lower=Lower;
            this.Upper=Upper;
            this.Pos=Pos == null ? null : Array.AsReadOnly(Pos.ToArray());
            this.Axis=Axis == null ? null : Array.AsReadOnly(Axis.ToArray());
            this.LimitSolver=LimitSolver;
            this.FrictionSolver=FrictionSolver;
        }
}
public sealed class JointForceSnapshot {
public readonly string Joint;
public readonly string JointId;
public readonly double? Lower;
public readonly double? Upper;
        public JointForceSnapshot(
            string Joint=null,
            string JointId=null,
            double? Lower=null,
            double? Upper=null)
        {
            this.Joint=Joint;
            this.JointId=JointId;
            this.Lower=Lower;
            this.Upper=Upper;
        }
}
public sealed class ActuatorSnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Joint;
public readonly string JointId;
public readonly string Type;
public readonly double? Gear;
public readonly double? Gain;
public readonly double? CtrlMin;
public readonly double? CtrlMax;
public readonly double? ForceMin;
public readonly double? ForceMax;
        public ActuatorSnapshot(
            string Id=null,
            string Name=null,
            string Joint=null,
            string JointId=null,
            string Type=null,
            double? Gear=null,
            double? Gain=null,
            double? CtrlMin=null,
            double? CtrlMax=null,
            double? ForceMin=null,
            double? ForceMax=null)
        {
            this.Id=Id;
            this.Name=Name;
            this.Joint=Joint;
            this.JointId=JointId;
            this.Type=Type;
            this.Gear=Gear;
            this.Gain=Gain;
            this.CtrlMin=CtrlMin;
            this.CtrlMax=CtrlMax;
            this.ForceMin=ForceMin;
            this.ForceMax=ForceMax;
        }
}
public sealed class SensorSnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Site;
public readonly string SiteId;
public readonly string Type;
public readonly double? Cutoff;
public readonly double? Fovy;
public readonly double? Noise;
        public SensorSnapshot(
            string Id=null,
            string Name=null,
            string Site=null,
            string SiteId=null,
            string Type=null,
            double? Cutoff=null,
            double? Fovy=null,
            double? Noise=null)
        {
            this.Id=Id;
            this.Name=Name;
            this.Site=Site;
            this.SiteId=SiteId;
            this.Type=Type;
            this.Cutoff=Cutoff;
            this.Fovy=Fovy;
            this.Noise=Noise;
        }
}
public sealed class EqualitySnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Type;
public readonly string Binding;
public readonly string Site1;
public readonly string Site1Id;
public readonly string Site2;
public readonly string Site2Id;
public readonly string Joint1;
public readonly string Joint1Id;
public readonly string Joint2;
public readonly string Joint2Id;
public readonly string Body1;
public readonly string Body1Id;
public readonly string Body2;
public readonly string Body2Id;
public readonly string PoseMode;
public readonly double? Torquescale;
public readonly bool? Active;
public readonly ReadOnlyCollection<double> Polycoef;
public readonly ReadOnlyCollection<double> Anchor;
public readonly ReadOnlyCollection<double> Position;
public readonly ReadOnlyCollection<double> Orientation;
public readonly ConstraintSnapshot Solver;
        public EqualitySnapshot(
            string Id=null,
            string Name=null,
            string Type=null,
            string Binding=null,
            string Site1=null,
            string Site1Id=null,
            string Site2=null,
            string Site2Id=null,
            string Joint1=null,
            string Joint1Id=null,
            string Joint2=null,
            string Joint2Id=null,
            string Body1=null,
            string Body1Id=null,
            string Body2=null,
            string Body2Id=null,
            string PoseMode=null,
            double? Torquescale=null,
            bool? Active=null,
            IEnumerable<double> Polycoef=null,
            IEnumerable<double> Anchor=null,
            IEnumerable<double> Position=null,
            IEnumerable<double> Orientation=null,
            ConstraintSnapshot Solver=null)
        {
            this.Id=Id;
            this.Name=Name;
            this.Type=Type;
            this.Binding=Binding;
            this.Site1=Site1;
            this.Site1Id=Site1Id;
            this.Site2=Site2;
            this.Site2Id=Site2Id;
            this.Joint1=Joint1;
            this.Joint1Id=Joint1Id;
            this.Joint2=Joint2;
            this.Joint2Id=Joint2Id;
            this.Body1=Body1;
            this.Body1Id=Body1Id;
            this.Body2=Body2;
            this.Body2Id=Body2Id;
            this.PoseMode=PoseMode;
            this.Torquescale=Torquescale;
            this.Active=Active;
            this.Polycoef=Polycoef == null ? null : Array.AsReadOnly(Polycoef.ToArray());
            this.Anchor=Anchor == null ? null : Array.AsReadOnly(Anchor.ToArray());
            this.Position=Position == null ? null : Array.AsReadOnly(Position.ToArray());
            this.Orientation=Orientation == null ? null : Array.AsReadOnly(Orientation.ToArray());
            this.Solver=Solver;
        }
}
public sealed class SiteForceSnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Type;
public readonly string Site1;
public readonly string Site1Id;
public readonly string Site2;
public readonly string Site2Id;
public readonly string LengthMode;
public readonly double? Stiffness;
public readonly double? Damping;
public readonly double? Magnitude;
public readonly double? RestLength;
public readonly bool? Enabled;
        public SiteForceSnapshot(
            string Id=null,
            string Name=null,
            string Type=null,
            string Site1=null,
            string Site1Id=null,
            string Site2=null,
            string Site2Id=null,
            string LengthMode=null,
            double? Stiffness=null,
            double? Damping=null,
            double? Magnitude=null,
            double? RestLength=null,
            bool? Enabled=null)
        {
            this.Id=Id;
            this.Name=Name;
            this.Type=Type;
            this.Site1=Site1;
            this.Site1Id=Site1Id;
            this.Site2=Site2;
            this.Site2Id=Site2Id;
            this.LengthMode=LengthMode;
            this.Stiffness=Stiffness;
            this.Damping=Damping;
            this.Magnitude=Magnitude;
            this.RestLength=RestLength;
            this.Enabled=Enabled;
        }
}
public sealed class CollisionGeometrySnapshot {
public readonly string Id;
public readonly string Name;
public readonly string Link;
public readonly string LinkId;
public readonly string Type;
public readonly ReadOnlyCollection<double> Size;
public readonly ReadOnlyCollection<double> Xyz;
public readonly ReadOnlyCollection<double> Rpy;
        public CollisionGeometrySnapshot(
            string Id=null,
            string Name=null,
            string Link=null,
            string LinkId=null,
            string Type=null,
            IEnumerable<double> Size=null,
            IEnumerable<double> Xyz=null,
            IEnumerable<double> Rpy=null)
        {
            this.Id=Id;
            this.Name=Name;
            this.Link=Link;
            this.LinkId=LinkId;
            this.Type=Type;
            this.Size=Size == null ? null : Array.AsReadOnly(Size.ToArray());
            this.Xyz=Xyz == null ? null : Array.AsReadOnly(Xyz.ToArray());
            this.Rpy=Rpy == null ? null : Array.AsReadOnly(Rpy.ToArray());
        }
}
public sealed class ContactPairSnapshot {
public readonly string Link1;
public readonly string Link1Id;
public readonly string Link2;
public readonly string Link2Id;
public readonly ConstraintSnapshot Solver;
        public ContactPairSnapshot(
            string Link1=null,
            string Link1Id=null,
            string Link2=null,
            string Link2Id=null,
            ConstraintSnapshot Solver=null)
        {
            this.Link1=Link1;
            this.Link1Id=Link1Id;
            this.Link2=Link2;
            this.Link2Id=Link2Id;
            this.Solver=Solver;
        }
}
public sealed class CollisionSnapshot {
public readonly bool? DisableInternal;
public readonly ReadOnlyCollection<CollisionGeometrySnapshot> Geometries;
public readonly ReadOnlyCollection<ContactPairSnapshot> AllowedPairs;
public readonly ReadOnlyDictionary<string,string> LinkModes;
        public CollisionSnapshot(
            bool? DisableInternal=null,
            IEnumerable<CollisionGeometrySnapshot> Geometries=null,
            IEnumerable<ContactPairSnapshot> AllowedPairs=null,
            IDictionary<string,string> LinkModes=null)
        {
            this.DisableInternal=DisableInternal;
            this.Geometries=Array.AsReadOnly((Geometries ?? Enumerable.Empty<CollisionGeometrySnapshot>()).ToArray());
            this.AllowedPairs=Array.AsReadOnly((AllowedPairs ?? Enumerable.Empty<ContactPairSnapshot>()).ToArray());
            this.LinkModes=new ReadOnlyDictionary<string,string>(new Dictionary<string,string>(LinkModes ?? new Dictionary<string,string>()));
        }
}
public sealed class SimulationConfigSnapshot {
public readonly string BaseMode;
public readonly bool? JointDefaults;
public readonly SolverSnapshot Solver;
public readonly CollisionSnapshot Collision;
public readonly ReadOnlyCollection<JointSettingsSnapshot> Joints;
public readonly ReadOnlyCollection<JointForceSnapshot> JointForceLimits;
public readonly ReadOnlyCollection<ActuatorSnapshot> Actuators;
public readonly ReadOnlyCollection<SensorSnapshot> Sensors;
public readonly ReadOnlyCollection<EqualitySnapshot> Equalities;
public readonly ReadOnlyCollection<SiteForceSnapshot> SiteForces;
public readonly ReadOnlyCollection<SiteSnapshot> Sites;

        public SimulationConfigSnapshot(
            string BaseMode=null,
            bool? JointDefaults=null,
            SolverSnapshot Solver=null,
            CollisionSnapshot Collision=null,
            IEnumerable<JointSettingsSnapshot> Joints=null,
            IEnumerable<JointForceSnapshot> JointForceLimits=null,
            IEnumerable<ActuatorSnapshot> Actuators=null,
            IEnumerable<SensorSnapshot> Sensors=null,
            IEnumerable<EqualitySnapshot> Equalities=null,
            IEnumerable<SiteForceSnapshot> SiteForces=null,
            IEnumerable<SiteSnapshot> Sites=null)
        {
            this.BaseMode=BaseMode;
            this.JointDefaults=JointDefaults;
            this.Solver=Solver;
            this.Collision=Collision;
            this.Joints=Array.AsReadOnly((Joints ?? Enumerable.Empty<JointSettingsSnapshot>()).ToArray());
            this.JointForceLimits=Array.AsReadOnly((JointForceLimits ?? Enumerable.Empty<JointForceSnapshot>()).ToArray());
            this.Actuators=Array.AsReadOnly((Actuators ?? Enumerable.Empty<ActuatorSnapshot>()).ToArray());
            this.Sensors=Array.AsReadOnly((Sensors ?? Enumerable.Empty<SensorSnapshot>()).ToArray());
            this.Equalities=Array.AsReadOnly((Equalities ?? Enumerable.Empty<EqualitySnapshot>()).ToArray());
            this.SiteForces=Array.AsReadOnly((SiteForces ?? Enumerable.Empty<SiteForceSnapshot>()).ToArray());
            this.Sites=Array.AsReadOnly((Sites ?? Enumerable.Empty<SiteSnapshot>()).ToArray());
            RobotModelValidator.Unique(this.Sites.Select(x=>x.Id),"site ID");
            RobotModelValidator.Unique(this.Sites.Select(x=>x.Name),"site name");
        }
}
    public sealed class SiteSnapshot
    {
        public readonly string Id,Name,LinkId;
        public readonly bool IsFrame;
        public readonly RigidTransform LinkFromSite;
        public SiteSnapshot(string id,string name,string link,bool frame,RigidTransform pose) { Id=id;Name=name;LinkId=link;IsFrame=frame;LinkFromSite=pose; }
    }

    public sealed class RobotModel
    {
        public const int SchemaVersion=1;
        public RobotCoreSnapshot Core { get; private set; }
        public SimulationConfigSnapshot Simulation { get; private set; }
        public RobotModel(RobotCoreSnapshot core,SimulationConfigSnapshot simulation)
        { Core=core??throw new ArgumentNullException("core");Simulation=simulation??throw new ArgumentNullException("simulation");RobotModelValidator.ValidateBindings(core,simulation); }
    }

}
