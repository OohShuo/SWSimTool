using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDF;
using SW2URDF.RobotModel;

namespace SW2URDF.URDFExport
{
    public partial class ExportHelper
    {
        // Configuration/reference compatibility only: this does not build a URDF Robot.
        public void EnsureNativeReferences(LinkNode root)
        {
            Action<LinkNode> visit=null;
            visit=node=>{
                if(node.Link.SWComponents.Count>0)node.Link.SWMainComponent=node.Link.SWComponents[0];
                var j=node.Link.Joint;
                if(node.Parent==null&&j.CoordinateSystemName=="Automatically Generate") {
                    CreateBaseRefOrigin(true);j.CoordinateSystemName="Origin_global";
                } else if(node.Parent!=null&&(j.CoordinateSystemName=="Automatically Generate"||j.AxisName=="Automatically Generate"||j.Type=="Automatically Detect")) {
                    ExportErrorWhy="";
                    CreateJoint(((LinkNode)node.Parent).Link,node.Link);
                    if(!string.IsNullOrWhiteSpace(ExportErrorWhy))throw new InvalidOperationException(ExportErrorWhy);
                }
                foreach(LinkNode child in node.Nodes)visit(child);
            };
            visit(root);
        }
        public Dictionary<string,MeshSource> PlanConfiguredNativeMeshes(LinkNode root,string directory)
        {
            var result=new Dictionary<string,MeshSource>();Action<LinkNode> visit=null;
            visit=node=>{if(!node.Link.isFixedFrame&&node.Link.SWComponents.Count>0)result.Add(node.Link.StableId,new MeshSource(node.Link.StableId+"/mesh",Path.Combine(directory,node.Link.StableId+".stl"),new Vector3d(1,1,1)));foreach(LinkNode child in node.Nodes)visit(child);};visit(root);return result;
        }
        public Dictionary<string,MeshSource> ExportConfiguredNativeMeshes(LinkNode root,string directory)
        {
            Directory.CreateDirectory(directory);
            var result=new Dictionary<string,MeshSource>();
            var hidden=CommonSwOperations.FindHiddenComponents(((AssemblyDoc)ActiveSWModel).GetComponents(false));
            SaveUserPreferences();
            try {
                SetSTLExportPreferences();ActiveSWModel.Extension.SelectAll();ActiveSWModel.HideComponent2();
                Action<LinkNode> visit=null;
                visit=node=>{
                    foreach(LinkNode child in node.Nodes)visit(child);
                    var config=node.Link;
                    if(config.isFixedFrame||config.SWComponents.Count==0)return;
                    var path=Path.Combine(directory,config.StableId+".stl");
                    SaveSTL(config,path);
                    result.Add(config.StableId,new MeshSource(config.StableId+"/mesh",path,new Vector3d(1,1,1)));
                };
                visit(root);return result;
            } finally {CommonSwOperations.ShowAllComponents(ActiveSWModel,hidden);ResetUserPreferences();}
        }
    }
}
