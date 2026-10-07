using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SWSimTool.RobotModel;
namespace SWSimTool.Simulation {
    public interface IExportWorkspace:IDisposable {string Work {get;} string Staging {get;}}
    public interface IExportAssetPlan {
        MeshSource[] Sources {get;}
        PreparedMeshAsset[] Assets {get;}
        void VerifySources();
    }
    public interface IAssetStore {
        IExportAssetPlan Plan(SWSimTool.RobotModel.RobotModel model,string outputDirectory);
        string Hash(string path);
        bool Exists(string path);
        void WriteUtf8(string path,string contents);
    }
    public interface IPackageStore {
        IExportWorkspace CreateWorkspace(string parent);
        void Publish(string staging,string output);
    }
    public interface IMjcfWriter {string Generate(SWSimTool.RobotModel.RobotModel model,PreparedAssets assets,ExportContext context);}
    public sealed class ExportMetrics {
        public string ExportId {get;}
        public IReadOnlyDictionary<string,int> Counts {get;}
        public ExportMetrics(string id,IDictionary<string,int> counts){ExportId=id;Counts=new ReadOnlyDictionary<string,int>(new Dictionary<string,int>(counts));}
    }
}
