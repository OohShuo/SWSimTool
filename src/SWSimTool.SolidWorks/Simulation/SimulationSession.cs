using SolidWorks.Interop.sldworks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace SWSimTool.Simulation {
 // Editors already own isolated drafts. These session hints supplement export fingerprints.
 public static class SimulationSession {
  sealed class State {public Dictionary<string,SimulationDirtyFlags> dirty=new Dictionary<string,SimulationDirtyFlags>();}
  static readonly ConditionalWeakTable<ModelDoc2,State> states=new ConditionalWeakTable<ModelDoc2,State>();
  public static SimulationDirtyFlags Dirty(ModelDoc2 model){SimulationDirtyFlags value;return states.GetValue(model,x=>new State()).dirty.TryGetValue(model.ConfigurationManager.ActiveConfiguration.Name,out value)?value:SimulationDirtyFlags.None;}
  public static void Mark(ModelDoc2 model,SimulationDirtyFlags flags){states.GetValue(model,x=>new State()).dirty[model.ConfigurationManager.ActiveConfiguration.Name]=Dirty(model)|flags;}
  public static void Applied(ModelDoc2 model,SimulationProject before,SimulationProject after){Mark(model,ExportFingerprint.Changed(before,after));}
  internal static void InvalidateExport(ModelDoc2 model,string configuration){states.GetValue(model,x=>new State()).dirty[configuration]=SimulationDirtyFlags.Source|SimulationDirtyFlags.Mjcf;}
  public static void ExportSucceeded(ModelDoc2 model){states.GetValue(model,x=>new State()).dirty[model.ConfigurationManager.ActiveConfiguration.Name]=SimulationDirtyFlags.None;}
 }
}
