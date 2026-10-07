using System;using System.Collections.Generic;using System.IO;using System.Linq;
namespace SWSimTool.Simulation { public static class DomainStableReferences {
        public static void MigrateLegacyLinkModesByName(SimulationProject project,IDictionary<string,string> namesById)
        {
            var collision=project?.collision;if(collision==null)return;
            if(collision.link_modes==null||collision.link_modes_by_id==null)throw new InvalidDataException("Collision modes are corrupt");
            if(!collision.link_modes_migrated&&collision.link_modes_by_id.Count==0){
                var migrated=new Dictionary<string,string>();
                foreach(var pair in collision.link_modes){var matches=namesById.Where(x=>x.Value==pair.Key).ToArray();if(matches.Length!=1)throw new InvalidDataException("Unknown or ambiguous legacy collision link: "+pair.Key);migrated.Add(matches[0].Key,pair.Value);}
                collision.link_modes_by_id=migrated;
            }
            collision.link_modes_migrated=true;collision.link_modes.Clear();
        }
        public static void RemapLinkModesByStableId(SimulationProject project)
        {
            var collision=project?.collision;if(collision==null)return;
            if(!collision.link_modes_migrated)throw new InvalidDataException("Migrate legacy collision modes before remapping");
            // Deleted IDs remain unresolved. Never transfer their settings to a same-name link.
            collision.link_modes_by_id=new Dictionary<string,string>(collision.link_modes_by_id);collision.link_modes.Clear();
        }
}}
