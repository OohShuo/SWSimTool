using System;
using System.IO;
namespace SWSimTool.Simulation
{
    public static class ExportOutputPaths
    {
        public static string DefaultOutput(string urdf)=>Path.Combine(Path.GetDirectoryName(urdf),Path.GetFileNameWithoutExtension(urdf)+"_mjcf",Path.GetFileNameWithoutExtension(urdf)+".xml");
        public static string PackageOutput(string urdf,string output){var full=Path.GetFullPath(output);var directory=Path.GetDirectoryName(full);return Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)).EndsWith("_mjcf",StringComparison.Ordinal)?full:Path.Combine(directory,Path.GetFileNameWithoutExtension(urdf)+"_mjcf",Path.GetFileName(full));}
    }
}
