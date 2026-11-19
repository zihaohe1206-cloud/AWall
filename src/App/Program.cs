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
 [STAThread]public static void Main(string[] args){
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
    // Sign-in launch: tray only, the window opens from the tray icon.
    if(autostart&&!test){window.StartHidden();app.Run();}else app.Run(window);
   }
  }
 }
}
}
