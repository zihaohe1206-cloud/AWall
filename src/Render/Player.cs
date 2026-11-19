using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AWall {
public class Player : Window {
 public IntPtr Handle, ParentHandle; public Forms.Screen Monitor;
 // The wallpaper this player shows (null while a PKG standby waits for Assign).
 public Item Item;
 bool presented,closed,hostFailed;string hostError="";
 public ScenePlayer Scene;
 public bool Ready {get {return !hostFailed&&Scene.Ready;}}
 public bool Failed {get {return hostFailed||Scene.Failed;}}
 public string LastError {get {return hostFailed?hostError:Scene.LastError;}}
 public Action<string> Report;
 public Player(Item item,Forms.Screen monitor) {
  Monitor=monitor;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
  ShowActivated=false;Background=Brushes.Black;Width=monitor.Bounds.Width;Height=monitor.Bounds.Height;
  // A renderable staging HWND; attach only after the backend produces a frame.
  Left=-32000;Top=-32000;WindowStartupLocation=WindowStartupLocation.Manual;
  Item=item;Scene=new ScenePlayer(item,monitor.DeviceName);Content=Scene.UsesMpv?null:Scene.View;
  Scene.Report+=ForwardReport;
  SourceInitialized+=delegate {Handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(Handle).AddHook(WindowMessage);
   Native.SetLong(Handle,-20,new IntPtr(Native.GetLong(Handle,-20).ToInt64()|0x08000080L));};
  Loaded+=async delegate {try{await Scene.Initialize(Handle);}catch(Exception e){HostFailure(e.Message);}};
  Closed+=delegate {closed=true;Report=null;Scene.Dispose();};
 }
 public bool IsClosed {get {return closed;}}
 // Hand a pre-started standby PKG player its scene (see WallpaperService.TakeStandby).
 public void Assign(Item item){if(closed)throw new ObjectDisposedException("Player");Scene.Assign(item);Item=item;}
 void HostFailure(string message){if(closed)return;hostFailed=true;hostError=message;if(Report!=null)Report(message);}
 void ForwardReport(string message){if(!closed&&Report!=null)Report(message);}
 public void Pause(bool value){Scene.Pause(value);}
 public Task<string> Stats(){return Scene.Stats();}
 public async Task SeekNearEnd(){await Scene.SeekNearEnd();}
 IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message==0x0046&&!presented){var pos=(Native.WindowPos)Marshal.PtrToStructure(lParam,typeof(Native.WindowPos));
   pos.After=new IntPtr(1);pos.Flags&=~0x0004u;
   if(ParentHandle==IntPtr.Zero){pos.X=-32000;pos.Y=-32000;pos.Flags&=~0x0002u;}
   Marshal.StructureToPtr(pos,lParam,false);}
  return IntPtr.Zero;
 }
 public void Attach(){if(closed)throw new ObjectDisposedException("Player");DesktopHost.Attach(Handle,Monitor,!presented);ParentHandle=DesktopHost.Parent();}
 public void Present(){if(!Ready)throw new InvalidOperationException("播放器尚未就绪。");presented=true;Attach();DesktopHost.Raise(Handle);}
 public async Task<string> Snapshot(string path) {
  return await Scene.Snapshot(path);
 }
}
}
