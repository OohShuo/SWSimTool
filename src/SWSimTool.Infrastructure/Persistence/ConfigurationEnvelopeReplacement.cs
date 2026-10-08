using System;
using System.IO;
namespace SWSimTool.Simulation {
 public static class ConfigurationEnvelopeReplacement {
        public static string ReplaceEntry(string original,string configuration,string configurationId,string entryJson){
            var serializer=SWSimTool.Persistence.DocumentEnvelopeSerializer.Serializer();
            var envelope=string.IsNullOrWhiteSpace(original)?new System.Collections.Generic.Dictionary<string,object>{{"version",2},{"configurations",new System.Collections.Generic.Dictionary<string,object>()}}:serializer.Deserialize<System.Collections.Generic.Dictionary<string,object>>(original);
            object version,entriesValue;
            if(!envelope.TryGetValue("version",out version)||Convert.ToInt32(version)!=2||!envelope.TryGetValue("configurations",out entriesValue))throw new InvalidDataException("Unsupported configuration envelope");
            var entries=entriesValue as System.Collections.Generic.Dictionary<string,object>;
            if(entries==null)throw new InvalidDataException("Invalid configuration entries");
            // Preserve all other entries and unknown fields. Replace only the
            // uniquely identified current entry, without parsing its old model.
            string target=null;
            foreach(var pair in entries){
                var data=pair.Value as System.Collections.Generic.Dictionary<string,object>;object id;
                if(data==null)throw new InvalidDataException("Invalid configuration entry: "+pair.Key);
                bool identified=data.TryGetValue("configuration_id",out id)&&!string.IsNullOrWhiteSpace(id as string);
                if(identified?(string)id==configurationId:pair.Key==configuration){if(target!=null)throw new InvalidDataException("Duplicate SW configuration identity");target=pair.Key;}
            }
            if(target!=null)entries.Remove(target);
            if(entries.ContainsKey(configuration))throw new InvalidDataException("Configuration name belongs to a different stable identity");
            entries.Add(configuration,serializer.DeserializeObject(entryJson));
            return serializer.Serialize(envelope);
        }
 }
}
