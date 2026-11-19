using System;
using System.IO;
using System.Runtime.InteropServices;

namespace AWall {
// Show / hide the system desktop icons (Explorer's SysListView32 under SHELLDLL_DefView).
// AWall never draws icons itself; it only hides Explorer's list window, which Explorer does not remember.
// Safety rules (docs/tasks/desktop.md "图标显隐"): exit, Explorer restart and the next start after a crash
// always bring the icons back. The flag file marks "AWall hid them" so a crashed run can be repaired.
// Idea from DesktopFencesPlus DesktopIconManager (MIT), rewritten.
public static class DesktopIcons {
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 static string FlagPath {get {return Path.Combine(AppPaths.Data,"icons-hidden.flag");}}
 public static event Action Changed;
 static IntPtr List(){try{return DesktopHost.IconList();}catch{return IntPtr.Zero;}}
 // True when the icon window exists and is visible (also true when it cannot be found: nothing is hidden).
 public static bool Visible {get {var list=List();return list==IntPtr.Zero||IsWindowVisible(list);}}
 // False when Explorer's icon window could not be found.
 public static bool SetVisible(bool visible){
  var list=List();if(list==IntPtr.Zero)return false;
  if(!visible){try{Directory.CreateDirectory(AppPaths.Data);File.WriteAllText(FlagPath,DateTime.Now.ToString("s"));}catch{}}
  ShowWindow(list,visible?5:0);
  if(visible)try{File.Delete(FlagPath);}catch{}
  var handler=Changed;if(handler!=null)handler();
  return true;
 }
 // Start-up: a previous run hid the icons and did not restore them (crash, kill).
 public static bool RecoverAfterCrash(){
  if(!File.Exists(FlagPath))return false;
  var list=List();if(list!=IntPtr.Zero)ShowWindow(list,5);
  try{File.Delete(FlagPath);}catch{}
  return true;
 }
 // Exit / stop / Explorer restart: icons always come back.
 // Only undoes AWall's own hiding: "查看 > 显示桌面图标" turned off in Explorer also hides the list window.
 public static void Restore(){if(File.Exists(FlagPath))SetVisible(true);}
 // A new Explorer shows its icons again by itself; only the marker needs clearing.
 public static void ShellRestarted(){try{File.Delete(FlagPath);}catch{}var handler=Changed;if(handler!=null)handler();}
 public static bool HiddenByAWall {get {return File.Exists(FlagPath);}}
}
}
