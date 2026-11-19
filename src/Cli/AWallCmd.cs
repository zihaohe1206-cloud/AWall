using System;

namespace AWall {
// AWallCmd.exe: console front end for the command pipe. Built separately (build.ps1) from this file,
// App\CommandPipe.cs, App\AppPaths.cs and Core\Commands\CommandBus.cs; it is a console program so
// PowerShell and cmd wait for it and print its output in order, unlike the GUI AWall.exe --cmd.
public static class AWallCmd {
 public static int Main(string[] args){
  return CommandLine.Run(args,Console.Out,Console.Error);
 }
}
}
