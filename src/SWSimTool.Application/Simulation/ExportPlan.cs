namespace SWSimTool.Simulation {
 public sealed class ExportPlan {
  public bool RebuildSource {get;private set;}
  public SimulationDirtyFlags Dirty {get;private set;}
  public static ExportPlan Build(bool verifiedSource,SimulationDirtyFlags hint){return new ExportPlan{RebuildSource=!verifiedSource,Dirty=hint|(!verifiedSource?SimulationDirtyFlags.Source|SimulationDirtyFlags.Mesh:SimulationDirtyFlags.None)|SimulationDirtyFlags.Mjcf};}
 }
}
