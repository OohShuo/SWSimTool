using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SW2URDF.RobotModel;
using SW2URDF.URDFExport;

namespace SW2URDF.Simulation
{
    internal sealed class ProjectSourceResult
    {
        internal RobotCoreSnapshot Core;
        internal ResolvedSimulationGeometry Geometry;
        internal Dictionary<string,double[]> Frames;
        internal string Urdf,Sidecar;
        internal Dictionary<string,object> LegacyData;
    }
    internal static class ProjectSourceBuilder
    {
        internal static ProjectSourceResult Build(SldWorks app,ModelDoc2 document,string workspace,SimulationProject project,bool native)
        {
            bool error;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(document,out error);
            if(error||tree==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");
            CommonSwOperations.LoadSWComponents(document,tree,new List<string>());
            var helper=new ExportHelper(app){SavePath=workspace,PackageName="robot",ShowExportLocation=false,ExportSimulationInformation=true};
            helper.GetSimulation().Project=project;
            var result=native?BuildNative(helper,tree,workspace):BuildReference(helper,tree,workspace);
            result.Frames=helper.GetSimulation().LinkTransforms().ToDictionary(x=>x.Key,x=>x.Value.ToRowMajorArray());
            helper.GetSimulation().UseExportFrames(result.Frames);
            result.Geometry=helper.GetSimulation().ResolveNativeGeometry(result.Core);
            return result;
        }
        static ProjectSourceResult BuildNative(ExportHelper helper,SW2URDF.URDF.LinkNode tree,string workspace)
        {
            helper.EnsureNativeReferences(tree);helper.GetSimulation().SetCollisionTree(tree);
            var directory=Path.Combine(workspace,"raw-mesh");
            var sources=helper.PlanConfiguredNativeMeshes(tree,directory);
            var core=CadRobotCoreBuilder.Build("robot",helper.GetSimulation(),tree,sources);
            helper.GetSimulation().UseExportFrames(helper.GetSimulation().LinkTransforms().ToDictionary(x=>x.Key,x=>x.Value.ToRowMajorArray()));
            helper.ExportConfiguredNativeMeshes(tree,directory);
            if(helper.URDFRobot!=null)throw new InvalidOperationException("Native source must not construct a URDF Robot");
            return new ProjectSourceResult{Core=core};
        }
        // Explicit reference harness only; never a fallback from BuildNative.
        static ProjectSourceResult BuildReference(ExportHelper helper,SW2URDF.URDF.LinkNode tree,string workspace)
        {
            if(!helper.CreateRobotFromTreeView(tree))throw new InvalidOperationException("URDF 构建失败，请检查 link / joint 配置。");
            helper.ExportRobot();
            if(helper.LastURDFPath==null||helper.LastSimulationPath==null)throw new IOException("临时 URDF 或附加配置导出失败。");
            var sources=new Dictionary<string,MeshSource>();Action<SW2URDF.URDF.Link> collect=null;
            collect=link=>{string path=Path.Combine(workspace,"robot","meshes",link.Name.Replace('/','_')+".STL");if(File.Exists(path))sources.Add(link.Name,new MeshSource(link.StableId+"/mesh",path,new Vector3d(1,1,1)));foreach(var child in link.Children)collect(child);};collect(helper.URDFRobot.BaseLink);
            return new ProjectSourceResult{Core=SolidWorksRobotModelBuilder.FromResolvedRobot(helper.URDFRobot,sources),Urdf=helper.LastURDFPath,Sidecar=helper.LastSimulationPath,LegacyData=ExportFingerprint.Serializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(helper.LastSimulationPath))};
        }
    }
}
