using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AWall {
// One desktop block in the AboveIcons layer: a borderless WPF window parented to SHELLDLL_DefView
// (DesktopHost.AttachAboveIcons). Content is native WPF (fences) or a WebView2 page (web blocks).
// Reveal / Unreveal implement "揭开": the window temporarily becomes a topmost popup and then goes back.
public sealed class BlockWindow : Window {
 public IntPtr Handle {get;private set;}
 public System.Drawing.Rectangle Bounds {get;private set;}
 public bool Revealed {get;private set;}
 public WebView2 View {get;private set;}
 bool closed;
 public bool IsClosed {get {return closed;}}
 public BlockWindow(UIElement content){
  WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;
  Left=-32000;Top=-32000;Width=200;Height=100;WindowStartupLocation=WindowStartupLocation.Manual;Content=content;
  SourceInitialized+=delegate{Handle=new WindowInteropHelper(this).Handle;
   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW: never steals focus, never in Alt+Tab.
   Native.SetLong(Handle,-20,new IntPtr(Native.GetLong(Handle,-20).ToInt64()|0x08000080L));};
  Closed+=delegate{closed=true;if(View!=null)try{View.Dispose();}catch{}};
 }
 // A WebView2 block. transparent: the page's transparent pixels show the window behind (see the experiment in --test-desktop).
 public static BlockWindow Web(bool transparent){
  var view=new WebView2();
  view.DefaultBackgroundColor=transparent?System.Drawing.Color.Transparent:System.Drawing.Color.Black;
  var w=new BlockWindow(view);w.View=view;w.Background=transparent?Brushes.Transparent:Brushes.Black;
  return w;
 }
 // Creates the handle offscreen, then loads html. Completes when the page finished loading.
 public async Task LoadAsync(string html,string profile){
  if(View==null)throw new InvalidOperationException("不是网页块。");
  EnsureShown();
  Directory.CreateDirectory(profile);
  var env=await CoreWebView2Environment.CreateAsync(null,profile,new CoreWebView2EnvironmentOptions("--disable-features=CalculateNativeWinOcclusion"));
  await View.EnsureCoreWebView2Async(env);
  var done=new TaskCompletionSource<bool>();
  EventHandler<CoreWebView2NavigationCompletedEventArgs> handler=null;
  handler=delegate(object s,CoreWebView2NavigationCompletedEventArgs e){View.CoreWebView2.NavigationCompleted-=handler;done.TrySetResult(e.IsSuccess);};
  View.CoreWebView2.NavigationCompleted+=handler;
  View.NavigateToString(html);
  if(await Task.WhenAny(done.Task,Task.Delay(15000))!=done.Task||!done.Task.Result)throw new InvalidOperationException("块页面加载失败。");
 }
 void EnsureShown(){if(Handle==IntPtr.Zero)Show();}
 // bounds in screen pixels.
 public void Mount(System.Drawing.Rectangle bounds){
  if(closed)throw new ObjectDisposedException("BlockWindow");
  EnsureShown();Bounds=bounds;DesktopHost.AttachAboveIcons(Handle,bounds);Revealed=false;
 }
 public void Reveal(){if(closed)throw new ObjectDisposedException("BlockWindow");DesktopHost.Reveal(Handle,Bounds);Revealed=true;}
 public void Unreveal(){Mount(Bounds);}
 // After an Explorer restart the old DefView is gone: mount again under the new one.
 public bool Healthy {get {return !closed&&(Revealed||DesktopHost.IsAboveIcons(Handle));}}
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint key,byte alpha,uint flags);
 // Experiment B: colour-key transparency on a child window (needs the Windows 8+ manifest entry, which AWall has).
 public bool ColorKey(Color key,out int error){
  Native.SetLong(Handle,-20,new IntPtr(Native.GetLong(Handle,-20).ToInt64()|0x00080000L));
  bool ok=SetLayeredWindowAttributes(Handle,(uint)(key.R|(key.G<<8)|(key.B<<16)),255,1);
  error=ok?0:Marshal.GetLastWin32Error();return ok;
 }
}
}
