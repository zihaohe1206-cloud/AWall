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
// A native desktop child containing only a frozen bitmap. Alpha applies to the
// entire HWND, so it also works above WebView2's separate browser child windows.
public sealed class TransitionCover : Window {
 public IntPtr Handle,ParentHandle;readonly Forms.Screen monitor;
 public TransitionCover(Forms.Screen screen,BitmapSource frame){
  monitor=screen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;AllowsTransparency=true;Background=Brushes.Transparent;
  Width=screen.Bounds.Width;Height=screen.Bounds.Height;Content=new Image(){Source=frame,Stretch=Stretch.Fill};
  SourceInitialized+=delegate{Handle=new WindowInteropHelper(this).Handle;Attach();};Loaded+=delegate{Attach();};
 }
 public void Attach(){DesktopHost.Attach(Handle,monitor,false);ParentHandle=DesktopHost.Parent();SetAlpha(255);}
 public void Raise(){DesktopHost.Raise(Handle);}
 public void SetAlpha(byte alpha){Opacity=alpha/255.0;}
}
}
