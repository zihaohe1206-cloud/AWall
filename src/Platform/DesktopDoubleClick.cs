using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace AWall {
// Double-click on an empty spot of the desktop, seen through a low-level mouse hook.
// The hook lives on its own thread with its own message loop, so a busy UI thread can never delay the
// mouse; the callback only times clicks (DoubleClickDetector). Deciding whether both clicks hit empty
// desktop (WindowFromPoint + UI Automation for Explorer's icon list) runs on a worker thread.
// Idea from DesktopFencesPlus DesktopMouseHook and DeskFrame HandleGlobalDoubleClick / IsDesktopIconHit (MIT), rewritten.
public sealed class DesktopDoubleClick : IDisposable {
 [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] struct HookData {public NativePoint Point;public uint MouseData,Flags,Time;public IntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)] struct Message {public IntPtr Window;public uint Id;public IntPtr WParam,LParam;public uint Time;public NativePoint Point;}
 delegate IntPtr HookProc(int code,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc proc,IntPtr module,uint thread);
 [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")] static extern int GetMessage(out Message message,IntPtr hwnd,uint min,uint max);
 [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
 [DllImport("user32.dll")] static extern uint GetDoubleClickTime();
 [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

 public event Action<System.Drawing.Point> DoubleClicked; // raised on a worker thread
 readonly DoubleClickDetector detector=new DoubleClickDetector((int)GetDoubleClickTime(),Math.Max(4,GetSystemMetrics(36)),Math.Max(4,GetSystemMetrics(37)));
 HookProc proc;IntPtr hook;Thread thread;uint threadId;int busy;
 public bool Running {get {return hook!=IntPtr.Zero;}}
 public int Clicks {get;private set;}

 public void Start(){
  if(thread!=null)return;
  var ready=new ManualResetEvent(false);string error=null;
  thread=new Thread(delegate(){
   threadId=GetCurrentThreadId();proc=Callback;
   hook=SetWindowsHookEx(14,proc,GetModuleHandle(null),0);// WH_MOUSE_LL
   if(hook==IntPtr.Zero)error="SetWindowsHookEx 失败（"+Marshal.GetLastWin32Error()+"）";
   ready.Set();if(hook==IntPtr.Zero)return;
   Message m;while(GetMessage(out m,IntPtr.Zero,0,0)>0){}
   UnhookWindowsHookEx(hook);hook=IntPtr.Zero;
  }){IsBackground=true,Name="AWall desktop mouse hook"};
  thread.SetApartmentState(ApartmentState.STA);thread.Start();ready.WaitOne(3000);
  if(error!=null){thread=null;throw new InvalidOperationException(error);}
 }
 IntPtr Callback(int code,IntPtr wParam,IntPtr lParam){
  if(code>=0&&wParam.ToInt32()==0x0201){// WM_LBUTTONDOWN
   var data=(HookData)Marshal.PtrToStructure(lParam,typeof(HookData));Clicks++;
   if(detector.Click(data.Point.X,data.Point.Y,data.Time)){
    var p=new System.Drawing.Point(data.Point.X,data.Point.Y);
    if(Interlocked.Exchange(ref busy,1)==0)ThreadPool.QueueUserWorkItem(delegate{
     try{if(DesktopHit.IsEmptyDesktop(p)){var h=DoubleClicked;if(h!=null)h(p);}}catch{}finally{Interlocked.Exchange(ref busy,0);}
    });
   }
  }
  return CallNextHookEx(hook,code,wParam,lParam);
 }
 public void Dispose(){
  if(thread==null)return;
  PostThreadMessage(threadId,0x0012,IntPtr.Zero,IntPtr.Zero);// WM_QUIT
  thread.Join(2000);thread=null;
 }
}

// Pure timing rule, testable: two button-downs within the system double-click time and rectangle.
// A third click does not start another double-click with the second one.
public sealed class DoubleClickDetector {
 readonly int maxMs,dx,dy;int lastX,lastY;uint lastTime;bool armed;
 public DoubleClickDetector(int maxMs,int dx,int dy){this.maxMs=maxMs;this.dx=dx;this.dy=dy;}
 public bool Click(int x,int y,uint time){
  bool hit=armed&&unchecked(time-lastTime)<=(uint)maxMs&&Math.Abs(x-lastX)<=dx/2+1&&Math.Abs(y-lastY)<=dy/2+1;
  if(hit){armed=false;return true;}
  armed=true;lastX=x;lastY=y;lastTime=time;return false;
 }
}

// "Empty desktop" = Explorer's desktop windows where no icon is, or AWall's own wallpaper windows.
public static class DesktopHit {
 [StructLayout(LayoutKind.Sequential)] struct NativePoint {public int X,Y;}
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int size);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 static string ClassOf(IntPtr h){var s=new StringBuilder(64);GetClassName(h,s,64);return s.ToString();}
 public static bool IsEmptyDesktop(System.Drawing.Point p){
  var hwnd=WindowFromPoint(new NativePoint(){X=p.X,Y=p.Y});if(hwnd==IntPtr.Zero)return false;
  uint pid,shell;GetWindowThreadProcessId(hwnd,out pid);GetWindowThreadProcessId(FindWindow("Progman",null),out shell);
  string cls=ClassOf(hwnd);
  if(pid==(uint)Process.GetCurrentProcess().Id){var parent=GetParent(hwnd);return parent!=IntPtr.Zero&&ClassOf(parent)=="WorkerW";}// a wallpaper player, not a fence
  if(pid!=shell)return false;
  if(cls=="SHELLDLL_DefView"||cls=="WorkerW"||cls=="Progman")return true;
  if(cls!="SysListView32")return false;
  // On the icon list: an icon (or its rename box) is a ListItem / Edit; the empty list is the List itself.
  var element=AutomationElement.FromPoint(new System.Windows.Point(p.X,p.Y));
  if(element==null)return false;
  var type=element.Current.ControlType;
  return type!=ControlType.ListItem&&type!=ControlType.Edit&&type!=ControlType.Image&&type!=ControlType.Text;
 }
}
}
