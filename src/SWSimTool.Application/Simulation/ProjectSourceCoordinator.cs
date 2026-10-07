using System;
using System.Collections.Generic;
using SWSimTool.RobotModel;
namespace SWSimTool.Simulation {
    public sealed class CadSourceSnapshot {
        public RobotCoreSnapshot Core;
        public ResolvedSimulationGeometry Geometry;
        public Dictionary<string,double[]> Frames;
        public string Urdf,Sidecar;
        public Dictionary<string,object> LegacyData;
    }
    // The production contract has no reference-generation mode or fallback.
    public interface ICadSource {CadSourceSnapshot BuildNative(string workspace,SimulationProject project);}
    public sealed class ProjectSourceCoordinator {
        readonly ICadSource source;
        public ProjectSourceCoordinator(ICadSource source){this.source=source??throw new ArgumentNullException(nameof(source));}
        public CadSourceSnapshot BuildNative(string workspace,SimulationProject project) {
            var resolved=source.BuildNative(workspace,project);
            if(resolved==null||resolved.Core==null||resolved.Geometry==null)throw new InvalidOperationException("Incomplete resolved CAD source");
            if(resolved.Urdf!=null||resolved.Sidecar!=null||resolved.LegacyData!=null)throw new InvalidOperationException("Production CAD source cannot contain reference intermediates");
            RobotModelValidator.Validate(resolved.Core);
            return resolved;
        }
    }
}
