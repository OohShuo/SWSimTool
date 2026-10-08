using System;
using System.Collections.Generic;
using System.IO;
namespace SWSimTool.Simulation {
    public static class ConfigurationIdentityValidation {
        static void Check<T>(IEnumerable<T> items,Func<T,string> id,Func<T,string> name,string category){
            if(items==null)throw new InvalidDataException("缺少配置集合："+category);
            var ids=new Dictionary<string,string>();var names=new Dictionary<string,string>();int index=0;
            foreach(var item in items){var path=category+"["+(index++)+"]";if(item==null)throw new InvalidDataException("空配置对象："+path);
                Add(ids,id(item),path,"ID");Add(names,name(item),path,"名称");}
        }
        static void Add(Dictionary<string,string> seen,string value,string path,string kind){
            if(string.IsNullOrWhiteSpace(value))return;string previous;
            if(seen.TryGetValue(value,out previous))throw new InvalidDataException("重复 "+kind+" '"+value+"'："+previous+" 与 "+path+"。不会忽略或覆盖对象。");
            seen.Add(value,path);
        }
        public static void Validate(SimulationProject p){
            Check(p.attachments,x=>x.id,x=>x.name,"site");Check(p.actuators,x=>x.id,x=>x.name,"actuator");
            Check(p.sensors,x=>x.id,x=>x.name,"sensor");Check(p.equalities,x=>x.id,x=>x.name,"constraint");
            Check(p.site_forces,x=>x.id,x=>x.name,"site force/spring");
            if(p.collision!=null)Check(p.collision.geometries,x=>x.id,x=>x.name,"collision geometry");
        }
    }
}
