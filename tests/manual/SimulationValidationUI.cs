using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SW2URDF.URDFExport;
using SW2URDF.Simulation;
public static class SimulationValidationUI {
 [STAThread] public static void Main(string[] args) {
  try {
   File.AppendAllText(Path.Combine(args[1],"ui-stages.txt"),"binding\n"); SldWorks sw; if(args[0]=="new") { sw=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); if(sw.GetDocumentCount()!=0) throw new Exception("Refusing server with existing documents"); sw.Visible=true; sw.UserControl=true; int openErrors=0,openWarnings=0; if(sw.OpenDoc6(Path.Combine(args[1],"isolated_fixture.SLDASM"),2,1,"",ref openErrors,ref openWarnings)==null) throw new Exception("Test fixture open failed: "+openErrors); File.WriteAllText(Path.Combine(args[1],"solidworks-process.txt"),sw.GetProcessID().ToString()); } else sw=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
   if(args[0]!="new" && sw.GetProcessID()!=int.Parse(args[0])) throw new Exception("Different SolidWorks process; refusing operation");
   var model=(ModelDoc2)sw.ActiveDoc;
   if(!string.Equals(model.GetPathName(),Path.Combine(args[1],"isolated_fixture.SLDASM"),StringComparison.OrdinalIgnoreCase)) throw new Exception("Different model; refusing operation");
   if(args.Length>2 && args[2]=="--propertymanager") { var pm=new ExportPropertyManager(sw); if(!pm.LoadConfigTree()) throw new Exception("PM tree load failed"); pm.Show(); Application.Run(new Form {Text="Isolated URDF PropertyManager test host",Width=360,Height=100}); GC.KeepAlive(pm); return; } File.AppendAllText(Path.Combine(args[1],"ui-stages.txt"),"helper\n"); var helper=new ExportHelper(sw);
   File.AppendAllText(Path.Combine(args[1],"ui-stages.txt"),"service\n"); var service=helper.GetSimulation();
   if(args.Length>2 && args[2]=="--export-form") {
    bool stop; var tree=ConfigurationSerialization.LoadBaseNodeFromModel(model,out stop);
    CommonSwOperations.LoadSWComponents(model,tree,new List<string>());
    if(stop || !helper.CreateRobotFromTreeView(tree)) throw new Exception("Tree reload failed");
    Application.EnableVisualStyles(); Application.Run(new SW2URDF.UI.AssemblyExportForm(sw,tree,helper)); return;
   }
   File.AppendAllText(Path.Combine(args[1],"ui-stages.txt"),"form\n"); Application.EnableVisualStyles();
   using(var form=new SW2URDF.UI.SimulationConfigForm(service,"arm",new[]{"hinge"})) {
    File.AppendAllText(Path.Combine(args[1],"ui-stages.txt"),"show\n"); if(form.ShowDialog()!=DialogResult.OK) return;
   }
   // Reopen to verify persistence through the same production UI.
   using(var form=new SW2URDF.UI.SimulationConfigForm(new SW2URDF.Simulation.AttachmentService(helper),"arm",new[]{"hinge"})) form.ShowDialog();
   bool abort;
   var node=ConfigurationSerialization.LoadBaseNodeFromModel(model,out abort);
   CommonSwOperations.LoadSWComponents(model,node,new List<string>());
   if(abort || !helper.CreateRobotFromTreeView(node)) throw new Exception("Tree reload failed");
   // Explicit test hinge axis; the automatically generated axis defaults to fixed.
   var joint=helper.URDFRobot.BaseLink.Children[0].Joint;
   joint.Type="continuous"; joint.Axis.X=0; joint.Axis.Y=0; joint.Axis.Z=1;
   helper.PackageName="cad_robot"; helper.SavePath=args[1]; helper.ShowExportLocation=false;
   helper.ExportRobot();
   int errors=0,warnings=0; model.Extension.SaveAs(model.GetPathName(),0,1,null,ref errors,ref warnings);
   PythonBackend.Launch(service.Project.python,helper.LastURDFPath,helper.LastSimulationPath,true);
   Application.Run(new Form {Text="SW2URDF validation backend host",Width=360,Height=100});
  } catch(Exception error) { File.WriteAllText(Path.Combine(args[1],"ui-error.txt"),error.ToString()); MessageBox.Show(error.ToString(),"Validation error"); }
 }
}
