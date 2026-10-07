using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
namespace SWSimTool.Simulation {
 // Only immutable revision metadata is retained; ModelDoc2 is a weak table key.
 public static class CadRevision {
  sealed class State {public int stamp;public long generation;public string session=Guid.NewGuid().ToString("N");public bool initialized;}
  static readonly ConditionalWeakTable<ModelDoc2,State> states=new ConditionalWeakTable<ModelDoc2,State>();
  public static string Get(ModelDoc2 model){var state=states.GetValue(model,x=>new State());int stamp=model.GetUpdateStamp();if(state.initialized&&state.stamp!=stamp)state.generation++;state.initialized=true;state.stamp=stamp;return model.GetPathName()+"|"+model.ConfigurationManager.ActiveConfiguration.Name+"|"+state.session+"|"+state.generation;}
  // A verified attribute-only write may advance the stamp without changing CAD geometry.
  public static int BeforeConfigurationWrite(ModelDoc2 model){Get(model);return model.GetUpdateStamp();}
  public static void AfterConfigurationWrite(ModelDoc2 model,int before){var state=states.GetValue(model,x=>new State());if(state.initialized&&state.stamp==before)state.stamp=model.GetUpdateStamp();else Invalidate(model);}
  public static void Invalidate(ModelDoc2 model){var state=states.GetValue(model,x=>new State());state.generation++;state.stamp=model.GetUpdateStamp();state.initialized=true;}
 }
 // Resolved values are serialized pure data; no Feature, Face2, Edge or Component2 survives a query.
 public sealed class CadReferenceSnapshot {
  readonly Dictionary<string,string> values=new Dictionary<string,string>();
  public string Revision {get;private set;}
  public int QueryCount {get;private set;}
  public CadReferenceSnapshot(string revision){Revision=revision;}
  public T Resolve<T>(string key,Func<T> query){string data;if(!values.TryGetValue(key,out data)){QueryCount++;ExportInstrumentation.GeometryQuery();data=ExportFingerprint.Serializer().Serialize(query());values.Add(key,data);}return ExportFingerprint.Serializer().Deserialize<T>(data);}
 }
 public static class CadSnapshotCache {
  sealed class State {public CadReferenceSnapshot snapshot;}
  static readonly ConditionalWeakTable<ModelDoc2,State> states=new ConditionalWeakTable<ModelDoc2,State>();
  public static CadReferenceSnapshot Get(ModelDoc2 model){var state=states.GetValue(model,x=>new State());var revision=CadRevision.Get(model);if(state.snapshot==null||state.snapshot.Revision!=revision)state.snapshot=new CadReferenceSnapshot(revision);return state.snapshot;}
 }
}
