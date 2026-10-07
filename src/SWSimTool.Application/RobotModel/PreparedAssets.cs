using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;



namespace SWSimTool.RobotModel
{
    public sealed class PreparedMeshAsset
    {
        public readonly string SourceId,Name,RelativePath;
        public PreparedMeshAsset(string sourceId,string name,string path)
        {
            if(string.IsNullOrWhiteSpace(sourceId)||string.IsNullOrWhiteSpace(name)||string.IsNullOrWhiteSpace(path)||Path.IsPathRooted(path)||path.Contains(":")||path.Split('/','\\').Any(p=>p==".."||p=="."))throw new InvalidDataException("Invalid prepared mesh path");
            SourceId=sourceId;Name=name;RelativePath=path.Replace('\\','/');
        }
    }
    public sealed class PreparedAssets
    {
        public ReadOnlyCollection<PreparedMeshAsset> Meshes { get; private set; }
        public PreparedAssets(IEnumerable<PreparedMeshAsset> meshes) { Meshes=Array.AsReadOnly(meshes.ToArray());RobotModelValidator.Unique(Meshes.Select(m=>m.SourceId),"prepared source");RobotModelValidator.Unique(Meshes.Select(m=>m.Name),"mesh name");RobotModelValidator.Unique(Meshes.Select(m=>m.RelativePath),"mesh path"); }
    }
    public sealed class ExportContext
    {
        public readonly string ModelName;
        public ExportContext(string name) { ModelName=name; }
    }
}
