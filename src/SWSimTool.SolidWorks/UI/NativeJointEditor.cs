using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using SWSimTool.Simulation;

namespace SWSimTool.UI {
    public sealed class NativeJointEditor : IDisposable {
        readonly AttachmentService service;
        readonly List<JointDescriptor> descriptors;
        readonly SimulationProject draft;
        readonly CollisionPreview preview;
        readonly Timer timer=new Timer{Interval=180},cadTimer=new Timer{Interval=500};
        public NativeParameterFields Fields {get;private set;}
        JointDescriptor descriptor;JointConfiguration current;
        bool ownsPage,disposed,show=true,disabled;
        string status="",revision,previewKey;
        ConstraintSettings limitValues,frictionValues;
        bool localLimit,localFriction;
        bool Editable=>current!=null&&new[]{"revolute","continuous","prismatic"}.Contains(descriptor.type);
        string Unit=>current!=null&&(current.type=="slide"||current.type=="inherit"&&descriptor.type=="prismatic")?"m":"rad";
        bool Limits=>Editable&&(current.limit_mode=="custom"||current.limit_mode=="inherit"&&descriptor.type!="continuous");
        public NativeJointEditor(AttachmentService service,List<JointDescriptor> descriptors,IPropertyManagerPageGroup group){try{
            this.service=service;service.BeginPage();ownsPage=true;this.descriptors=descriptors??throw new ArgumentNullException(nameof(descriptors));draft=service.Project;
            revision=service.CollisionRevision;preview=new CollisionPreview(service);
            if(descriptors.Count>0)Select(descriptors[0].name);
            Fields=new NativeParameterFields(group,Require);
            Fields.Choice("joint",()=>"所属 joint（各关节独立配置）",descriptors.Select(d=>d.name).ToArray(),()=>descriptor?.name,Select,()=>descriptors.Count>0);
            Fields.Choice("base",()=>"根基座模式",new[]{"inherit","fixed","floating"},()=>draft.base_mode,v=>draft.base_mode=v);
            Fields.Note("base-note",()=>"inherit 沿用原配置；fixed 固定；floating 六自由度浮动，仅作用于根基座。");
            Fields.Note("joint-note",()=>descriptor==null?"没有可配置关节，仍可设置根基座模式。":descriptor.parent+" → "+descriptor.child+"；URDF 类型："+descriptor.type);
            Fields.Note("unsupported",()=>"固定、浮动及 planar 关节请在 URDF 配置中调整结构。",()=>descriptor!=null&&!Editable);
            Fields.Choice("type",()=>"类型",new[]{"inherit","hinge","slide"},()=>current?.type,v=>current.type=v,()=>Editable);
            Fields.Note("physics-note",()=>"位置和轴向来自 URDF 树。物理参数按 actuator 引用提供默认值，手动值优先；留空恢复默认。",()=>Editable);
            Physics("damping",()=>"阻尼 damping（"+(Unit=="rad"?"N·m·s/rad":"N·s/m")+"）",()=>current?.damping,v=>current.damping=v);
            Physics("frictionloss",()=>"干摩擦 frictionloss（"+(Unit=="rad"?"N·m":"N")+"）",()=>current?.frictionloss,v=>current.frictionloss=v);
            Physics("armature",()=>"附加惯量 armature（"+(Unit=="rad"?"kg·m²":"kg")+"）",()=>current?.armature,v=>current.armature=v);
            Fields.Choice("limit-mode",()=>"限位：inherit 沿用 / none 无 / custom 自定义",new[]{"inherit","none","custom"},()=>current?.limit_mode,v=>{current.limit_mode=v;if(v=="custom"){current.lower=current.lower??descriptor.lower;current.upper=current.upper??descriptor.upper;}Fields.LoadValues("lower","upper");},()=>Editable);
            Fields.Number("lower",()=>"限位下限 "+Unit,()=>current?.lower,v=>current.lower=v,false,()=>Editable&&current.limit_mode=="custom");
            Fields.Number("upper",()=>"限位上限 "+Unit,()=>current?.upper,v=>current.upper=v,false,()=>Editable&&current.limit_mode=="custom");
            Fields.Number("margin",()=>"限位提前量 margin（"+Unit+"）",()=>current?.margin,v=>current.margin=v,true,()=>Limits);
            Fields.Check("limit-local",()=>"自定义限位求解参数",()=>localLimit,v=>{localLimit=v;current.limit_solver=v?limitValues:null;},()=>Limits);
            Solver("limit",()=>limitValues,()=>Limits&&localLimit);
            Fields.Check("friction-local",()=>"自定义干摩擦求解参数",()=>localFriction,v=>{localFriction=v;current.friction_solver=v?frictionValues:null;},()=>Editable&&(current.frictionloss??DefaultPhysics("frictionloss"))>0);
            Solver("friction",()=>frictionValues,()=>Editable&&(current.frictionloss??DefaultPhysics("frictionloss"))>0&&localFriction);
            Fields.Choice("spring-mode",()=>"弹簧：inherit 沿用 / off 无 / custom 自定义",new[]{"inherit","off","custom"},()=>current?.spring_mode,v=>{current.spring_mode=v;current.stiffness=v=="inherit"?(double?)null:0;current.springref=v=="custom"?current.springref??current.@ref??0:null;Fields.LoadValues("stiffness","springref");},()=>Editable);
            Fields.Number("stiffness",()=>"弹簧刚度（"+(Unit=="rad"?"N·m/rad":"N/m")+"）",()=>current?.stiffness,v=>current.stiffness=v,false,()=>Editable&&current.spring_mode=="custom");
            Fields.Number("springref",()=>"弹簧平衡位置 "+Unit,()=>current?.springref??0,v=>current.springref=v,false,()=>Editable&&current.spring_mode=="custom");
            Fields.Number("ref",()=>"参考坐标值 ref（"+Unit+"，不改变装配姿态）",()=>current?.@ref,v=>current.@ref=v,true,()=>Editable);
            Fields.Check("preview",()=>"实时预览关节轴 / 限位范围",()=>show,v=>show=v);
            Fields.Button("refresh","刷新 CAD 参考",()=>{service.InvalidateCADCache();previewKey=null;Schedule();});
            Fields.Button("save","保存配置到装配",()=>Save());
            Fields.Note("status",()=>status);
            Fields.Changed=Schedule;Fields.LoadValues();
            timer.Tick+=(s,e)=>{timer.Stop();Guard(Preview);};
            cadTimer.Tick+=(s,e)=>{try{Require();if(revision!=service.CollisionRevision){revision=service.CollisionRevision;Schedule();}}catch(Exception ex){disabled=true;timer.Stop();cadTimer.Stop();preview.Clear();status=ex.Message;Fields.Refresh();}};
            service.PageDraft.Flush=Flush;cadTimer.Start();
        }catch{Dispose();throw;}}
        double DefaultPhysics(string key){if(current==null)return 0;bool driven=draft.actuators.Any(a=>!string.IsNullOrWhiteSpace(current.joint_id)?a.joint_id==current.joint_id:string.IsNullOrWhiteSpace(a.joint_id)&&a.joint==current.joint);return key=="armature"?(driven?.001:0):(driven?.01:.001);}
        void Physics(string key,Func<string> caption,Func<double?> read,Action<double?> write){Fields.Number(key,caption,()=>current==null?(double?)null:read()??DefaultPhysics(key),write,true,()=>Editable);Fields.Note(key+"-state",()=>read().HasValue?"手动值（留空恢复默认）":"默认值："+DefaultPhysics(key).ToString("G12",CultureInfo.InvariantCulture)+"（按 actuator 引用）",()=>Editable);}
        void Solver(string prefix,Func<ConstraintSettings> settings,Func<bool> visible){
            var names=new[]{"timeconst","dampratio","dmin","dmax","width","midpoint","power"};var captions=new[]{"solref 时间常数 s","solref 阻尼比","solimp 起始阻抗","solimp 末端阻抗","solimp 变化宽度 ","solimp 中点比例","solimp 曲线指数"};
            for(int i=0;i<names.Length;i++){int slot=i;var property=typeof(ConstraintSettings).GetProperty(names[i]);Fields.Number(prefix+"-"+names[i],()=>captions[slot]+(slot==4?Unit:""),()=>settings()==null?(double?)null:(double)property.GetValue(settings()),v=>property.SetValue(settings(),v.Value),false,visible);}
        }
        void Select(string name){
            if(Fields!=null){try{Fields.Validate();}catch{Fields.LoadValues("joint");throw;}}
            descriptor=descriptors.First(d=>d.name==name);
            current=draft.joints.FirstOrDefault(j=>!string.IsNullOrWhiteSpace(descriptor.id)?j.joint_id==descriptor.id:string.IsNullOrWhiteSpace(j.joint_id)&&j.joint==name);
            if(current==null){current=new JointConfiguration{joint=name,joint_id=descriptor.id};if(new[]{"revolute","continuous","prismatic"}.Contains(descriptor.type))draft.joints.Add(current);}
            localLimit=current.limit_solver!=null;localFriction=current.friction_solver!=null;
            limitValues=current.limit_solver??JointConfiguration.LimitDefaults();frictionValues=current.friction_solver??new ConstraintSettings{timeconst=.02,dmin=.9,dmax=.95};
            Fields?.LoadValues();previewKey=null;
        }
        void Require(){if(disposed||disabled)throw new InvalidOperationException("关节配置页面已失效，请关闭后重新进入。");service.RequireCurrentDocument();service.PageDraft.RequireCurrent();}
        void Flush(){Require();Fields.Validate();draft.joint_defaults=true;}
        public ConfigurationCommitResult Save(){Flush();draft.ValidateSolver();foreach(var j in draft.joints){var d=descriptors.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(j.joint_id)?x.id==j.joint_id:x.name==j.joint);if(d==null)throw new InvalidOperationException("关节已失效："+j.joint);bool changed=j.type!="inherit"&&j.type!=(d.type=="prismatic"?"slide":"hinge");if(changed&&j.limit_mode=="inherit")throw new InvalidOperationException("改变关节类型时必须选择无限位或自定义限位。");}var result=service.Save();status=result.RefreshSucceeded?"配置已写入装配，请保存 .sldasm。":result.Message;if(!result.RefreshSucceeded){disabled=true;timer.Stop();cadTimer.Stop();preview.Clear();}Fields.Refresh();return result;}
        public void Guard(Action action){try{action();}catch(Exception e){status=e.Message;if(!disposed){Fields?.Refresh();}}}
        void Schedule(){if(disposed||disabled)return;timer.Stop();timer.Start();}
        void Preview(){Require();if(!show||!Editable){preview.Clear();previewKey=null;return;}string key=service.CollisionRevision+new JavaScriptSerializer().Serialize(new{descriptor,current.joint,current.type,current.limit_mode,current.lower,current.upper,current.@ref});if(key==previewKey)return;preview.Show(service.JointPreview(descriptor,current),null);previewKey=key;status="关节预览已更新。";Fields.Refresh();}
        public void Dispose(){if(disposed)return;disposed=true;timer.Dispose();cadTimer.Dispose();preview?.Dispose();Fields?.Dispose();if(ownsPage){ownsPage=false;service.EndPage();}}
    }
}