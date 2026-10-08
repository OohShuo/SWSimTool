using System;
using System.IO;
namespace SWSimTool.Simulation {
    public interface IConfigurationTransactionStore:IDocumentStore { void RestoreConfiguration(string original); }
    public static class ConfigurationReplacement {
        public static string Replace(IConfigurationTransactionStore store,string candidate,Action requireCurrent,Action<string> validate,Func<string,string> backup){
            requireCurrent();validate(candidate);string original=store.ReadConfiguration();
            string savedBackup=backup(original);if(string.IsNullOrWhiteSpace(savedBackup))throw new IOException("未生成可恢复备份，停止替换。");
            requireCurrent();
            try{store.WriteConfiguration(candidate);string actual=store.ReadConfiguration();if(actual!=candidate)throw new InvalidDataException("配置替换回读不一致。");validate(actual);return savedBackup;}
            catch(Exception error){
                try{store.RestoreConfiguration(original);if(store.ReadConfiguration()!=original)throw new InvalidDataException("旧配置恢复回读不一致。");}
                catch(Exception rollback){throw new AggregateException("配置替换和回滚均失败。保留当前草稿；请使用备份恢复："+savedBackup,error,rollback);}
                throw new IOException("配置替换失败，旧保存内容已恢复，当前草稿保留。备份："+savedBackup,error);
            }
        }
    }
}
