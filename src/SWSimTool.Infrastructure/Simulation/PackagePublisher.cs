using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace SWSimTool.Simulation
{
    public static class PackagePublisher
    {
        public static void Publish(string stagedDirectory,string output,Action<string,string> move=null)
        {
            output=Path.GetFullPath(output);var root=Path.GetDirectoryName(output);var parent=Path.GetDirectoryName(root);
            var staging=Path.GetFullPath(stagedDirectory);
            if(!new DirectoryInfo(root).Name.EndsWith("_mjcf",StringComparison.Ordinal)||Path.GetDirectoryName(staging)!=parent||staging==root)
                throw new InvalidDataException("Staging and package must be separate siblings");
            var allowed=ManagedFiles(staging,Path.GetFileName(output));
            EnsureOnlyManaged(staging,allowed);
            if(Directory.Exists(root))EnsureOnlyManaged(root,ManagedFiles(root,Path.GetFileName(output)));
            var backup=Path.Combine(parent,"."+Path.GetFileName(root)+".previous-"+Guid.NewGuid().ToString("N"));
            move=move??Directory.Move;
            bool existed=Directory.Exists(root);
            if(existed)move(root,backup);
            try{move(staging,root);}
            catch{if(existed)move(backup,root);throw;}
            if(existed){try{Directory.Delete(backup,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
        static HashSet<string> ManagedFiles(string directory,string filename)
        {
            var result=new HashSet<string>(PlatformPaths.Comparer);
            string xml=Path.Combine(directory,filename);
            if(!File.Exists(xml)) {
                if(Directory.EnumerateFileSystemEntries(directory).Any())throw new IOException("Existing output directory is not a managed MJCF package");
                return result;
            }
            result.Add(Path.GetFullPath(xml));
            foreach(var mesh in XDocument.Load(xml).Descendants("asset").Elements("mesh")) {
                var path=Path.GetFullPath(Path.Combine(directory,(string)mesh.Attribute("file")??""));
                if(!path.StartsWith(directory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,PlatformPaths.Comparison)||Path.GetExtension(path).ToLowerInvariant()!=".stl")
                    throw new IOException("Mesh escapes package");
                result.Add(path);
            }
            return result;
        }
        static void EnsureOnlyManaged(string directory,HashSet<string> managed)
        {
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Package directory must not be a link");
            foreach(var child in Directory.GetDirectories(directory,"*",SearchOption.AllDirectories))if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new IOException("Package contains a linked directory");
            foreach(var file in Directory.GetFiles(directory,"*",SearchOption.AllDirectories)) {
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||!managed.Contains(Path.GetFullPath(file)))throw new IOException("Refusing to replace unrelated package contents: "+file);
            }
            foreach(var file in managed)if(!File.Exists(file))throw new FileNotFoundException("Package mesh is missing",file);
        }
    }
}
