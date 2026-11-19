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
// Main window: fields, construction, layout of the window, now-playing and detail views.
public partial class MainWindow : Window {
 ListBox library=new ListBox(); TextBlock status=new TextBlock(); TextBox search=new TextBox();
 const double PreviewAspect=76.0/44.0; ImageBrush previewBrush=new ImageBrush(){Stretch=Stretch.UniformToFill}; Border previewImage=new Border(); TextBlock previewHint=new TextBlock();
 TextBlock detailTitle=new TextBlock(),detailMeta=new TextBlock(),detailFile=new TextBlock(),badgeText=new TextBlock(),nowText=new TextBlock(),countText=new TextBlock(); Border badge=new Border();
 System.Windows.Shapes.Ellipse nowDot=new System.Windows.Shapes.Ellipse(); TextBlock pauseGlyph,pauseLabel; Forms.ToolStripLabel trayNow; Forms.ToolStripMenuItem trayPause,trayStop; Forms.ContextMenuStrip trayMenu; string lastNow;
 string themeMode="system"; bool darkNow; IntPtr windowHandle; TextBlock themeGlyph; Button themeButton;
 Button propsButton,applyButton,stopButton; PropertiesWindow propsWindow;
 CheckBox pauseCovered=new CheckBox(){Content="全屏应用时暂停",IsChecked=true};
 List<Item> items=new List<Item>();
 Forms.NotifyIcon tray; bool exiting; bool testMode;
 string data=AppPaths.Data;
 // Wallpaper root: <project>\library\wallpapers; shared assets stay in library\we-assets.
 string root=AppPaths.DefaultWallpaperRoot;
 // Target of "设为动态桌面": null = every display, otherwise a Screen.DeviceName.
 string applyTarget; WrapPanel targetPanel; TextBlock applyLabel;
 CheckBox autostartSwitch=new CheckBox(){Content="开机自动启动"},rotationSwitch=new CheckBox(){Content="轮播"};
 Button rotationItemButton,rotationIntervalButton,rotationOrderButton,videoBackendButton; TextBlock settingsHint; Forms.ToolStripMenuItem trayNext;
 bool started,autostartLaunch;
 public MainWindow(bool test,bool autostart=false) {
  testMode=test; autostartLaunch=autostart&&!test; Directory.CreateDirectory(data); HookWallpaper(); RegisterCommands(); LoadLibraryRoot(); wallpaper.LibraryRoot=root; LoadVideoBackend(); wallpaper.LoadSettings(); Title="AWall · 轻量动态壁纸"; Width=1080; Height=760; MinWidth=880; MinHeight=640; WindowStartupLocation=WindowStartupLocation.CenterScreen;
  string themeError;var styles=Theme.Load(out themeError);themeMode=LoadThemeMode();darkNow=ResolveDark();
  var resources=new ResourceDictionary();resources.MergedDictionaries.Add(Theme.Palette(darkNow));resources.MergedDictionaries.Add(styles);Resources=resources;
  SetResourceReference(BackgroundProperty,"Bg"); SetResourceReference(ForegroundProperty,"Text"); FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI"); FontSize=13; UseLayoutRounding=true; SnapsToDevicePixels=true;
  TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
  var appIcon=Theme.AppIcon(); if(appIcon!=null)Icon=appIcon;
  var outer=new Grid(){Margin=new Thickness(24,18,24,16)}; Content=outer;
  outer.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto}); outer.RowDefinitions.Add(new RowDefinition()); outer.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto});
  // Header: logo, title and a "now playing" pill.
  var header=new Grid(){Margin=new Thickness(0,0,0,18)}; outer.Children.Add(header);
  header.ColumnDefinitions.Add(new ColumnDefinition(){Width=GridLength.Auto}); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition(){Width=GridLength.Auto});
  var logo=new Image(){Width=42,Height=42,Source=Theme.Logo(96),Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center}; RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality); header.Children.Add(logo);
  var titles=new StackPanel(){VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(titles,1); header.Children.Add(titles);
  titles.Children.Add(new TextBlock(){Text="AWall",FontSize=24,FontWeight=FontWeights.Bold});
  titles.Children.Add(Fg(new TextBlock(){Text="轻量动态桌面 · 静态锁屏",Margin=new Thickness(0,1,0,0)},"Muted"));
  var pill=new Border(){CornerRadius=new CornerRadius(17),BorderThickness=new Thickness(1),Padding=new Thickness(13,7,15,8),VerticalAlignment=VerticalAlignment.Center};
  SurfaceColors(pill);
  var headerRight=new StackPanel(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(headerRight,2); header.Children.Add(headerRight);
  themeButton=AddButton(headerRight,"\uE770",null,CycleTheme,"IconButton"); themeGlyph=(TextBlock)((StackPanel)themeButton.Content).Children[0]; themeGlyph.Margin=new Thickness(0);
  headerRight.Children.Add(pill); UpdateThemeButton(); var pillRow=new StackPanel(){Orientation=Orientation.Horizontal}; pill.Child=pillRow;
  nowDot.Width=8; nowDot.Height=8; nowDot.Fill=Theme.Idle; nowDot.VerticalAlignment=VerticalAlignment.Center; nowDot.Margin=new Thickness(0,1,9,0); pillRow.Children.Add(nowDot);
  nowText.Text="未播放"; nowText.MaxWidth=360; nowText.TextTrimming=TextTrimming.CharacterEllipsis; pillRow.Children.Add(nowText);
  var body=new Grid(); Grid.SetRow(body,1); outer.Children.Add(body);
  body.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(340)}); body.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(16)}); body.ColumnDefinitions.Add(new ColumnDefinition());
  // Library card: header with count, search, thumbnail list.
  var left=Card(14); body.Children.Add(left); var leftDock=new DockPanel(); left.Child=leftDock;
  var libraryHead=new DockPanel(){Margin=new Thickness(2,0,2,10)}; DockPanel.SetDock(libraryHead,Dock.Top); leftDock.Children.Add(libraryHead);
  Fg(countText,"Faint"); countText.VerticalAlignment=VerticalAlignment.Center; DockPanel.SetDock(countText,Dock.Right); libraryHead.Children.Add(countText);
  libraryHead.Children.Add(new TextBlock(){Text="素材库",FontSize=15,FontWeight=FontWeights.SemiBold});
  search.Style=TryFindResource("SearchBox") as Style; search.Margin=new Thickness(0,0,0,10); search.TextChanged+=delegate{ApplyFilter();}; DockPanel.SetDock(search,Dock.Top); leftDock.Children.Add(search);
  library.Style=TryFindResource("Library") as Style; library.ItemTemplate=TryFindResource("ItemTemplate") as DataTemplate; leftDock.Children.Add(library);
  // Detail card: preview, info, grouped actions, settings.
  var rightCard=Card(18); Grid.SetColumn(rightCard,2); body.Children.Add(rightCard); var right=new Grid(); rightCard.Child=right;
  right.RowDefinitions.Add(new RowDefinition(){MinHeight=120}); for(int i=0;i<4;i++)right.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto});
  var previewFrame=new Border(){CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1)}; previewFrame.SetResourceReference(Border.BackgroundProperty,"PreviewBack"); previewFrame.SetResourceReference(Border.BorderBrushProperty,"Line");
  // Same framing as the library thumbnails (76:44, fill + centre crop), scaled to the space available.
  var previewHost=new Grid(); right.Children.Add(previewHost); previewFrame.HorizontalAlignment=HorizontalAlignment.Center; previewFrame.VerticalAlignment=VerticalAlignment.Center; previewHost.Children.Add(previewFrame);
  previewHost.SizeChanged+=delegate{double w=previewHost.ActualWidth,h=previewHost.ActualHeight;if(w<=0||h<=0)return;double fw=Math.Min(w,h*PreviewAspect);previewFrame.Width=Math.Floor(fw);previewFrame.Height=Math.Floor(fw/PreviewAspect);};
  var previewGrid=new Grid(); previewFrame.Child=previewGrid;
  previewHint.Text="暂无预览"; Fg(previewHint,"Faint"); previewHint.HorizontalAlignment=HorizontalAlignment.Center; previewHint.VerticalAlignment=VerticalAlignment.Center; previewGrid.Children.Add(previewHint);
  previewImage.CornerRadius=new CornerRadius(9); previewImage.Background=previewBrush; previewImage.Visibility=Visibility.Collapsed; previewGrid.Children.Add(previewImage);
  var info=new StackPanel(){Margin=new Thickness(2,16,2,0)}; Grid.SetRow(info,1); right.Children.Add(info);
  detailTitle.Text="未选择素材"; detailTitle.FontSize=19; detailTitle.FontWeight=FontWeights.SemiBold; detailTitle.TextTrimming=TextTrimming.CharacterEllipsis; info.Children.Add(detailTitle);
  var metaRow=new StackPanel(){Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)}; info.Children.Add(metaRow);
  badge.CornerRadius=new CornerRadius(4); badge.Padding=new Thickness(7,1,7,2); badge.Margin=new Thickness(0,0,10,0); badge.VerticalAlignment=VerticalAlignment.Center; badge.Visibility=Visibility.Collapsed; badge.Child=badgeText; metaRow.Children.Add(badge);
  badgeText.FontSize=11; badgeText.FontWeight=FontWeights.SemiBold;
  Fg(detailMeta,"Muted"); detailMeta.VerticalAlignment=VerticalAlignment.Center; metaRow.Children.Add(detailMeta);
  Fg(detailFile,"Faint"); detailFile.FontSize=12; detailFile.Margin=new Thickness(0,6,0,0); detailFile.TextTrimming=TextTrimming.CharacterEllipsis; info.Children.Add(detailFile);
  var actions=Section(right,2,"壁纸");
  // Multi-display target row ("应用到"); only shown with more than one display.
  targetPanel=new WrapPanel(){Margin=new Thickness(0,0,0,4)}; ((StackPanel)actions.Parent).Children.Insert(1,targetPanel);
  applyButton=AddButton(actions,"\uE768","设为动态桌面",Apply,"PrimaryButton"); applyLabel=(TextBlock)((StackPanel)applyButton.Content).Children[1];
  var pauseContent=(StackPanel)AddButton(actions,"\uE769","暂停",delegate{Run("wallpaper.toggle-pause");},"SoftButton").Content; pauseGlyph=(TextBlock)pauseContent.Children[0]; pauseLabel=(TextBlock)pauseContent.Children[1];
  AddButton(actions,"\uE72E","同步锁屏快照",delegate{Run("wallpaper.lock-snapshot");},"SoftButton");
  propsButton=AddButton(actions,"\uE713","调整参数",OpenProperties,"SoftButton");propsButton.Visibility=Visibility.Collapsed;
  rotationItemButton=AddButton(actions,"\uE8EE","加入轮播",ToggleRotationItem,"SoftButton");
  stopButton=AddButton(actions,"\uE71A","停止壁纸",delegate{Run("wallpaper.stop");},"DangerButton");
  var manage=Section(right,3,"素材");
  AddButton(manage,"\uE710","导入本地文件",Import,"SoftButton"); AddButton(manage,"\uE72C","重新扫描",delegate{Run("library.scan");},"SoftButton"); AddButton(manage,"\uE896","从 Wallpaper Engine 导入",ImportFromWe,"SoftButton");
  var settings=new StackPanel(){Margin=new Thickness(2,12,2,0)}; Grid.SetRow(settings,4); right.Children.Add(settings);
  var switches=new WrapPanel(); settings.Children.Add(switches);
  foreach(var sw in new[]{pauseCovered,autostartSwitch,rotationSwitch}){sw.Style=TryFindResource("Switch") as Style; sw.Margin=new Thickness(0,0,22,8); switches.Children.Add(sw);}
  pauseCovered.Click+=delegate{wallpaper.SetPauseWhenCovered(pauseCovered.IsChecked==true);};
  autostartSwitch.IsChecked=Autostart.Enabled; autostartSwitch.Click+=delegate{SetAutostart(autostartSwitch.IsChecked==true);};
  rotationSwitch.IsChecked=wallpaper.RotationOn; rotationSwitch.Click+=delegate{wallpaper.SetRotation(rotationSwitch.IsChecked==true);};
  var options=new WrapPanel(); settings.Children.Add(options);
  rotationIntervalButton=SmallButton(options,"\uE916",wallpaper.CycleRotationInterval); rotationOrderButton=SmallButton(options,"\uE8B1",wallpaper.ToggleRotationOrder); videoBackendButton=SmallButton(options,"\uE714",CycleVideoBackend);
  settingsHint=Fg(new TextBlock(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,2,0,0)},"Faint"); settings.Children.Add(settingsHint);
  BuildTargets(); UpdateSettingsUi();
  // Status line.
  var statusBar=new DockPanel(){Margin=new Thickness(4,14,4,0)}; Grid.SetRow(statusBar,2); outer.Children.Add(statusBar);
  var statusIcon=Fg(new TextBlock(){Text="\uE946",FontSize=12,Margin=new Thickness(0,1,8,0),VerticalAlignment=VerticalAlignment.Center},"Faint"); statusIcon.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont"); statusBar.Children.Add(statusIcon);
  Fg(status,"Muted"); status.FontSize=12; status.TextTrimming=TextTrimming.CharacterEllipsis; status.VerticalAlignment=VerticalAlignment.Center; statusBar.Children.Add(status);
  library.SelectionChanged+=delegate{ShowDetail(library.SelectedItem as Item);UpdateActionButtons();};
  BuildTray(test); Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;Hide();}};
  SourceInitialized+=delegate{var handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(handle).AddHook(ShellMessage);windowHandle=handle;Theme.TitleBar(handle,darkNow);};
  // A second launch (desktop icon, Start menu) signals this event instead of showing a message box.
  if(!test&&Program.ShowSignal!=null)System.Threading.ThreadPool.RegisterWaitForSingleObject(Program.ShowSignal,delegate{Dispatcher.BeginInvoke(new Action(ShowMain));},null,-1,false);
  SystemEvents.UserPreferenceChanged+=PreferenceChanged;
  wallpaper.Start();
  if(themeError!=null)SetStatus("界面样式加载失败，已使用默认样式："+themeError);
  Scan(); Loaded+=delegate{Startup();};
  UpdateNowPlaying();
 }
 // Runs once: from Loaded, or directly for a hidden --autostart launch (the window is first shown
 // later from the tray, and its Loaded must not restore a second time).
 void Startup(){
  if(started)return;started=true;
  if(testMode){Hide();var a=Environment.GetCommandLineArgs();int at=Array.IndexOf(a,"--test-file");if(at>=0&&at+1<a.Length){string f=Path.GetFullPath(a[at+1]);items=new List<Item>{new Item(){Title=Path.GetFileName(f),Type=Item.TypeOf(f),File=f,Preview=""}};Bind();library.SelectedIndex=0;}if(a.Contains("--test-transitions"))TestTransitions();else if(a.Contains("--test-library"))TestLibrary();else RunTest();return;}
  RepairAutostart();wallpaper.ResetRotationClock();
  if(autostartLaunch)RestoreAfterLogin();else RestoreLayout();
 }
 // --autostart: create the window handle (tray, shell hooks) without showing the window.
 public void StartHidden(){new WindowInteropHelper(this).EnsureHandle();Dispatcher.BeginInvoke(new Action(Startup));}
 Border Card(double padding){return SurfaceColors(new Border(){BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(padding)});}
 static Border SurfaceColors(Border border){border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Line");return border;}
 // Theme-aware foreground: TextBlock and Control share TextElement.ForegroundProperty.
 static T Fg<T>(T element,string key) where T:FrameworkElement{element.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,key);return element;}
 WrapPanel Section(Grid parent,int row,string label){
  var box=new StackPanel(){Margin=new Thickness(2,14,2,0)};Grid.SetRow(box,row);parent.Children.Add(box);
  box.Children.Add(Fg(new TextBlock(){Text=label,FontSize=12,Margin=new Thickness(0,0,0,8)},"Faint"));
  var wrap=new WrapPanel();box.Children.Add(wrap);return wrap;
 }
 Button AddButton(Panel p,string glyph,string text,Action a,string style) {
  var icon=new TextBlock(){Text=glyph,FontSize=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};icon.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont");
  var content=new StackPanel(){Orientation=Orientation.Horizontal};content.Children.Add(icon);if(text!=null)content.Children.Add(new TextBlock(){Text=text,VerticalAlignment=VerticalAlignment.Center});
  var b=new Button(){Content=content,Style=TryFindResource(style) as Style};b.Click+=delegate{try{a();}catch(Exception e){SetStatus(e.Message);}};p.Children.Add(b);return b;
 }
 // ---- Now playing across displays
 string NowTitle(){var distinct=wallpaper.Layout.Values.Where(x=>x!=null).GroupBy(x=>WallpaperService.ItemKey(x)).Select(g=>g.First()).ToList();return distinct.Count==0?"":distinct.Count==1?distinct[0].Title:distinct.Count+" 个壁纸（多屏）";}
 string NowDetail(){
  var layout=wallpaper.Layout;if(layout.Count==0)return null;var screens=WallpaperService.OrderedScreens();if(screens.Length<2)return current==null?null:current.Title;
  var lines=new List<string>();
  for(int i=0;i<screens.Length;i++){Item shown;lines.Add("显示器 "+(i+1)+(screens[i].Primary?"（主）":"")+"："+(layout.TryGetValue(screens[i].DeviceName,out shown)&&shown!=null?shown.Title:"未播放"));}
  return String.Join("\n",lines.ToArray());
 }
 // ---- "应用到" row: all displays, or one of them (numbered left to right)
 void BuildTargets(){
  if(targetPanel==null)return;targetPanel.Children.Clear();
  var screens=WallpaperService.OrderedScreens();
  if(applyTarget!=null&&!screens.Any(s=>s.DeviceName==applyTarget))applyTarget=null;
  if(screens.Length<2){applyTarget=null;targetPanel.Visibility=Visibility.Collapsed;UpdateTargetButtons();return;}
  targetPanel.Visibility=Visibility.Visible;
  targetPanel.Children.Add(Fg(new TextBlock(){Text="应用到",FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,8)},"Muted"));
  AddTarget("全部显示器",null);
  for(int i=0;i<screens.Length;i++)AddTarget("显示器 "+(i+1)+(screens[i].Primary?" · 主":""),screens[i].DeviceName);
  UpdateTargetButtons();
 }
 void AddTarget(string text,string device){
  var b=new Button(){Content=text,Style=TryFindResource("SoftButton") as Style,Padding=new Thickness(12,4,12,5),MinHeight=30,FontSize=12,Tag=device};
  b.Click+=delegate{applyTarget=device;UpdateActionButtons();};
  targetPanel.Children.Add(b);
 }
 void UpdateTargetButtons(){
  var screens=WallpaperService.OrderedScreens();var layout=wallpaper.Layout;
  if(targetPanel!=null)foreach(var b in targetPanel.Children.OfType<Button>()){
   string device=b.Tag as string;
   if(device==applyTarget){b.SetResourceReference(Button.BorderBrushProperty,"Accent");b.SetResourceReference(Button.ForegroundProperty,"Accent");}
   else{b.SetResourceReference(Button.BorderBrushProperty,"ButtonLine");b.SetResourceReference(Button.ForegroundProperty,"Text");}
   var screen=screens.FirstOrDefault(x=>x.DeviceName==device);Item shown;
   b.ToolTip=device==null?"所有显示器播放同一张壁纸":screen==null?null:screen.Bounds.Width+"×"+screen.Bounds.Height+" · 当前："+(layout.TryGetValue(device,out shown)&&shown!=null?shown.Title:"未播放");
  }
  if(applyLabel!=null){int index=applyTarget==null?-1:Array.FindIndex(screens,x=>x.DeviceName==applyTarget);applyLabel.Text=index<0?"设为动态桌面":"设到显示器 "+(index+1);}
 }
 Button SmallButton(Panel p,string glyph,Action a){var b=AddButton(p,glyph," ",a,"SoftButton");b.Padding=new Thickness(10,4,12,5);b.MinHeight=30;b.FontSize=12;return b;}
 static void SetButtonText(Button b,string text){if(b==null)return;var content=b.Content as StackPanel;if(content!=null&&content.Children.Count>1)((TextBlock)content.Children[1]).Text=text;}
 static string Shorten(string text,int max){text=text??"";return text.Length>max?text.Substring(0,max-1)+"…":text;}
 void ShowMain(){if(exiting)return;if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();Topmost=true;Topmost=false;Focus();}
 static string WebMeta(Item item){
  var present=WebPresent.Of(item.File);
  if(WallpaperService.RouteOf(item)==PresentRoute.SnapshotToSystemWallpaper)
   return present.Mode==PresentMode.Interval?"网页 · 快照档：每 "+present.IntervalSeconds+" 秒离屏渲染一次，设为系统壁纸":"网页 · 静态：渲染一次后设为系统壁纸";
  return "网页 · WebView2 离线渲染（首次播放后自动生成预览图）";
 }
 void ShowDetail(Item item){
  if(item==null)return;
  detailTitle.Text=String.IsNullOrEmpty(item.Title)?"未命名素材":item.Title;detailTitle.ToolTip=item.Title;
  badge.Visibility=Visibility.Visible;badge.SetResourceReference(Border.BackgroundProperty,item.BadgeKey+"Back");badgeText.SetResourceReference(TextBlock.ForegroundProperty,item.BadgeKey+"Fore");badgeText.Text=item.Badge;
  detailMeta.Text=item.Type=="video"?(MpvActive?"视频 · mpv 硬件解码":"视频 · 内置播放器（WebView2 硬件解码）"):item.Type=="scene"?"PKG 场景 · WebGL 兼容渲染（非官方引擎）":item.Type=="image"?"图片 · 设为 Windows 系统壁纸（AWall 不参与渲染）":WebMeta(item);
  detailFile.Text=Path.GetFileName(item.File??"");detailFile.ToolTip=item.File;
  propsButton.Visibility=item.Type=="web"&&WebProperties.Parse(item).Count>0?Visibility.Visible:Visibility.Collapsed;
  SetButtonText(rotationItemButton,wallpaper.InRotation(item)?"移出轮播":"加入轮播");
  var image=Item.LoadImage(item.Preview,1280);previewBrush.ImageSource=image;
  previewImage.Visibility=image==null?Visibility.Collapsed:Visibility.Visible;previewHint.Visibility=image==null?Visibility.Visible:Visibility.Collapsed;
 }
 // Parameters declared inside an HTML wallpaper. Changes apply live when that wallpaper is playing
 // and are saved for the next time it plays.
 void OpenProperties(){
  var item=library.SelectedItem as Item;if(item==null||item.Type!="web")return;
  string error;var props=WebProperties.Parse(item,out error);
  if(error!=null){SetStatus(error);return;}
  if(props.Count==0){SetStatus("这个网页没有声明可调参数。");return;}
  if(propsWindow!=null){propsWindow.Close();propsWindow=null;}
  propsWindow=new PropertiesWindow(this,item,props,delegate(string name,object value){
   wallpaper.SetWebProperty(item,name,value);
  },darkNow);
  propsWindow.Closed+=delegate(object s,EventArgs e){if(propsWindow==s)propsWindow=null;};
  propsWindow.Show();
 }
 void Bind(){library.ItemsSource=null;library.ItemsSource=items;ApplyFilter();}
 void ApplyFilter(){
  var view=CollectionViewSource.GetDefaultView(items);if(view==null)return;string q=(search.Text??"").Trim();
  if(q.Length==0)view.Filter=null;else view.Filter=delegate(object o){var it=o as Item;return it!=null&&(it.Title??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0;};
  countText.Text=q.Length==0?items.Count+" 项":items.Count(x=>view.Filter(x))+" / "+items.Count;
 }
 // "设为动态桌面" is disabled while the selected item is already the active
 // wallpaper (playing, paused or loading); "停止壁纸" only when nothing is active.
 bool IsActive(Item item){
  var layout=wallpaper.Layout;if(item==null||layout.Count==0)return false;
  var screens=Forms.Screen.AllScreens;
  var targets=applyTarget!=null&&screens.Any(s=>s.DeviceName==applyTarget)?new[]{applyTarget}:screens.Select(s=>s.DeviceName).ToArray();
  foreach(var device in targets){Item shown;if(!layout.TryGetValue(device,out shown)||!WallpaperService.SameItem(shown,item))return false;}
  return true;
 }
 void UpdateActionButtons(){
  bool active=wallpaper.Active;
  if(applyButton!=null)applyButton.IsEnabled=library.SelectedItem is Item&&!IsActive(library.SelectedItem as Item);
  if(stopButton!=null)stopButton.IsEnabled=active;
  UpdateTargetButtons();
 }
 void UpdateNowPlaying(){
  UpdateActionButtons();
  bool loading=wallpaper.Loading,paused=wallpaper.Paused;
  string text=current==null?"未播放":(loading?"正在加载 · ":paused?"已暂停 · ":"正在播放 · ")+NowTitle();
  string detail=NowDetail();
  string key=text+"|"+paused+"|"+detail;if(key==lastNow)return;lastNow=key;
  nowText.Text=text;nowText.ToolTip=detail;
  nowDot.Fill=current==null?Theme.Idle:loading?Theme.Accent:paused?Theme.Warn:Theme.Ok;
  if(pauseGlyph!=null){pauseGlyph.Text=paused?"\uE768":"\uE769";pauseLabel.Text=paused?"继续":"暂停";}
  if(tray!=null)tray.Text=Shorten("AWall · "+text,63);
 }
 void SetStatus(string s){status.Text=s;status.ToolTip=s;UpdateNowPlaying();System.IO.File.AppendAllText(Path.Combine(data,"AWall.log"),DateTime.Now.ToString("s")+" "+s+Environment.NewLine);}
 void Exit(){exiting=true;wallpaper.Shutdown();SystemEvents.UserPreferenceChanged-=PreferenceChanged;tray.Dispose();Application.Current.Shutdown();}
}
}
