using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using System;
using System.IO;
namespace SW2URDF.Simulation {
 public sealed class ProjectExport : IDisposable {
  readonly string temporary;
  public string Urdf{get;private set;} public string Sidecar{get;private set;}
  public ProjectExport(SldWorks app,ModelDoc2 expected,string configuration){
   if(!ReferenceEquals(app.ActiveDoc,expected)||expected.ConfigurationManager.ActiveConfiguration.Name!=configuration)throw new InvalidOperationException("当前装配或 Configuration 已切换，请重新打开工程导出。");
   temporary=Path.Combine(Path.GetTempPath(),"SW2MuJoCo-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temporary);
   try{
    bool error;var tree=ConfigurationSerialization.LoadBaseNodeFromModel(expected,out error);
    if(error||tree==null)throw new InvalidOperationException("请先配置并保存 URDF 树。");
    CommonSwOperations.LoadSWComponents(expected,tree,new System.Collections.Generic.List<string>());
    var helper=new ExportHelper(app){SavePath=temporary,PackageName="robot",ShowExportLocation=false,ExportSimulationInformation=true};
    helper.GetSimulation();if(!helper.CreateRobotFromTreeView(tree))throw new InvalidOperationException("URDF 构建失败，请检查 link / joint 配置。");
    helper.ExportRobot();Urdf=helper.LastURDFPath;Sidecar=helper.LastSimulationPath;
    if(Urdf==null||Sidecar==null)throw new IOException("临时 URDF 或附加配置导出失败。");
   }catch{Dispose();throw;}
  }
  public void Dispose(){if(Directory.Exists(temporary)&&Path.GetDirectoryName(Path.GetFullPath(temporary))==Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)&&Path.GetFileName(temporary).StartsWith("SW2MuJoCo-",StringComparison.Ordinal))Directory.Delete(temporary,true);}
 }
}
