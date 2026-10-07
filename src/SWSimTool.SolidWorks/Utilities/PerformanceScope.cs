using System;
using System.Diagnostics;
namespace SWSimTool.Utilities {
 internal sealed class PerformanceScope:IDisposable {
  readonly string stage;readonly Stopwatch watch;
  internal PerformanceScope(string stage){this.stage=stage;if(Environment.GetEnvironmentVariable("SWSIMTOOL_PROFILE")=="1")watch=Stopwatch.StartNew();}
  public void Dispose(){if(watch!=null){watch.Stop();Trace.WriteLine("SWSimTool PERF "+stage+": "+watch.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" ms");}}
 }
}
