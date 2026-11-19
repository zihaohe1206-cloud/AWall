using System;
using System.IO;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace AWall {
// The real Windows background through the official IDesktopWallpaper COM API (Windows 8+).
// This is the "static" presentation route: once set, Explorer / DWM draw the image and AWall
// renders nothing. Per-monitor IDs are matched to Forms.Screen by bounds.
public static class SystemWallpaper {
 // DESKTOP_WALLPAPER_POSITION
 public const int Center=0,Tile=1,Stretch=2,Fit=3,Fill=4,Span=5;

 [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}

 [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IDesktopWallpaper {
  void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,[MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
  [return:MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
  [return:MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
  uint GetMonitorDevicePathCount();
  NativeRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
  void SetBackgroundColor(uint color);
  uint GetBackgroundColor();
  void SetPosition(int position);
  int GetPosition();
  void SetSlideshow(IntPtr items);
  IntPtr GetSlideshow();
  void SetSlideshowOptions(int options,uint slideshowTick);
  void GetSlideshowOptions(out int options,out uint slideshowTick);
  void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID,int direction);
  int GetStatus();
  void Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
 }
 [ComImport,Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")] class DesktopWallpaperClass {}

 // screen == null sets every monitor. Position applies to all monitors (a Windows limitation).
 public static void Set(string image,Forms.Screen screen){Set(image,screen,Fill);}
 public static void Set(string image,Forms.Screen screen,int position){
  if(String.IsNullOrEmpty(image)||!File.Exists(image))throw new FileNotFoundException("图片不存在",image);
  var wallpaper=(IDesktopWallpaper)new DesktopWallpaperClass();
  try{
   wallpaper.SetPosition(position);
   if(screen==null){wallpaper.SetWallpaper(null,Path.GetFullPath(image));return;}
   string id=MonitorId(wallpaper,screen);if(id==null)throw new InvalidOperationException("找不到显示器："+screen.DeviceName);
   wallpaper.SetWallpaper(id,Path.GetFullPath(image));
  }finally{Marshal.ReleaseComObject(wallpaper);}
 }
 // Current image of one monitor (empty when none / unknown).
 public static string Get(Forms.Screen screen){
  var wallpaper=(IDesktopWallpaper)new DesktopWallpaperClass();
  try{string id=MonitorId(wallpaper,screen);return id==null?"":(wallpaper.GetWallpaper(id)??"");}
  catch{return "";}
  finally{Marshal.ReleaseComObject(wallpaper);}
 }
 static string MonitorId(IDesktopWallpaper wallpaper,Forms.Screen screen){
  if(screen==null)return null;
  uint count=wallpaper.GetMonitorDevicePathCount();var b=screen.Bounds;
  for(uint i=0;i<count;i++){
   string id;NativeRect r;
   try{id=wallpaper.GetMonitorDevicePathAt(i);if(String.IsNullOrEmpty(id))continue;r=wallpaper.GetMonitorRECT(id);}catch{continue;}// detached monitors throw
   if(r.Left==b.Left&&r.Top==b.Top&&r.Right==b.Right&&r.Bottom==b.Bottom)return id;
  }
  return null;
 }
}
}
