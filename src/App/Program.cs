using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AWall {
// Process entry: single instance, test / autostart switches, application start.
public static class Program {
 // A second launch (desktop icon, Start menu) signals this event; the running MainWindow shows itself.
 public static System.Threading.EventWaitHandle ShowSignal;
 [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
 [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
 [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int handle);
 [STAThread]public static void Main(string[] args){
  // "AWall.exe --cmd <命令> key=value": forward to the running instance over the command pipe and print the result.
  int cmdAt=Array.IndexOf(args,"--cmd");
  if(cmdAt>=0){AttachParentConsole();Environment.ExitCode=CommandLine.Run(args.Skip(cmdAt+1).ToArray(),Console.Out,Console.Error);Console.Out.Flush();return;}
  Native.SetProcessDPIAware();bool test=args.Contains("--self-test")||args.Contains("--test-library")||args.Contains("--test-transitions");bool autostart=args.Contains("--autostart");
  // The show event is created before the mutex, so a second launch can always tell
  // whether the running instance listens for it (builds before this change did not).
  bool signalCreated;
  using(ShowSignal=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,"Local\\AWall.Desktop.Show",out signalCreated)){
   bool owned;using(var mutex=new System.Threading.Mutex(true,"Local\\AWall.Desktop",out owned)){
    if(!owned){
     if(test||autostart)return;
     if(signalCreated){MessageBox.Show("AWall 已在运行（旧版本），请从托盘打开，或退出后重新启动。","AWall");return;}
     AllowSetForegroundWindow(-1);ShowSignal.Set();return;
    }
    var app=new Application(){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){System.IO.File.AppendAllText(Path.Combine(Path.GetTempPath(),"AWall-error.log"),e.Exception.ToString());MessageBox.Show(e.Exception.Message,"AWall");e.Handled=true;};
    var window=new MainWindow(test,autostart);
    if(!test)StartCommandPipe(window);
    // Sign-in launch: tray only, the window opens from the tray icon.
    if(autostart&&!test){window.StartHidden();app.Run();}else app.Run(window);
   }
  }
 }
 // External callers (AWallCmd, scripts, agents) run the same command table as the tray and buttons.
 static void StartCommandPipe(MainWindow window){
  CommandPipeServer.Start(delegate(string name,CommandArgs args){
   string response=null;
   window.Dispatcher.Invoke(new Action(delegate{response=CommandPipe.Response(window.Commands.Execute(name,args));}),DispatcherPriority.Normal,System.Threading.CancellationToken.None,TimeSpan.FromSeconds(30));
   return response;
  });
 }
 // A GUI-subsystem exe has no console: use redirected handles as they are, otherwise attach to the caller's console.
 static void AttachParentConsole(){
  IntPtr output=GetStdHandle(-11);
  if(output==IntPtr.Zero||output==new IntPtr(-1))AttachConsole(-1);
 }
}
}
