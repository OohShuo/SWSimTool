using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
namespace SWSimTool.Persistence {
    public sealed class ConfigurationBackupData {
        public string product{get;set;}="SWSimTool";
        public int version{get;set;}=1;
        public string document{get;set;}
        public string configuration{get;set;}
        public string assembly_version{get;set;}
        public string created_utc{get;set;}
        public string node_name{get;set;}
        public string payload{get;set;}
        public string sha256{get;set;}
    }
    public static class ConfigurationBackup {
        static string Hash(string value){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value??""))).Replace("-","").ToLowerInvariant();}
        static string Digest(ConfigurationBackupData data)=>Hash(DocumentEnvelopeSerializer.Serializer().Serialize(new{data.product,data.version,data.document,data.configuration,data.assembly_version,data.created_utc,data.node_name,data.payload}));
        public static string Write(string folder,string document,string configuration,string payload,string version,string nodeName=null){
            Directory.CreateDirectory(folder);string path=Path.Combine(folder,"configuration-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N")+".swsimtool-backup.json");
            var backup=new ConfigurationBackupData{document=document,configuration=configuration,payload=payload,node_name=nodeName,assembly_version=version,created_utc=DateTime.UtcNow.ToString("O")};backup.sha256=Digest(backup);
            var bytes=new UTF8Encoding(false).GetBytes(DocumentEnvelopeSerializer.Serializer().Serialize(backup));
            using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes,0,bytes.Length);file.Flush(true);}
            var verified=Read(path);if(verified.payload!=payload||verified.document!=document||verified.configuration!=configuration)throw new InvalidDataException("配置备份回读不一致；未执行替换。");return path;
        }
        public static ConfigurationBackupData Read(string path){
            var data=DocumentEnvelopeSerializer.Serializer().Deserialize<ConfigurationBackupData>(File.ReadAllText(path,Encoding.UTF8));
            if(data==null||data.product!="SWSimTool"||data.version!=1||data.sha256!=Digest(data)||string.IsNullOrWhiteSpace(data.document)||string.IsNullOrWhiteSpace(data.configuration))throw new InvalidDataException("配置备份损坏或格式不支持。");return data;
        }
    }
}
