using System;
using System.Runtime.CompilerServices;
using SolidWorks.Interop.sldworks;
using SWSimTool.Simulation;
using SWSimTool.URDF;

namespace SWSimTool.UI {
    // UI ownership is separate from the confirmed business draft.
    public sealed class ConfigurationPageDraft : IDisposable {
        sealed class Holder {public ConfigurationPageDraft Active;}
        static readonly ConditionalWeakTable<ModelDoc2,Holder> pages=new ConditionalWeakTable<ModelDoc2,Holder>();
        readonly ModelDoc2 model;readonly ConfigurationEditingContext context;
        bool disposed;
        public SimulationProject Project {get;set;}
        public Link Tree {get;set;}
        public Action Flush {get;set;}
        public Action Close {get;set;}
        ConfigurationPageDraft(ModelDoc2 model,ConfigurationEditingContext context){this.model=model;this.context=context;Project=ConfigurationEditingContext.CopyProject(context.Project);Tree=context.Tree?.Clone();}
        public static ConfigurationPageDraft Open(ModelDoc2 model,ConfigurationEditingContext context){
            context.Session.RequireCurrent();var holder=pages.GetValue(model,x=>new Holder());
            if(holder.Active!=null&&!holder.Active.disposed)throw new InvalidOperationException("请先确认或取消当前配置页面，再打开另一配置页面。");
            return holder.Active=new ConfigurationPageDraft(model,context);
        }
        public static ConfigurationPageDraft Active(ModelDoc2 model){Holder holder;return pages.TryGetValue(model,out holder)?holder.Active:null;}
        public static void RequireAvailable(ModelDoc2 model){if(Active(model)!=null)throw new InvalidOperationException("请先确认或取消当前配置页面，再打开另一配置页面。");}
        public void RequireCurrent(){if(disposed)throw new System.IO.InvalidDataException("配置页面已关闭。");context.Session.RequireCurrent();}
        public void Collect(){RequireCurrent();Flush?.Invoke();RequireCurrent();}
        public void Commit(){RequireCurrent();context.Commit(Project,Tree);}
        public void Dispose(){if(disposed)return;disposed=true;Flush=null;Close=null;Holder holder;if(pages.TryGetValue(model,out holder)&&ReferenceEquals(holder.Active,this))holder.Active=null;}
    }
}
