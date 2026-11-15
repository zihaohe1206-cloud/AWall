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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AWall {
public class Item {
 public string Title, Type, File, Preview;
 public bool Supported {get {return Type=="video"||Type=="scene";}}
 public override string ToString() { return (Type == "video" ? "▶ MP4  " : Type=="scene" ? "▧ PKG  " : "◇ WEB  ") + Title; }
}
public static class Native {
 static IntPtr desktopParent;
 public delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p,IntPtr a,string c,string n);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetLong(IntPtr h,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetLong(IntPtr h,int index,IntPtr value);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint timeout,out IntPtr result);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct WindowPos { public IntPtr Window,After; public int X,Y,Width,Height; public uint Flags; }
 public static IntPtr DesktopParent() {
  // Sending 0x52c for every staging window can disturb the desktop surface.
  // Reuse the host until Explorer actually destroys it.
  if(desktopParent!=IntPtr.Zero&&IsWindow(desktopParent))return desktopParent;
  var prog=FindWindow("Progman",null); if(prog==IntPtr.Zero) return IntPtr.Zero;
  IntPtr result; SendMessageTimeout(prog,0x52c,IntPtr.Zero,IntPtr.Zero,2,1500,out result);
  var child=FindWindowEx(prog,IntPtr.Zero,"WorkerW",null);
  if(child!=IntPtr.Zero) {desktopParent=child;return child;}
  IntPtr worker=IntPtr.Zero;
  EnumWindows(delegate(IntPtr h,IntPtr p) { if(FindWindowEx(h,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero) worker=FindWindowEx(IntPtr.Zero,h,"WorkerW",null); return worker==IntPtr.Zero; },IntPtr.Zero);
  desktopParent=worker;return worker;
 }
}
public class Player : Window {
 public IntPtr Handle, ParentHandle; public Forms.Screen Monitor;
 bool presented;
 public ScenePlayer Scene;
 public bool Ready {get {return Scene.Ready;}}
 public bool Failed {get {return Scene.Failed;}}
 public string LastError {get {return Scene.LastError;}}
 public Action<string> Report;
 public Player(Item item,Forms.Screen monitor) {
  Monitor=monitor; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; ShowInTaskbar=false;
  ShowActivated=false; Background=Brushes.Black; Width=monitor.Bounds.Width; Height=monitor.Bounds.Height;
  Scene=new ScenePlayer(item,monitor.DeviceName);Content=Scene.View;
  Scene.Report+=ForwardReport;
  // Parent and position the HWND before WPF first shows it on the desktop.
  SourceInitialized+=delegate { Handle=new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(Handle).AddHook(WindowMessage); Attach(); };
  // WPF can update bounds during Show(); restore desktop-relative bounds before loading.
  Loaded+=async delegate { Attach();await Scene.Initialize(Handle); };
  Closed+=delegate { Scene.Dispose(); };
 }
 void ForwardReport(string message){if(Report!=null)Report(message);}
 public void Pause(bool value){Scene.Pause(value);}
 public Task<string> Stats(){return Scene.Stats();}
 public async Task SeekNearEnd(){await Scene.SeekNearEnd();}
 IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  // ShowWindow may raise a newly shown HWND. Keep staging windows underneath
  // the old wallpaper even when WPF changes their visibility or position.
  if(message==0x0046&&!presented){var pos=(Native.WindowPos)Marshal.PtrToStructure(lParam,typeof(Native.WindowPos));pos.After=new IntPtr(1);pos.Flags&=~0x0004u;Marshal.StructureToPtr(pos,lParam,false);}
  return IntPtr.Zero;
 }
 public void Attach() {
  ParentHandle=Native.DesktopParent(); if(ParentHandle==IntPtr.Zero) throw new Exception("未找到 Windows 桌面承载窗口。");
  long style=Native.GetLong(Handle,-16).ToInt64(); Native.SetLong(Handle,-16,new IntPtr((style & ~0x80000000L) | 0x40000000L));
  Native.SetLong(Handle,-20,new IntPtr(Native.GetLong(Handle,-20).ToInt64() | 0x08000020L));
  Native.SetParent(Handle,ParentHandle);
  var b=Monitor.Bounds; Native.Rect p; Native.GetWindowRect(ParentHandle,out p);
  // Load underneath the current wallpaper; keep it visible so Chromium renders frames.
  Native.SetWindowPos(Handle,presented?IntPtr.Zero:new IntPtr(1),b.Left-p.Left,b.Top-p.Top,b.Width,b.Height,0x10);
 }
 public void Present(){presented=true;Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x13);}
 public async Task<string> Snapshot(string path) {
  return await Scene.Snapshot(path);
 }
}
// A native desktop child containing only a frozen bitmap. Alpha applies to the
// entire HWND, so it also works above WebView2's separate browser child windows.
public sealed class TransitionCover : Window {
 public IntPtr Handle,ParentHandle;readonly Forms.Screen monitor;
 public TransitionCover(Forms.Screen screen,BitmapSource frame){
  monitor=screen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;AllowsTransparency=true;Background=Brushes.Transparent;
  Width=screen.Bounds.Width;Height=screen.Bounds.Height;Content=new Image(){Source=frame,Stretch=Stretch.Fill};
  SourceInitialized+=delegate{Handle=new WindowInteropHelper(this).Handle;Attach();};Loaded+=delegate{Attach();};
 }
 public void Attach(){
  ParentHandle=Native.DesktopParent();if(ParentHandle==IntPtr.Zero)throw new Exception("未找到 Windows 桌面承载窗口。");
  Native.SetLong(Handle,-16,new IntPtr((Native.GetLong(Handle,-16).ToInt64()&~0x80000000L)|0x40000000L));
  Native.SetLong(Handle,-20,new IntPtr(Native.GetLong(Handle,-20).ToInt64()|0x08000020L));Native.SetParent(Handle,ParentHandle);
  Native.Rect parent;Native.GetWindowRect(ParentHandle,out parent);var b=monitor.Bounds;
  Native.SetWindowPos(Handle,IntPtr.Zero,b.Left-parent.Left,b.Top-parent.Top,b.Width,b.Height,0x10);SetAlpha(255);
 }
 public void Raise(){Native.SetWindowPos(Handle,IntPtr.Zero,0,0,0,0,0x13);}
 public void SetAlpha(byte alpha){Opacity=alpha/255.0;}
}
public static class StartupBackdrop {
 // Read the static system background; never capture application windows.
 public static BitmapSource Capture(Forms.Screen screen){
  string path="",style="10",tile="0",rgb="0 0 0";
  using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")){if(key!=null){path=Convert.ToString(key.GetValue("WallPaper",""));style=Convert.ToString(key.GetValue("WallpaperStyle","10"));tile=Convert.ToString(key.GetValue("TileWallpaper","0"));}}
  using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors")){if(key!=null)rgb=Convert.ToString(key.GetValue("Background","0 0 0"));}
  var parts=rgb.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);byte r=0,g=0,b=0;
  var color=parts.Length==3&&byte.TryParse(parts[0],out r)&&byte.TryParse(parts[1],out g)&&byte.TryParse(parts[2],out b)?Color.FromRgb(r,g,b):Colors.Black;
  BitmapImage image=null;
  try{if(File.Exists(path)){image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();}}catch{}
  var bounds=screen.Bounds;var visual=new DrawingVisual();
  using(var draw=visual.RenderOpen()){
   draw.DrawRectangle(new SolidColorBrush(color),null,new System.Windows.Rect(0,0,bounds.Width,bounds.Height));
   if(image!=null){
    var area=style=="22"?Forms.SystemInformation.VirtualScreen:bounds;
    double w=image.PixelWidth,h=image.PixelHeight,x=area.Left-bounds.Left,y=area.Top-bounds.Top;
    if(tile=="1"){
     var brush=new ImageBrush(image){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new System.Windows.Rect(0,0,w,h),Stretch=Stretch.Fill};
     draw.DrawRectangle(brush,null,new System.Windows.Rect(0,0,bounds.Width,bounds.Height));
    }else{
     if(style=="2"){w=area.Width;h=area.Height;}
     else if(style=="6"||style=="10"||style=="22"){double scale=style=="6"?Math.Min(area.Width/w,area.Height/h):Math.Max(area.Width/w,area.Height/h);w*=scale;h*=scale;}
     x+=(area.Width-w)/2;y+=(area.Height-h)/2;draw.DrawImage(image,new System.Windows.Rect(x,y,w,h));
    }
   }
  }
  var frame=new RenderTargetBitmap(bounds.Width,bounds.Height,96,96,PixelFormats.Pbgra32);frame.Render(visual);frame.Freeze();return frame;
 }
}
public class MainWindow : Window {
 ListBox library=new ListBox(); Image preview=new Image(); TextBlock status=new TextBlock(); TextBlock detail=new TextBlock();
 CheckBox pauseCovered=new CheckBox(){Content="全屏应用时暂停",IsChecked=true};
 List<Player> players=new List<Player>(); List<Item> items=new List<Item>();
 List<Player> previousPlayers=new List<Player>(); Item previousItem; bool transitioning; DispatcherTimer transitionTimer;
 List<TransitionCover> covers=new List<TransitionCover>();DispatcherTimer fadeTimer;int transitionVersion;bool fading,previousManualPause;
 Forms.NotifyIcon tray; bool exiting,manualPause,locked; string topology=String.Join(";",Forms.Screen.AllScreens.Select(x=>x.Bounds.ToString())); Item current; DispatcherTimer timer; DispatcherTimer pointerTimer; bool testMode;
 string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall");
 string root="E:\\Steam\\steamapps\\workshop\\content\\431960";
 Stopwatch transitionClock;double preparationMs;
 void TransitionLog(string phase){try{File.AppendAllText(Path.Combine(data,"transitions.log"),DateTime.Now.ToString("s")+" version="+transitionVersion+" phase="+phase+" elapsedMs="+(transitionClock==null?0:transitionClock.ElapsedMilliseconds)+Environment.NewLine);}catch{}}
 public MainWindow(bool test) {
  testMode=test; Directory.CreateDirectory(data); Title="AWall · 轻量动态壁纸"; Width=1050; Height=720; MinWidth=850; MinHeight=600;
  Background=new SolidColorBrush(Color.FromRgb(18,22,32)); Foreground=Brushes.White; FontFamily=new FontFamily("Microsoft YaHei UI");
  var outer=new DockPanel(){Margin=new Thickness(28)}; Content=outer;
  var header=new StackPanel(); DockPanel.SetDock(header,Dock.Top); outer.Children.Add(header);
  header.Children.Add(new TextBlock(){Text="AWall",FontSize=34,FontWeight=FontWeights.Bold});
  header.Children.Add(new TextBlock(){Text="动态桌面 · 静态锁屏",Foreground=Brushes.LightSteelBlue,Margin=new Thickness(0,4,0,20)});
  status.TextWrapping=TextWrapping.Wrap; status.Margin=new Thickness(0,18,0,0); status.Foreground=Brushes.LightSteelBlue; DockPanel.SetDock(status,Dock.Bottom); outer.Children.Add(status);
  var grid=new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(350)}); grid.ColumnDefinitions.Add(new ColumnDefinition()); outer.Children.Add(grid);
  library.Background=new SolidColorBrush(Color.FromRgb(28,34,47)); library.Foreground=Brushes.White; library.BorderThickness=new Thickness(0); library.Padding=new Thickness(10); grid.Children.Add(library);
  var right=new StackPanel(){Margin=new Thickness(24,0,0,0)}; Grid.SetColumn(right,1); grid.Children.Add(right);
  preview.Height=250; preview.Stretch=Stretch.Uniform; right.Children.Add(preview); detail.TextWrapping=TextWrapping.Wrap; detail.Margin=new Thickness(0,12,0,16); right.Children.Add(detail);
  var actions=new WrapPanel(); right.Children.Add(actions);
  AddButton(actions,"设为动态桌面",Apply); AddButton(actions,"同步锁屏快照",SyncLock); AddButton(actions,"暂停 / 继续",delegate { manualPause=!manualPause; Tick(); }); AddButton(actions,"停止壁纸",Stop);
  var manage=new WrapPanel(); right.Children.Add(manage); AddButton(manage,"导入 MP4 / PKG",Import); AddButton(manage,"重新扫描",Scan); AddButton(manage,"选择素材目录",ChooseFolder);
  pauseCovered.Margin=new Thickness(4,18,0,10); pauseCovered.Foreground=Brushes.White; right.Children.Add(pauseCovered);
  right.Children.Add(new TextBlock(){Text="MP4 硬件加速循环播放；PKG 场景引擎（30 FPS）。\n部分专用效果可能不兼容；关闭窗口后驻留托盘。",TextWrapping=TextWrapping.Wrap,Foreground=Brushes.LightSlateGray,Margin=new Thickness(4,6,0,0)});
  library.SelectionChanged+=delegate { var item=library.SelectedItem as Item; if(item==null)return; detail.Text=item.Title+"\n"+(item.Type=="video"?"视频 · WebView2 播放":item.Type=="scene"?"PKG 场景 · WebGL 兼容渲染（非官方引擎）":"网页 · 暂不支持")+"\n"+Path.GetFileName(item.File); preview.Source=null; try { if(System.IO.File.Exists(item.Preview)){var img=new BitmapImage(); img.BeginInit();img.CacheOption=BitmapCacheOption.OnLoad; img.UriSource=new Uri(item.Preview);img.EndInit();preview.Source=img;} }catch{} };
  tray=new Forms.NotifyIcon(){Text="AWall",Icon=System.Drawing.SystemIcons.Application,Visible=!test};
  var menu=new Forms.ContextMenuStrip(); menu.Items.Add("打开 AWall",null,delegate { Dispatcher.Invoke(new Action(delegate{Show();Activate();})); });
  menu.Items.Add("暂停 / 继续",null,delegate{manualPause=!manualPause;Tick();}); menu.Items.Add("退出",null,delegate{Exit();}); tray.ContextMenuStrip=menu;
  tray.DoubleClick+=delegate{Show();Activate();}; Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;Hide();}};
  SystemEvents.SessionSwitch+=SessionChanged; SystemEvents.DisplaySettingsChanged+=DisplayChanged;
  timer=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(2)}; timer.Tick+=delegate{Tick();};timer.Start();
  pointerTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(50)};pointerTimer.Tick+=delegate{foreach(var p in previousPlayers.Concat(players)){if(p.Scene!=null){var cursor=Forms.Cursor.Position;var b=p.Monitor.Bounds;if(b.Contains(cursor))p.Scene.Pointer((double)(cursor.X-b.Left)/b.Width,(double)(cursor.Y-b.Top)/b.Height);}}};pointerTimer.Start();
  Scan(); Loaded+=delegate { if(testMode){Hide();var a=Environment.GetCommandLineArgs();int at=Array.IndexOf(a,"--test-file");if(at>=0&&at+1<a.Length){string f=Path.GetFullPath(a[at+1]);items=new List<Item>{new Item(){Title=Path.GetFileName(f),Type=Path.GetExtension(f).ToLowerInvariant()==".pkg"?"scene":"video",File=f,Preview=""}};library.ItemsSource=items;library.SelectedIndex=0;}if(a.Contains("--test-transitions"))TestTransitions();else if(a.Contains("--test-library"))TestLibrary();else RunTest();} };
 }
 void AddButton(Panel p,string text,Action a) { var b=new Button(){Content=text,Padding=new Thickness(14,9,14,9),Margin=new Thickness(3),Background=new SolidColorBrush(Color.FromRgb(64,105,188)),Foreground=Brushes.White,BorderThickness=new Thickness(0)};b.Click+=delegate{try{a();}catch(Exception e){SetStatus(e.Message);}};p.Children.Add(b); }
 void SetStatus(string s){status.Text=s;System.IO.File.AppendAllText(Path.Combine(data,"AWall.log"),DateTime.Now.ToString("s")+" "+s+Environment.NewLine);}
 void Import(){var dialog=new Microsoft.Win32.OpenFileDialog(){Filter="壁纸文件|*.mp4;*.pkg|MP4 视频|*.mp4|PKG 场景|*.pkg",Multiselect=true};if(dialog.ShowDialog()==true){foreach(string file in dialog.FileNames){var item=items.FirstOrDefault(x=>String.Equals(x.File,file,StringComparison.OrdinalIgnoreCase));if(item==null){item=new Item(){Title=Path.GetFileNameWithoutExtension(file),Type=Path.GetExtension(file).Equals(".pkg",StringComparison.OrdinalIgnoreCase)?"scene":"video",File=file,Preview=Path.Combine(Path.GetDirectoryName(file),"preview.jpg")};items.Add(item);}library.ItemsSource=null;library.ItemsSource=items;library.SelectedItem=item;}SetStatus("已导入 "+dialog.FileNames.Length+" 个 MP4 / PKG 文件。");}}
 void ChooseFolder(){using(var d=new Forms.FolderBrowserDialog()){d.SelectedPath=root;if(d.ShowDialog()==Forms.DialogResult.OK){root=d.SelectedPath;Scan();}}}
 void Scan(){items.Clear(); if(Directory.Exists(root))foreach(var file in Directory.GetFiles(root,"project.json",SearchOption.AllDirectories)){try {var j=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(System.IO.File.ReadAllText(file));var dir=Path.GetDirectoryName(file);string type=j.ContainsKey("type")?Convert.ToString(j["type"]).ToLowerInvariant():"scene";string entry=j.ContainsKey("file")?Convert.ToString(j["file"]):"";var item=new Item(){Title=j.ContainsKey("title")?Convert.ToString(j["title"]):Path.GetFileName(dir),Type=type,File=Path.GetFullPath(Path.Combine(dir,entry)),Preview=Path.Combine(dir,j.ContainsKey("preview")?Convert.ToString(j["preview"]):"preview.jpg")};if(!item.File.StartsWith(dir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;if(type=="scene"&&!System.IO.File.Exists(item.File)&&System.IO.File.Exists(Path.Combine(dir,"scene.pkg")))item.File=Path.Combine(dir,"scene.pkg");items.Add(item);}catch{}}
  items=items.OrderBy(x=>x.Type=="video"?0:1).ThenBy(x=>x.Title).ToList();library.ItemsSource=null;library.ItemsSource=items;if(items.Count>0)library.SelectedIndex=0;
  SetStatus("已扫描 "+items.Count+" 个素材，其中 "+items.Count(x=>x.Type=="video")+" 个视频、"+items.Count(x=>x.Type=="scene")+" 个 PKG 场景。目录："+root);
 }
 async void Apply(){try{await ApplyAsync();}catch(Exception e){SetStatus(e.Message);}}
 async Task ApplyAsync(){var item=library.SelectedItem as Item;if(item==null)return;if(!item.Supported){SetStatus("当前优先支持 MP4 / PKG，网页素材暂不支持。");return;}if(!System.IO.File.Exists(item.File))throw new Exception("素材入口文件不存在。");
  if(!transitioning&&current!=null&&String.Equals(current.File,item.File,StringComparison.OrdinalIgnoreCase)&&current.Type==item.Type&&players.Count==Forms.Screen.AllScreens.Length&&players.All(p=>p.Ready&&!p.Failed&&Forms.Screen.AllScreens.Any(s=>s.DeviceName==p.Monitor.DeviceName&&s.Bounds==p.Monitor.Bounds))){SetStatus("当前壁纸已在播放。");return;}
  CancelTransition();
  transitionClock=Stopwatch.StartNew();preparationMs=0;TransitionLog("begin");
  int version=++transitionVersion;previousManualPause=manualPause;
  previousPlayers=players;previousItem=current;players=new List<Player>();current=item;manualPause=false;transitioning=true;
  var batch=players;var screens=Forms.Screen.AllScreens;bool coversReady=false;
  Action finish=delegate{if(!coversReady||version!=transitionVersion||players!=batch||!transitioning||fading||batch.Count!=screens.Length||!batch.All(x=>x.Ready))return;foreach(var ready in batch)ready.Present();foreach(var cover in covers)cover.Raise();TransitionLog("all-content-ready");StopTransitionTimer();BeginFade(batch,version);};
  transitionTimer=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(60)};
  transitionTimer.Tick+=delegate{if(transitioning&&players==batch){CancelTransition();SetStatus("壁纸加载超时，已保留原壁纸。");}};transitionTimer.Start();
  try{
   // Capture all monitors before showing any cover. Keep the old players alive
   // but frozen under the covers until the replacement has succeeded.
   foreach(var screen in screens){var p=new Player(item,screen);p.Report=delegate(string message){
    if(players!=batch)return;SetStatus(message);
    // Dispose/replace windows outside WebView2's message callback. A newer request
    // invalidates this batch, so a late ready/error cannot replace its wallpaper.
    Dispatcher.BeginInvoke(new Action(delegate{if(players!=batch||!transitioning)return;
     var failed=batch.FirstOrDefault(x=>x.Failed);
     if(failed!=null){string error=failed.LastError;CancelTransition();SetStatus("壁纸加载失败，已保留原壁纸："+error);return;}
     finish();
    }),DispatcherPriority.ContextIdle);
   };batch.Add(p);p.Show();}
   var frames=await Task.WhenAll(previousPlayers.Select(old=>old.Scene.CaptureFrame()).ToArray());if(version!=transitionVersion)return;
   for(int i=0;i<previousPlayers.Count;i++){var cover=new TransitionCover(previousPlayers[i].Monitor,frames[i]);covers.Add(cover);cover.Show();}
   if(previousPlayers.Count==0)foreach(var screen in screens){var cover=new TransitionCover(screen,StartupBackdrop.Capture(screen));covers.Add(cover);cover.Show();}
   if(covers.Count>0){await Dispatcher.InvokeAsync(delegate{},DispatcherPriority.Render);await Task.Delay(50);if(version!=transitionVersion)return;foreach(var old in previousPlayers)old.Pause(true);}
   preparationMs=transitionClock.Elapsed.TotalMilliseconds;TransitionLog("covers-ready");
   coversReady=true;finish();
   SetStatus((item.Type=="scene"?"正在加载 PKG：":"正在加载 MP4：")+item.Title+"。锁屏图片需点击“同步锁屏快照”。");}catch{if(version!=transitionVersion)return;CancelTransition();throw;}
 }
 void BeginFade(List<Player> batch,int version){
  fading=true;var elapsed=Stopwatch.StartNew();fadeTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(15)};
  fadeTimer.Tick+=delegate{
   if(version!=transitionVersion||players!=batch)return;
   try{
    if(batch.Any(x=>x.Failed)){CancelTransition();SetStatus("新壁纸播放失败，已恢复原壁纸。");return;}
    double progress=Math.Min(1,elapsed.Elapsed.TotalMilliseconds/350.0);
    foreach(var cover in covers)cover.SetAlpha((byte)Math.Round(255*(1-progress)));
    if(progress<1&&covers.Count>0)return;
    CloseCovers();transitioning=false;StopFade();var old=previousPlayers;previousPlayers=new List<Player>();previousItem=null;
    foreach(var retired in old)retired.Close();Tick();TransitionLog("complete");SetStatus("壁纸切换完成。");
   }catch(Exception e){CancelTransition();SetStatus("壁纸切换失败，已恢复原壁纸："+e.Message);}
  };fadeTimer.Start();
 }
 void StopFade(){if(fadeTimer!=null){fadeTimer.Stop();fadeTimer=null;}fading=false;}
 void CloseCovers(){var old=covers;covers=new List<TransitionCover>();foreach(var cover in old)cover.Close();}
 void StopTransitionTimer(){if(transitionTimer!=null){transitionTimer.Stop();transitionTimer=null;}}
 void CancelTransition(){if(!transitioning)return;TransitionLog("rollback");++transitionVersion;transitioning=false;StopTransitionTimer();StopFade();var pending=players;players=previousPlayers;current=previousItem;manualPause=previousManualPause;previousPlayers=new List<Player>();previousItem=null;foreach(var p in players)p.Present();foreach(var p in pending)p.Close();CloseCovers();Tick();}
 void Stop(){++transitionVersion;transitioning=false;StopTransitionTimer();StopFade();CloseCovers();var all=previousPlayers.Concat(players).ToArray();players=new List<Player>();previousPlayers=new List<Player>();previousItem=null;current=null;foreach(var p in all)p.Close();SetStatus("动态壁纸已停止。");}
 void Tick(){try{bool covered=false;IntPtr fg=Native.GetForegroundWindow();var cls=new StringBuilder(256);Native.GetClassName(fg,cls,256);Native.Rect r; if(pauseCovered.IsChecked==true&&Native.GetWindowRect(fg,out r)&&!Native.IsIconic(fg)&&cls.ToString()!="Progman"&&cls.ToString()!="WorkerW")foreach(var s in Forms.Screen.AllScreens){var b=s.Bounds;if(r.Left<=b.Left&&r.Top<=b.Top&&r.Right>=b.Right&&r.Bottom>=b.Bottom)covered=true;}
  foreach(var p in previousPlayers.Concat(players)){if(!Native.IsWindow(p.ParentHandle))p.Attach();if(p.Ready){p.Pause((transitioning&&covers.Count>0&&previousPlayers.Contains(p))||manualPause||locked||covered);}}
  foreach(var cover in covers){if(!Native.IsWindow(cover.ParentHandle))cover.Attach();cover.Raise();}
 }catch(Exception e){SetStatus("桌面恢复失败："+e.Message);}}
 void SessionChanged(object s,SessionSwitchEventArgs e){Dispatcher.BeginInvoke(new Action(delegate{locked=e.Reason==SessionSwitchReason.SessionLock;if(e.Reason==SessionSwitchReason.SessionUnlock)locked=false;Tick();}));}
 void DisplayChanged(object s,EventArgs e){Dispatcher.BeginInvoke(new Action(delegate{string next=String.Join(";",Forms.Screen.AllScreens.Select(x=>x.Bounds.ToString()));if(next!=topology){topology=next;if(current!=null){var item=current;library.SelectedItem=item;Apply();}}}));}
 async void SyncLock(){if(players.Count==0){SetStatus("请先加载 MP4 或 PKG，再同步锁屏快照。");return;}try{var p=players.FirstOrDefault(x=>x.Monitor.Primary)??players[0];string image=await p.Snapshot(Path.Combine(data,"lock-"+Guid.NewGuid().ToString("N")+".jpg"));SetStatus("正在保存锁屏快照…");var start=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Set-LockImage.ps1")+"\" -ImagePath \""+image+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};int code=await System.Threading.Tasks.Task.Run(delegate{using(var proc=Process.Start(start)){string output=proc.StandardOutput.ReadToEnd();string error=proc.StandardError.ReadToEnd();proc.WaitForExit();System.IO.File.WriteAllText(Path.Combine(data,"lock-result.txt"),output+error);return proc.ExitCode;}});SetStatus(code==0?"锁屏静态快照已同步。Win+L 后显示图片，不播放视频。":"锁屏设置失败，详情见 "+Path.Combine(data,"lock-result.txt"));}catch(Exception e){SetStatus(e.Message);}}
 void RunTest(){var check=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(8)};Apply();check.Tick+=async delegate{check.Stop();try{var p=players[0];await p.Snapshot(Path.Combine(data,"test-frame.jpg"));System.IO.File.WriteAllText(Path.Combine(data,"selftest.txt"),"items="+items.Count+"; videos="+items.Count(x=>x.Type=="video")+"; parent="+p.ParentHandle+"; media="+await p.Stats()+"; snapshot=ok");}catch(Exception e){System.IO.File.WriteAllText(Path.Combine(data,"selftest.txt"),e.ToString());}Exit();};check.Start();}
 async void TestLibrary(){
  timer.Stop();pointerTimer.Stop();pauseCovered.IsChecked=false;
  var results=new List<object>();string folder=Path.Combine(data,"compatibility");Directory.CreateDirectory(folder);
  var onlyArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--only="));string only=onlyArg==null?null:onlyArg.Substring(7);foreach(var item in items.Where(x=>x.Supported&&(only==null||only.Split(',').Contains(Path.GetFileName(Path.GetDirectoryName(x.File))))).ToArray()){
   library.SelectedItem=item;string id=Path.GetFileName(Path.GetDirectoryName(item.File));
   try{await ApplyAsync();var player=players.First();var started=DateTime.UtcNow;
    while(!player.Ready&&!player.Failed&&(DateTime.UtcNow-started).TotalSeconds<50)await Task.Delay(250);
    if(!player.Ready)throw new Exception(player.LastError);
    await Task.Delay(1500);if(player.Failed)throw new Exception(player.LastError);
    string frame=Path.Combine(folder,id+".jpg");await player.Snapshot(frame);
    int lo=255,hi=0;using(var bitmap=new System.Drawing.Bitmap(frame)){for(int y=0;y<bitmap.Height;y+=Math.Max(1,bitmap.Height/48))for(int x=0;x<bitmap.Width;x+=Math.Max(1,bitmap.Width/64)){var pixel=bitmap.GetPixel(x,y);lo=Math.Min(lo,Math.Min(pixel.R,Math.Min(pixel.G,pixel.B)));hi=Math.Max(hi,Math.Max(pixel.R,Math.Max(pixel.G,pixel.B)));}}if(hi-lo<8)throw new Exception("截图近似纯色，需人工排查");
    await Task.Delay(500);string second=Path.Combine(folder,id+"-next.jpg");await player.Snapshot(second);
    bool frameChanged;using(var sha=System.Security.Cryptography.SHA256.Create()){frameChanged=!sha.ComputeHash(System.IO.File.ReadAllBytes(frame)).SequenceEqual(sha.ComputeHash(System.IO.File.ReadAllBytes(second)));}
    string pauseStats="";{player.Pause(true);await Task.Delay(300);pauseStats=await player.Stats();if(!pauseStats.Contains("\"paused\":true"))throw new Exception("播放暂停未生效");player.Pause(false);await Task.Delay(300);}
    if(item.Type=="video"){
     player.Pause(true);await Task.Delay(250);
     var json=new JavaScriptSerializer();
     var before=json.Deserialize<Dictionary<string,object>>(await player.Stats());await Task.Delay(450);
     var after=json.Deserialize<Dictionary<string,object>>(await player.Stats());
     if(!Convert.ToBoolean(after["mediaPaused"])||Math.Abs(Convert.ToDouble(after["time"])-Convert.ToDouble(before["time"]))>0.1)throw new Exception("MP4 暂停后仍在播放");
     player.Pause(false);
     await player.SeekNearEnd();
     await Task.Delay(1200);
     var loop=json.Deserialize<Dictionary<string,object>>(await player.Stats());
     if(Convert.ToBoolean(loop["ended"])||Convert.ToDouble(loop["time"])>=Convert.ToDouble(loop["duration"])-0.2)throw new Exception("MP4 循环播放未生效");
    }
    string stats=await player.Stats();
    results.Add(new{id=id,title=item.Title,type=item.Type,success=true,warnings=player.Scene!=null?player.Scene.Warnings:0,frameChanged=frameChanged,pixelRange=hi-lo,pause=pauseStats,stats=stats});
   }catch(Exception ex){results.Add(new{id=id,title=item.Title,type=item.Type,success=false,error=ex.ToString()});}
   Stop();System.IO.File.WriteAllText(Path.Combine(folder,"results.json"),new JavaScriptSerializer().Serialize(results));await Task.Delay(300);
  }
  Exit();
 }
 async Task WaitForTransition(){var started=Stopwatch.StartNew();while(transitioning&&started.Elapsed.TotalSeconds<65)await Task.Delay(25);if(transitioning)throw new Exception("切换未结束");}
 async void TestTransitions(){
  var results=new List<string>();timer.Stop();pointerTimer.Stop();pauseCovered.IsChecked=false;
  string resultPath=Path.Combine(data,"transition-test.json");
  try{
   var video=items.First(x=>x.Type=="video");var scene=items.First(x=>x.Type=="scene"&&System.IO.File.Exists(x.File));
   library.SelectedItem=video;await ApplyAsync();await WaitForTransition();
   if(current!=video||players.Count==0||!players.All(x=>x.Ready)||covers.Count!=0)throw new Exception("首次播放失败");results.Add("first-play: pass");
   var original=players.ToArray();library.SelectedItem=scene;await ApplyAsync();
   if(covers.Count!=original.Length||previousPlayers.Count!=original.Length)throw new Exception("快照遮罩未创建");
   var oldCovers=covers.ToArray();await Task.Delay(100);
   if(transitioning){foreach(var old in original){string stats=await old.Stats();if(!stats.Contains("\"paused\":true"))throw new Exception("旧播放器未暂停");}}
   await WaitForTransition();if(current!=scene||!players.All(x=>x.Ready)||covers.Count!=0||previousPlayers.Count!=0||oldCovers.Any(x=>Native.IsWindow(x.Handle)))throw new Exception("场景切换或遮罩清理失败");results.Add("video-to-scene/freeze/fade/cleanup: pass");
   library.SelectedItem=video;await ApplyAsync();await WaitForTransition();if(current!=video||!players.All(x=>x.Ready))throw new Exception("视频切换失败");results.Add("scene-to-video: pass");
   original=players.ToArray();var broken=new Item(){Title="invalid transition test",Type="video",File=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","test-artifacts","invalid.mp4"),Preview=""};items.Add(broken);library.ItemsSource=null;library.ItemsSource=items;library.SelectedItem=broken;
   await ApplyAsync();await WaitForTransition();if(current!=video||!players.SequenceEqual(original)||covers.Count!=0||!players.All(x=>x.Ready))throw new Exception("失败后没有恢复旧播放器");results.Add("failed-load-rollback: pass");
   library.SelectedItem=scene;var stale=ApplyAsync();library.SelectedItem=video;var latest=ApplyAsync();await Task.WhenAll(stale,latest);await WaitForTransition();if(current!=video||covers.Count!=0||previousPlayers.Count!=0)throw new Exception("连续切换遗留状态");results.Add("rapid-switch: pass");
   library.SelectedItem=scene;var stopping=ApplyAsync();Stop();await stopping;await Task.Delay(250);if(players.Count!=0||covers.Count!=0||previousPlayers.Count!=0||transitioning)throw new Exception("停止后异步请求重新创建窗口");results.Add("stop-during-capture: pass");
   System.IO.File.WriteAllText(resultPath,new JavaScriptSerializer().Serialize(new{success=true,checks=results}));
  }catch(Exception e){System.IO.File.WriteAllText(resultPath,new JavaScriptSerializer().Serialize(new{success=false,checks=results,error=e.ToString()}));}
  Exit();
 }
 void Exit(){exiting=true;timer.Stop();pointerTimer.Stop();SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;Stop();tray.Dispose();Application.Current.Shutdown();}
 [STAThread]public static void Main(string[] args){Native.SetProcessDPIAware();bool owned;using(var mutex=new System.Threading.Mutex(true,"Local\\AWall.Desktop",out owned)){if(!owned){MessageBox.Show("AWall 已运行，请从托盘打开。");return;}var app=new Application(){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){System.IO.File.AppendAllText(Path.Combine(Path.GetTempPath(),"AWall-error.log"),e.Exception.ToString());MessageBox.Show(e.Exception.Message,"AWall");e.Handled=true;};app.Run(new MainWindow(args.Contains("--self-test")||args.Contains("--test-library")||args.Contains("--test-transitions")));}}
}
}

