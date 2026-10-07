using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using SW2URDF.RobotModel;

namespace SW2URDF.Simulation
{
    internal sealed class NativeAssetPlan
    {
        internal readonly MeshSource[] Sources;
        internal readonly PreparedMeshAsset[] Assets;
        readonly IDictionary<string,string> hashes;
        internal NativeAssetPlan(MeshSource[] sources,PreparedMeshAsset[] assets,IDictionary<string,string> sourceHashes){Sources=sources;Assets=assets;hashes=sourceHashes;}
        internal void VerifySources(){foreach(var item in hashes)if(NativeAssetPlanner.Hash(item.Key)!=item.Value)throw new IOException("Source mesh changed during export: "+item.Key);}
    }
    internal static class NativeAssetPlanner
    {
        internal static string Hash(string path){using(var algorithm=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
        internal static NativeAssetPlan Create(SW2URDF.RobotModel.RobotModel model,string outputDirectory)
        {
            var meshes=model.Core.Links.SelectMany(l=>l.Geometries).Where(g=>g.Mesh!=null).Select(g=>g.Mesh).GroupBy(m=>m.Id).Select(g=>g.First()).ToArray();
            foreach(var mesh in meshes)if(Path.GetFullPath(mesh.SourcePath).StartsWith(outputDirectory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("Output package must not contain original source meshes");
            var hashes=meshes.GroupBy(m=>m.SourcePath,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>Hash(g.Key),StringComparer.OrdinalIgnoreCase);
            var assets=meshes.Select((m,i)=>new PreparedMeshAsset(m.Id,"mesh_"+i.ToString("D4"),"meshes/mesh_"+i.ToString("D4")+".stl")).ToArray();
            return new NativeAssetPlan(meshes,assets,hashes);
        }
    }
    internal sealed class NativeExportWorkspace:IDisposable
    {
        internal readonly string Work,Staging;
        internal NativeExportWorkspace(string parent)
        {
            Directory.CreateDirectory(parent);
            Work=Path.Combine(parent,".sw2mujoco-native-"+Guid.NewGuid().ToString("N"));
            Staging=Path.Combine(parent,".sw2mujoco-stage-"+Guid.NewGuid().ToString("N"));
            try{Directory.CreateDirectory(Work);Directory.CreateDirectory(Staging);}catch{Dispose();throw;}
        }
        public void Dispose(){foreach(var path in new[]{Work,Staging})if(Directory.Exists(path))Directory.Delete(path,true);}
    }
}
