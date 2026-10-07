using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;

namespace SWSimTool.Simulation
{
    internal static class BackendProcess
    {
        internal static string Quote(string value)
        {
            if(value==null||value.Contains("\""))throw new ArgumentException("A path cannot contain quotes.");
            return "\""+value+new string('\\',value.Length-value.TrimEnd('\\').Length)+"\"";
        }
        internal static int Run(string python,string script,string arguments,Action<string> report,string exportId)
            => Execute(python,script,arguments,report,exportId,CancellationToken.None,TimeSpan.FromMinutes(10)).ExitCode;
        internal static ToolResult Execute(string python,string script,string arguments,Action<string> report,string exportId,CancellationToken cancellation,TimeSpan timeout)
        {
            if(timeout!=Timeout.InfiniteTimeSpan&&timeout<=TimeSpan.Zero)throw new ArgumentOutOfRangeException(nameof(timeout));
            if(cancellation.IsCancellationRequested)return ToolResult.Failure(ToolFailure.Cancelled,"Tool cancelled before start");
            if(!File.Exists(script))return ToolResult.Failure(ToolFailure.Start,"Python support script is missing: "+script);
            var info=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,Arguments=Quote(script)+" "+arguments,
                RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            info.EnvironmentVariables["SWSIMTOOL_EXPORT_ID"]=exportId??Guid.NewGuid().ToString("N");
            info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
            info.EnvironmentVariables["PYTHONUNBUFFERED"]="1";
            var log=new StringBuilder();var gate=new object();
            // Bounded diagnostic tail. Reporting failures cannot strand a worker.
            DataReceivedEventHandler receive=(sender,e)=>{if(e.Data==null)return;lock(gate){log.AppendLine(e.Data);if(log.Length>65536)log.Remove(0,log.Length-65536);try{report?.Invoke(e.Data);}catch{}}};
            using(var process=new Process{StartInfo=info})using(var job=new OwnedProcessJob()) {
                process.OutputDataReceived+=receive;process.ErrorDataReceived+=receive;
                try {
                    if(!process.Start())return ToolResult.Failure(ToolFailure.Start,"Cannot start local Python");
                    // Fail closed if tree ownership cannot be established.
                    try{job.Assign(process);}catch(Exception error){process.Kill();process.WaitForExit();return ToolResult.Failure(ToolFailure.Start,error.Message);}
                    process.BeginOutputReadLine();process.BeginErrorReadLine();var watch=Stopwatch.StartNew();
                    while(!process.WaitForExit(50)) {
                        var stopped=cancellation.IsCancellationRequested?ToolFailure.Cancelled:
                            timeout!=Timeout.InfiniteTimeSpan&&watch.Elapsed>=timeout?ToolFailure.Timeout:ToolFailure.None;
                        if(stopped==ToolFailure.None)continue;
                        job.Terminate();process.WaitForExit();lock(gate)return ToolResult.Failure(stopped,log.ToString());
                    }
                    process.WaitForExit();lock(gate)return new ToolResult(process.ExitCode,process.ExitCode==0?ToolFailure.None:ToolFailure.Exit,log.ToString());
                }catch(Exception error){return ToolResult.Failure(ToolFailure.Start,error.Message);}
            }
        }
    }
    // Job includes Python's Blender/other descendants. Closing kills the owned tree.
    internal sealed class OwnedProcessJob:IDisposable
    {
        IntPtr handle;
        [StructLayout(LayoutKind.Sequential)]struct Basic {public long PerProcess,PerJob;public uint Flags;public UIntPtr Min,Max;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
        [StructLayout(LayoutKind.Sequential)]struct Io {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
        [StructLayout(LayoutKind.Sequential)]struct Extended {public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
        [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr attributes,string name);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int kind,ref Extended info,uint length);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateJobObject(IntPtr job,uint code);
        [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
        internal OwnedProcessJob(){handle=CreateJobObject(IntPtr.Zero,null);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception();var info=new Extended{Basic=new Basic{Flags=0x2000}};if(!SetInformationJobObject(handle,9,ref info,(uint)Marshal.SizeOf(info))){Dispose();throw new System.ComponentModel.Win32Exception();}}
        internal void Assign(Process process){if(!AssignProcessToJobObject(handle,process.Handle))throw new System.ComponentModel.Win32Exception();}
        internal void Terminate(){if(handle!=IntPtr.Zero&&!TerminateJobObject(handle,1))throw new System.ComponentModel.Win32Exception();}
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
}
