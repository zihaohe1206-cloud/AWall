using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AWall {
// Lock-screen image through Set-LockImage.ps1 (WinRT; copied next to AWall.exe by build.ps1).
public static class LockScreen {
 // Script output goes to resultPath. Returns the script's exit code (0 = success).
 public static Task<int> Apply(string image,string resultPath){
  var start=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+AppPaths.InBase("Set-LockImage.ps1")+"\" -ImagePath \""+image+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
  return Task.Run(delegate{using(var proc=Process.Start(start)){string output=proc.StandardOutput.ReadToEnd();string error=proc.StandardError.ReadToEnd();proc.WaitForExit();File.WriteAllText(resultPath,output+error);return proc.ExitCode;}});
 }
}
}
