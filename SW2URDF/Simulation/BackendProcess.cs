using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SW2URDF.Simulation
{
    internal static class BackendProcess
    {
        internal static string Quote(string value)
        {
            if(value==null||value.Contains("\""))throw new ArgumentException("A path cannot contain quotes.");
            return "\""+value+new string('\\',value.Length-value.TrimEnd('\\').Length)+"\"";
        }
        internal static int Run(string python,string script,string arguments,Action<string> report,string exportId)
        {
            if(!File.Exists(script))throw new FileNotFoundException("Python support script is missing",script);
            var info=new ProcessStartInfo(python){UseShellExecute=false,CreateNoWindow=true,Arguments=Quote(script)+" "+arguments,
                RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            info.EnvironmentVariables["SW2MUJOCO_EXPORT_ID"]=exportId??Guid.NewGuid().ToString("N");
            info.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
            info.EnvironmentVariables["PYTHONUNBUFFERED"]="1";
            var gate=new object();
            DataReceivedEventHandler receive=(sender,e)=>{if(e.Data!=null)lock(gate)report?.Invoke(e.Data);};
            using(var process=new Process{StartInfo=info}) {
                process.OutputDataReceived+=receive;process.ErrorDataReceived+=receive;
                if(!process.Start())throw new InvalidOperationException("Cannot start local Python");
                process.BeginOutputReadLine();process.BeginErrorReadLine();process.WaitForExit();return process.ExitCode;
            }
        }
    }
}
