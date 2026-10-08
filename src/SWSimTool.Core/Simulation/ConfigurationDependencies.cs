using System;
using System.Collections.Generic;
using System.Linq;
namespace SWSimTool.Simulation {
    public sealed class ConfigurationDeletion {
        public readonly SimulationProject Result;
        public readonly string[] Affected;
        internal ConfigurationDeletion(SimulationProject result,IEnumerable<string> affected){Result=result;Affected=affected.ToArray();}
        public void ApplyTo(SimulationProject target){
            ConfigurationIdentityValidation.Validate(Result);
            Replace(target.attachments,Result.attachments);Replace(target.sensors,Result.sensors);Replace(target.equalities,Result.equalities);
            Replace(target.actuators,Result.actuators);Replace(target.site_forces,Result.site_forces);Replace(target.joints,Result.joints);Replace(target.joint_force_limits,Result.joint_force_limits);
            if(target.collision!=null&&Result.collision!=null){Replace(target.collision.geometries,Result.collision.geometries);Replace(target.collision.allowed_pairs,Result.collision.allowed_pairs);target.collision.link_modes_by_id.Clear();foreach(var p in Result.collision.link_modes_by_id)target.collision.link_modes_by_id.Add(p.Key,p.Value);}
        }
        static void Replace<T>(List<T> target,List<T> source){target.Clear();target.AddRange(source);}
    }
    public static class ConfigurationDependencies {
        public static ConfigurationDeletion Plan(SimulationProject original,IEnumerable<string> links=null,IEnumerable<string> joints=null,IEnumerable<string> sites=null){
            ConfigurationIdentityValidation.Validate(original);
            var l=new HashSet<string>(links??new string[0]);var j=new HashSet<string>(joints??new string[0]);var s=new HashSet<string>(sites??new string[0]);
            foreach(var a in original.attachments.Where(a=>l.Contains(a.link_id)))s.Add(a.id);
            var siteNames=new HashSet<string>(original.attachments.Where(a=>s.Contains(a.id)).Select(a=>a.name));
            Func<string,string,bool> removedSite=(id,name)=>!string.IsNullOrWhiteSpace(id)?s.Contains(id):siteNames.Contains(name);
            var copy=original.CopyCollections();var affected=new List<string>();
            Remove(copy.attachments,a=>s.Contains(a.id),a=>"site: "+a.name,affected);
            Remove(copy.sensors,a=>removedSite(a.site_id,a.site),a=>"sensor: "+a.name,affected);
            Remove(copy.site_forces,a=>removedSite(a.site1_id,a.site1)||removedSite(a.site2_id,a.site2),a=>"force/spring: "+a.name,affected);
            Remove(copy.equalities,a=>a.type=="joint"?(j.Contains(a.joint1_id)||j.Contains(a.joint2_id)):a.binding=="body"?(l.Contains(a.body1_id)||l.Contains(a.body2_id)):(removedSite(a.site1_id,a.site1)||removedSite(a.site2_id,a.site2)),a=>"constraint: "+a.name,affected);
            Remove(copy.actuators,a=>j.Contains(a.joint_id),a=>"actuator: "+a.name,affected);
            Remove(copy.joints,a=>j.Contains(a.joint_id),a=>"joint settings: "+a.joint,affected);
            Remove(copy.joint_force_limits,a=>j.Contains(a.joint_id),a=>"joint force limits: "+a.joint,affected);
            if(copy.collision!=null){
                Remove(copy.collision.geometries,a=>l.Contains(a.link_id),a=>"collision geometry: "+a.name,affected);
                Remove(copy.collision.allowed_pairs,a=>l.Contains(a.link1_id)||l.Contains(a.link2_id),a=>"contact pair: "+a.link1+" / "+a.link2,affected);
                foreach(var id in copy.collision.link_modes_by_id.Keys.Where(l.Contains).ToArray()){copy.collision.link_modes_by_id.Remove(id);affected.Add("collision mode: "+id);}
            }
            ConfigurationIdentityValidation.Validate(copy);return new ConfigurationDeletion(copy,affected);
        }
        static void Remove<T>(List<T> items,Func<T,bool> match,Func<T,string> description,List<string> affected){foreach(var item in items.Where(match).ToArray()){affected.Add(description(item));items.Remove(item);}}
    }
}
