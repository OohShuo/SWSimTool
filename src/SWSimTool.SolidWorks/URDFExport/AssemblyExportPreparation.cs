using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SWSimTool.Simulation;
using SWSimTool.URDF;

namespace SWSimTool.URDFExport {
    // Both UI entry points use business settings and an independent export tree.
    public static class AssemblyExportPreparation {
        public sealed class Prepared {public LinkNode Tree {get;internal set;}public ExportHelper Exporter {get;internal set;}}
        public static LinkNode CopyTree(Link tree){
            if(tree==null)throw new InvalidDataException("尚未建立 URDF 树，请先进入 URDF 配置。");
            var node=new LinkNode(tree.Clone());StableReferences.ValidateTree(node);return node;
        }
        public static Prepared Prepare(SldWorks app,Link tree,SimulationProject project){
            var model=app.ActiveDoc as ModelDoc2;
            var assembly=model as AssemblyDoc;if(assembly==null)throw new InvalidOperationException("该入口需要装配；零件使用独立导出流程。");
            var lease=ConfigurationSession.Capture(app,model);lease.RequireCurrent();
            var nodes=CopyTree(tree);var settings=project?.urdf_export??new UrdfExportSettings();
            Action<LinkNode> validate=null;validate=n=>{if(string.IsNullOrWhiteSpace(n.Link.Name)||(!n.IsBaseNode&&string.IsNullOrWhiteSpace(n.Link.Joint.Name)))throw new InvalidDataException("URDF 配置不完整："+n.Text+" 缺少 link/joint 名称，请进入 URDF 配置。");foreach(LinkNode child in n.Nodes)validate(child);};validate(nodes);
            int resolved=assembly.ResolveAllLightWeightComponents(true);
            if(resolved!=(int)swComponentResolveStatus_e.swResolveOk)throw new InvalidDataException("组件未能完全解析，请先解析轻化组件后重新导出。");
            var missing=new List<string>();CommonSwOperations.LoadSWComponents(model,nodes,missing);
            if(missing.Count>0)throw new InvalidDataException("组件引用失效："+string.Join(", ",missing)+"。请进入 URDF 配置重新选择。");
            Action<LinkNode> documents=null;documents=n=>{foreach(Component2 component in n.Link.SWComponents)if(component.GetModelDoc2()==null)throw new InvalidDataException("组件模型未加载："+component.Name2);if(n.Nodes.Count>0&&n.Link.SWComponents.Count==0)throw new InvalidDataException("URDF 配置不完整："+n.Text+" 有子 link，但未指定组件。");foreach(LinkNode child in n.Nodes)documents(child);};documents(nodes);
            CadTreeReferences.Normalize(model,nodes,true);
            var exporter=new ExportHelper(app);exporter.BeginExportSession();
            exporter.GetExportSimulation().Project=ConfigurationEditingContext.CopyProject(project??new SimulationProject());
            exporter.SetComputeInertial(settings.inertia);exporter.SetComputeVisualCollision(settings.geometry);exporter.SetComputeJointKinematics(settings.kinematics);exporter.SetComputeJointLimits(settings.limits);
            lease.RequireCurrent();if(!exporter.CreateRobotFromTreeView(nodes))throw new InvalidDataException("无法完成 URDF 模型计算，请检查 URDF 配置。");lease.RequireCurrent();
            exporter.GetExportSimulation().DraftTree=nodes.Snapshot();exporter.GetExportSimulation().SetCollisionTree(nodes);
            return new Prepared{Tree=nodes,Exporter=exporter};
        }
    }
}
