using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SWSimTool.RobotModel;
using SWSimTool.URDFExport;

namespace SWSimTool.Simulation
{
    internal static class ProjectSourceBuilder
    {
        internal static CadSourceSnapshot BuildNative(SldWorks app,ModelDoc2 document,string workspace,SimulationProject project)=>Build(app,document,workspace,project,BuildNative);
        internal static CadSourceSnapshot BuildReference(SldWorks app,ModelDoc2 document,string workspace,SimulationProject project)=>Build(app,document,workspace,project,BuildReference);
        static CadSourceSnapshot Build(SldWorks app,ModelDoc2 document,string workspace,SimulationProject project,Func<ExportHelper,SWSimTool.URDF.LinkNode,string,CadSourceSnapshot> buildSource)
        {
            bool error;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(document,out error);
            StableReferences.ValidateIdentities(tree);
            if(error||tree==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");
            CadTreeReferences.Normalize(document,tree,true);
            LoadComponents(document,tree);
            var helper=new ExportHelper(app){SavePath=workspace,PackageName="robot",ShowExportLocation=false,ExportSimulationInformation=true};
            helper.GetExportSimulation().Project=project;
            var result=buildSource(helper,tree,workspace);
            result.Frames=helper.GetExportSimulation().LinkTransforms().ToDictionary(x=>x.Key,x=>x.Value.ToRowMajorArray());
            helper.GetExportSimulation().UseExportFrames(result.Frames);
            result.Geometry=helper.GetExportSimulation().ResolveNativeGeometry(result.Core);
            return result;
        }
        static void LoadComponents(ModelDoc2 document,SWSimTool.URDF.LinkNode tree)
        {
            var unresolved=new List<string>();CommonSwOperations.LoadSWComponents(document,tree,unresolved);
            if(unresolved.Count!=0)throw new InvalidDataException("Unresolved CAD component reference in link: "+string.Join(", ",unresolved));
        }
        static CadSourceSnapshot BuildNative(ExportHelper helper,SWSimTool.URDF.LinkNode tree,string workspace)
        {
            helper.EnsureNativeReferences(tree);helper.GetExportSimulation().SetCollisionTree(tree);
            var directory=Path.Combine(workspace,"raw-mesh");
            var sources=helper.PlanConfiguredNativeMeshes(tree,directory);
            var core=CadRobotCoreBuilder.Build("robot",helper.GetExportSimulation(),tree,sources);
            helper.GetExportSimulation().UseExportFrames(helper.GetExportSimulation().LinkTransforms().ToDictionary(x=>x.Key,x=>x.Value.ToRowMajorArray()));
            helper.ExportConfiguredNativeMeshes(tree,directory);
            if(helper.URDFRobot!=null)throw new InvalidOperationException("Native source must not construct a URDF Robot");
            return new CadSourceSnapshot{Core=core};
        }
        // Explicit reference harness only; never a fallback from BuildNative.
        static CadSourceSnapshot BuildReference(ExportHelper helper,SWSimTool.URDF.LinkNode tree,string workspace)
        {
            if(!helper.CreateRobotFromTreeView(tree))throw new InvalidOperationException("URDF 构建失败，请检查 link / joint 配置。");
            helper.ExportRobot();
            if(helper.LastURDFPath==null||helper.LastSimulationPath==null)throw new IOException("临时 URDF 或附加配置导出失败。");
            var sources=new Dictionary<string,MeshSource>();Action<SWSimTool.URDF.Link> collect=null;
            collect=link=>{string path=Path.Combine(workspace,"robot","meshes",link.Name.Replace('/','_')+".STL");if(File.Exists(path))sources.Add(link.Name,new MeshSource(link.StableId+"/mesh",path,new Vector3d(1,1,1)));foreach(var child in link.Children)collect(child);};collect(helper.URDFRobot.BaseLink);
            return new CadSourceSnapshot{Core=SolidWorksRobotModelBuilder.FromResolvedRobot(helper.URDFRobot,sources),Urdf=helper.LastURDFPath,Sidecar=helper.LastSimulationPath,LegacyData=ExportFingerprint.Serializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(helper.LastSimulationPath))};
        }
    }
}
