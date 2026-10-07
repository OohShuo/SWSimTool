using System.IO;
using System.Text;
using SWSimTool.RobotModel;
namespace SWSimTool.Simulation {
    internal sealed class NativeExportStorage:IAssetStore,IPackageStore {
        public IExportAssetPlan Plan(SWSimTool.RobotModel.RobotModel model,string outputDirectory)=>NativeAssetPlanner.Create(model,outputDirectory);
        public string Hash(string path)=>NativeAssetPlanner.Hash(path);
        public bool Exists(string path)=>File.Exists(path);
        public void WriteUtf8(string path,string contents)=>File.WriteAllText(path,contents,new UTF8Encoding(false));
        public IExportWorkspace CreateWorkspace(string parent)=>new NativeExportWorkspace(parent);
        public void Publish(string staging,string output)=>PackagePublisher.Publish(staging,output);
    }
    internal sealed class NativeMjcfWriter:IMjcfWriter {
        public string Generate(SWSimTool.RobotModel.RobotModel model,PreparedAssets assets,ExportContext context)=>MjcfExporter.Generate(model,assets,context);
    }
}
