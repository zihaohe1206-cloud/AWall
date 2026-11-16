using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace AWall {
// Desktop lifecycle adapted from the observed Lively desktop protocol.
// Raised desktops deliberately use the research source's WorkerW-parent option:
// both WPF/WebView2 players and the existing layered covers remain siblings.
// Attaching each directly to Progman would interleave them with Shell DefView
// and require applying WS_EX_LAYERED to the WebView2 player's HWND.
public static class DesktopHost {
 const int Style=-16, ExtendedStyle=-20;
 const long Child=0x40000000L, Popup=0x80000000L;
 const long NoActivate=0x08000000L, Transparent=0x20L, NoRedirectionBitmap=0x00200000L;
 const uint NoMove=2, NoSize=1, NoActivatePosition=0x10, FrameChanged=0x20;
 static readonly IntPtr Bottom=new IntPtr(1);
 static IntPtr progman,worker,defView,defViewHost;
 static uint explorerPid;
 static bool raised;
 delegate bool EnumProc(IntPtr hwnd,IntPtr value);
 [StructLayout(LayoutKind.Sequential)] struct Point {public int X,Y;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,IntPtr value);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetParent(IntPtr hwnd,IntPtr parent);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] static extern IntPtr GetLong(IntPtr hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetLong(IntPtr hwnd,int index,IntPtr value);
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll",SetLastError=true)] static extern int MapWindowPoints(IntPtr from,IntPtr to,ref Point point,uint count);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint processId);
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam,uint flags,uint timeout,out IntPtr result);
 [DllImport("kernel32.dll")] static extern void SetLastError(uint error);

 static void Log(string message){
  try{
   string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall");
   Directory.CreateDirectory(directory);string path=Path.Combine(directory,"desktop-host.log");
   if(File.Exists(path)&&new FileInfo(path).Length>1024*1024)File.WriteAllText(path,"");
   File.AppendAllText(path,DateTime.Now.ToString("s")+" "+message+Environment.NewLine);
  }catch{} // Diagnostics must never interrupt desktop recovery.
 }
 static Exception Failure(string operation){
  int error=Marshal.GetLastWin32Error();Log(operation+" error="+error);
  return new Win32Exception(error,operation+" (Win32 "+error+")");
 }
 static bool SameExplorer(IntPtr hwnd){uint pid;return hwnd!=IntPtr.Zero&&IsWindow(hwnd)&&GetWindowThreadProcessId(hwnd,out pid)!=0&&pid==explorerPid;}
 public static void Invalidate(){progman=worker=defView=defViewHost=IntPtr.Zero;explorerPid=0;raised=false;Log("invalidate");}
 public static bool IsHealthy(IntPtr parent){
  return parent!=IntPtr.Zero&&parent==worker&&SameExplorer(progman)&&FindWindow("Progman",null)==progman&&SameExplorer(worker)&&SameExplorer(defView)&&SameExplorer(defViewHost)&&GetParent(defView)==defViewHost&&(!raised||GetParent(worker)==progman);
 }
 public static IntPtr Parent(){
  if(IsHealthy(worker)){EnsureWorkerWZOrder();return worker;}
  Invalidate();progman=FindWindow("Progman",null);
  if(progman==IntPtr.Zero)throw new InvalidOperationException("Windows 桌面尚未就绪：找不到 Progman。");
  GetWindowThreadProcessId(progman,out explorerPid);
  raised=(GetLong(progman,ExtendedStyle).ToInt64()&NoRedirectionBitmap)!=0;
  IntPtr result;SetLastError(0);
  if(SendMessageTimeout(progman,0x052c,new IntPtr(0xD),new IntPtr(1),2,1000,out result)==IntPtr.Zero)
   Log("Progman 0x052c(0xD,1) returned zero error="+Marshal.GetLastWin32Error());
  EnumWindows(delegate(IntPtr top,IntPtr unused){
   var view=FindWindowEx(top,IntPtr.Zero,"SHELLDLL_DefView",null);
   if(view!=IntPtr.Zero&&SameExplorer(top)){
    defView=view;defViewHost=top;
    if(!raised)worker=FindWindowEx(IntPtr.Zero,top,"WorkerW",null);
   }
   return true;
  },IntPtr.Zero);
  if(raised){
   defView=FindWindowEx(progman,IntPtr.Zero,"SHELLDLL_DefView",null);defViewHost=progman;
   worker=FindWindowEx(progman,IntPtr.Zero,"WorkerW",null);
  }
  // Never fall back to the icon-bearing Progman: a successful-looking attach
  // there can cover desktop icons. The caller can retry after Explorer settles.
  if(!IsHealthy(worker)){
   Log("discovery failed raised="+raised+" progman="+progman+" worker="+worker+" defview="+defView);
   throw new InvalidOperationException("Windows 桌面宿主尚未就绪：未找到有效的壁纸 WorkerW。");
  }
  EnsureWorkerWZOrder();
  Log("discovered raised="+raised+" workerWParent=true explorerPid="+explorerPid+" progman="+progman+" worker="+worker+" defview="+defView);
  return worker;
 }
 static void EnsureWorkerWZOrder(){
  // In raised mode WorkerW and DefView are siblings under Progman. Keep the
  // wallpaper subtree below the transparent shell icons, as in Lively's rule.
  if(raised&&GetWindow(worker,2)!=IntPtr.Zero&&!SetWindowPos(worker,Bottom,0,0,0,0,NoMove|NoSize|NoActivatePosition))throw Failure("桌面 WorkerW 层级设置失败");
 }
 static void SetStyle(IntPtr hwnd,int index,long value){
  SetLastError(0);var previous=SetLong(hwnd,index,new IntPtr(value));
  if(previous==IntPtr.Zero&&Marshal.GetLastWin32Error()!=0)throw Failure("壁纸窗口样式设置失败");
  if(GetLong(hwnd,index).ToInt64()!=value)throw new InvalidOperationException("壁纸窗口样式未生效。");
 }
 public static void Attach(IntPtr hwnd,Forms.Screen monitor,bool bottom){
  if(hwnd==IntPtr.Zero||!IsWindow(hwnd))throw new InvalidOperationException("壁纸窗口已销毁，无法挂载。");
  if(monitor==null)throw new ArgumentNullException("monitor");
  var parent=Parent();
  SetStyle(hwnd,Style,(GetLong(hwnd,Style).ToInt64()&~Popup)|Child);
  SetStyle(hwnd,ExtendedStyle,GetLong(hwnd,ExtendedStyle).ToInt64()|NoActivate|Transparent);
  if(GetParent(hwnd)!=parent){
   SetLastError(0);var previous=SetParent(hwnd,parent);
   if(previous==IntPtr.Zero&&Marshal.GetLastWin32Error()!=0)throw Failure("壁纸挂载到桌面失败");
   if(GetParent(hwnd)!=parent)throw new InvalidOperationException("壁纸桌面父窗口校验失败。");
  }
  var bounds=monitor.Bounds;var origin=new Point(){X=bounds.Left,Y=bounds.Top};
  SetLastError(0);int offset=MapWindowPoints(IntPtr.Zero,parent,ref origin,1);
  if(offset==0&&Marshal.GetLastWin32Error()!=0)throw Failure("显示器坐标映射失败");
  if(!SetWindowPos(hwnd,bottom?Bottom:IntPtr.Zero,origin.X,origin.Y,bounds.Width,bounds.Height,NoActivatePosition|FrameChanged))throw Failure("壁纸窗口定位失败");
  Log("attach hwnd="+hwnd+" parent="+parent+" screen="+monitor.DeviceName+" x="+origin.X+" y="+origin.Y+" width="+bounds.Width+" height="+bounds.Height+" bottom="+bottom);
 }
 public static void Raise(IntPtr hwnd){
  var parent=Parent();
  if(GetParent(hwnd)!=parent)throw new InvalidOperationException("桌面宿主已改变，需要重新挂载壁纸窗口。");
  if(!SetWindowPos(hwnd,IntPtr.Zero,0,0,0,0,NoMove|NoSize|NoActivatePosition))throw Failure("壁纸窗口层级设置失败");
 }
}
}
