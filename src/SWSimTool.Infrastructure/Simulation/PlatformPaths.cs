using System;

namespace SWSimTool.Simulation
{
    // Filesystem identity follows the host. Export names and hashes have separate policies.
    internal static class PlatformPaths
    {
        internal static StringComparison Comparison=>Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        internal static StringComparer Comparer=>Environment.OSVersion.Platform==PlatformID.Win32NT?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal;
    }
}
