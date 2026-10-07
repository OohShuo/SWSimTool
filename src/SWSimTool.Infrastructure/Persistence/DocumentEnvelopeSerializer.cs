using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
namespace SWSimTool.Persistence {
    public static class DocumentEnvelopeSerializer {
        public static JavaScriptSerializer Serializer()=>new JavaScriptSerializer{MaxJsonLength=16*1024*1024};
        public static T Deserialize<T>(string data,int supportedVersion) {
            var serializer=Serializer();var envelope=serializer.Deserialize<Dictionary<string,object>>(data);object version;
            if(envelope==null||!envelope.TryGetValue("version",out version)||!(version is int)||(int)version!=supportedVersion||!envelope.ContainsKey("configurations"))throw new InvalidDataException("SWSimTool requires an explicit version 2 document envelope.");
            return serializer.ConvertToType<T>(envelope);
        }
    }
}
