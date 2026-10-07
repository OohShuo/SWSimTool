using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
namespace SWSimTool.Simulation {
 [Flags] public enum SimulationDirtyFlags {None=0,Source=1,Mesh=2,Site=4,Collision=8,Joint=16,Equality=32,Actuator=64,Sensor=128,Solver=256,SiteForce=512,Mjcf=1024}
 public static class ExportFingerprint {
  public static JavaScriptSerializer Serializer(){return new JavaScriptSerializer{MaxJsonLength=64*1024*1024};}
  static string Canonical(object value){
   if(value==null)return "null";
   var dictionary=value as IDictionary<string,object>;
   if(dictionary!=null)return "{"+string.Join(",",dictionary.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>Serializer().Serialize(x.Key)+":"+Canonical(x.Value)))+"}";
   var list=value as IEnumerable;if(list!=null&&!(value is string))return "["+string.Join(",",list.Cast<object>().Select(Canonical))+"]";
   return Serializer().Serialize(value);
  }
  public static string Hash(object value){var data=Canonical(Serializer().DeserializeObject(Serializer().Serialize(value)));using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(data))).Replace("-","").ToLowerInvariant();}
  public static SimulationDirtyFlags Changed(SimulationProject before,SimulationProject after){
   if(before==null)return SimulationDirtyFlags.Source|SimulationDirtyFlags.Mesh|SimulationDirtyFlags.Mjcf;
   var flags=SimulationDirtyFlags.None;
   if(Hash(before.attachments)!=Hash(after.attachments))flags|=SimulationDirtyFlags.Site;
   if(Hash(before.collision)!=Hash(after.collision))flags|=SimulationDirtyFlags.Collision;
   if(Hash(new{before.joints,before.base_mode,before.joint_defaults,before.joint_force_limits})!=Hash(new{after.joints,after.base_mode,after.joint_defaults,after.joint_force_limits}))flags|=SimulationDirtyFlags.Joint;
   if(Hash(before.equalities)!=Hash(after.equalities))flags|=SimulationDirtyFlags.Equality;
   if(Hash(before.actuators)!=Hash(after.actuators))flags|=SimulationDirtyFlags.Actuator;
   if(Hash(before.sensors)!=Hash(after.sensors))flags|=SimulationDirtyFlags.Sensor;
   if(Hash(before.solver)!=Hash(after.solver))flags|=SimulationDirtyFlags.Solver;
   if(Hash(before.site_forces)!=Hash(after.site_forces))flags|=SimulationDirtyFlags.SiteForce;
   return flags==SimulationDirtyFlags.None?flags:flags|SimulationDirtyFlags.Mjcf;
  }
 }
 public sealed class ExportPlan {
  public bool RebuildSource {get;private set;}
  public SimulationDirtyFlags Dirty {get;private set;}
  public static ExportPlan Build(bool verifiedSource,SimulationDirtyFlags hint){return new ExportPlan{RebuildSource=!verifiedSource,Dirty=hint|(!verifiedSource?SimulationDirtyFlags.Source|SimulationDirtyFlags.Mesh:SimulationDirtyFlags.None)|SimulationDirtyFlags.Mjcf};}
 }
}
