using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AWall {
// Fences controller: loads fences.json, lists the desktop, sorts items into fences, keeps one FenceView per
// visible fence mounted in the AboveIcons layer, and watches folders for changes.
// Desktop takeover: while any rule / free fence exists, the system icons are hidden (DesktopIcons) and an
// "其他" fence shows what no fence took, so nothing disappears. Exit / crash / Explorer restart bring the
// icons back through DesktopIcons' rules; the health timer re-hides them after Explorer comes back.
public sealed class FenceService {
 public FenceStore Store {get;private set;}
 public double Scale {get;private set;}
 public event Action<string> Status;
 public event Action Changed;
 readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
 readonly Dictionary<string,FenceView> views=new Dictionary<string,FenceView>(StringComparer.OrdinalIgnoreCase);
 readonly Dictionary<string,BitmapSource> icons=new Dictionary<string,BitmapSource>();
 readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
 readonly DispatcherTimer refreshTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(350)};
 readonly DispatcherTimer health=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(2)};
 List<DesktopEntry> desktop=new List<DesktopEntry>();
 bool started,stopping,takeoverApplied;
 public int Remounts {get;private set;}

 public FenceService(){
  using(var g=System.Drawing.Graphics.FromHwnd(IntPtr.Zero))Scale=g.DpiX/96.0;
  refreshTimer.Tick+=delegate{refreshTimer.Stop();Refresh();};
  health.Tick+=delegate{Health();};
 }
 public void Say(string message){var h=Status;if(h!=null)h(message);}
 void Raise(){var h=Changed;if(h!=null)h();}
 public IList<FenceView> Views {get {return views.Values.ToList();}}
 public FenceView View(string id){FenceView v;return views.TryGetValue(id,out v)?v:null;}

 // ---- Palette per display (from its wallpaper; see WallpaperPalette)
 readonly Dictionary<string,FencePalette> palettes=new Dictionary<string,FencePalette>(StringComparer.OrdinalIgnoreCase);
 public FencePalette PaletteFor(string display){FencePalette p;return display!=null&&palettes.TryGetValue(display,out p)?p:FencePalette.Default;}
 public bool HasPalette(string display){return display!=null&&palettes.ContainsKey(display);}
 // Displays that show fences, i.e. whose wallpaper colours matter.
 public IList<string> FenceDisplays {get {return views.Values.Select(v=>v.Config.Display).Where(x=>x!=null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();}}
 public void SetPalette(string display,FencePalette p,bool animate){
  if(display==null||p==null)return;
  FencePalette old;if(palettes.TryGetValue(display,out old)&&old.SameAs(p))return;
  palettes[display]=p;
  foreach(var v in views.Values)if(String.Equals(v.Config.Display,display,StringComparison.OrdinalIgnoreCase))v.ApplyPalette(p,animate);
 }

 // ---- Clean desktop: double-click on empty desktop hides the icons and every fence; again shows them.
 public bool Clean {get;private set;}
 bool cleanHidIcons;
 DesktopDoubleClick doubleClick;
 public bool DoubleClickRunning {get {return doubleClick!=null&&doubleClick.Running;}}
 public void SetClean(bool on){
  if(on==Clean||!started)return;Clean=on;
  foreach(var v in views.Values)Place(v);
  if(on){cleanHidIcons=!DesktopIcons.HiddenByAWall&&DesktopIcons.Visible&&DesktopIcons.SetVisible(false);}
  else{if(cleanHidIcons&&!Store.Takeover)DesktopIcons.Restore();cleanHidIcons=false;}
  Say(on?"已隐藏桌面图标和分区，再次双击桌面空白处恢复。":"已恢复桌面图标和分区。");Raise();
 }
 public void SetDoubleClickHide(bool on){Store.DoubleClickHide=on;Save();UpdateHook();Raise();}
 void UpdateHook(){
  bool want=started&&!stopping&&Store.DoubleClickHide;
  if(want&&doubleClick==null){
   doubleClick=new DesktopDoubleClick();
   doubleClick.DoubleClicked+=delegate{dispatcher.BeginInvoke(new Action(delegate{SetClean(!Clean);}));};
   try{doubleClick.Start();}catch(Exception e){doubleClick=null;Say("双击隐藏不可用："+e.Message);}
  }else if(!want&&doubleClick!=null){doubleClick.Dispose();doubleClick=null;}
 }

 public void Start(string storePath){
  if(started)return;started=true;
  Store=FenceStore.Load(storePath);
  Rewatch();Refresh();health.Start();UpdateHook();
 }
 public void Stop(){
  if(!started)return;stopping=true;health.Stop();refreshTimer.Stop();UpdateHook();
  if(Clean){Clean=false;if(cleanHidIcons)DesktopIcons.Restore();cleanHidIcons=false;}
  foreach(var w in watchers)w.Dispose();watchers.Clear();
  foreach(var v in views.Values.ToList())v.Close();views.Clear();
  if(takeoverApplied)DesktopIcons.Restore();takeoverApplied=false;started=false;stopping=false;
 }
 void Save(){try{Store.Save();}catch(Exception e){Say("分区布局保存失败："+e.Message);}}

 // ---- Desktop listing and folder watching
 void Rewatch(){
  foreach(var w in watchers)w.Dispose();watchers.Clear();
  var dirs=new List<string>{ShellItems.UserDesktop,ShellItems.PublicDesktop};
  dirs.AddRange(Store.Fences.Where(x=>x.Kind=="folder"&&!String.IsNullOrEmpty(x.Folder)).Select(x=>x.Folder));
  foreach(var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase)){
   if(!Directory.Exists(dir))continue;
   try{
    var w=new FileSystemWatcher(dir){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.Attributes|NotifyFilters.LastWrite};
    FileSystemEventHandler changed=delegate{dispatcher.BeginInvoke(new Action(ScheduleRefresh));};
    w.Created+=changed;w.Deleted+=changed;w.Changed+=changed;
    w.Renamed+=delegate(object s,RenamedEventArgs e){string from=e.OldFullPath,to=e.FullPath;dispatcher.BeginInvoke(new Action(delegate{Renamed(from,to);ScheduleRefresh();}));};
    w.Error+=delegate{dispatcher.BeginInvoke(new Action(ScheduleRefresh));};
    w.EnableRaisingEvents=true;watchers.Add(w);
   }catch{}// An unreadable folder shows as empty; the fence hint says it is missing.
  }
 }
 void ScheduleRefresh(){if(stopping||!started)return;refreshTimer.Stop();refreshTimer.Start();}
 // A free fence follows its items when they are renamed (on the desktop or by the fence's own F2).
 void Renamed(string from,string to){
  bool dirty=false;
  foreach(var f in Store.Fences.Where(x=>x.Kind=="free"))for(int i=0;i<f.Items.Count;i++)if(String.Equals(f.Items[i],from,StringComparison.OrdinalIgnoreCase)){f.Items[i]=to;dirty=true;}
  if(dirty)Save();
 }
 public void Refresh(){
  if(!started)return;
  desktop=ShellItems.ListDesktop();
  ApplyTakeover(); // creates "其他" first, so this pass already fills it
  var sorted=FenceStore.Classify(desktop,Store.Fences,DateTime.Now);
  Reconcile();
  foreach(var v in views.Values){
   List<DesktopEntry> list;
   if(v.Config.Kind=="folder")list=ShellItems.ListFolder(v.Config.Folder);
   else if(!sorted.TryGetValue(v.Config.Id,out list))list=new List<DesktopEntry>();
   v.SetEntries(list);
  }
  Raise();
 }
 public int DesktopCount {get {return desktop.Count;}}
 void ApplyTakeover(){
  bool want=Store.Takeover;
  if(want&&Store.Other==null){var o=NewFence("other","其他",null);o.Collapsed=false;Store.Fences.Add(o);Save();}
  if(want&&!takeoverApplied){if(DesktopIcons.SetVisible(false))takeoverApplied=true;}
  else if(!want&&takeoverApplied){DesktopIcons.Restore();takeoverApplied=false;}
 }
 // One view per fence that should be on screen.
 void Reconcile(){
  var wanted=Store.Fences.Where(x=>x.Kind!="other"||Store.Takeover).Select(x=>x.Id).ToList();
  foreach(var id in views.Keys.ToList())if(!wanted.Contains(id,StringComparer.OrdinalIgnoreCase)){views[id].Close();views.Remove(id);}
  foreach(var f in Store.Fences)if(wanted.Contains(f.Id,StringComparer.OrdinalIgnoreCase)){
   FenceView v;if(!views.TryGetValue(f.Id,out v)){v=new FenceView(this,f);views[f.Id]=v;}
   Place(v);
  }
 }
 void Place(FenceView v){
  var b=Bounds(v.Config);
  if(b.IsEmpty||Clean){if(v.Window.Handle!=IntPtr.Zero&&v.Window.IsVisible)v.Window.Hide();return;}// display not connected, or clean desktop
  try{if(!v.Window.IsVisible&&v.Window.Handle!=IntPtr.Zero)v.Window.Show();v.Mount(b);}catch(Exception e){Say("分区挂到桌面失败，稍后重试："+e.Message);}
 }

 // ---- Geometry: config stores pixels relative to its display.
 static Forms.Screen ScreenOf(FenceConfig f){
  if(String.IsNullOrEmpty(f.Display))return Forms.Screen.PrimaryScreen;
  return Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==f.Display);
 }
 public System.Drawing.Rectangle Bounds(FenceConfig f){
  var screen=ScreenOf(f);if(screen==null)return System.Drawing.Rectangle.Empty;
  var s=screen.Bounds;var r=new System.Drawing.Rectangle(s.Left+f.X,s.Top+f.Y,f.Width,f.Height);
  return ClampToDisplay(f,r);
 }
 public System.Drawing.Rectangle ClampToDisplay(FenceConfig f,System.Drawing.Rectangle r){
  var screen=ScreenOf(f);if(screen==null)return r;var a=screen.WorkingArea;
  r.Width=Math.Min(Math.Max(r.Width,(int)(160*Scale)),a.Width);r.Height=Math.Min(Math.Max(r.Height,(int)(60*Scale)),a.Height);
  r.X=Math.Max(a.Left,Math.Min(r.X,a.Right-r.Width));r.Y=Math.Max(a.Top,Math.Min(r.Y,a.Bottom-r.Height));
  return r;
 }
 FenceConfig NewFence(string kind,string title,string display){
  var screen=String.IsNullOrEmpty(display)?Forms.Screen.PrimaryScreen:(Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==display)??Forms.Screen.PrimaryScreen);
  int w=(int)Math.Round(300*Scale),h=(int)Math.Round(260*Scale),gap=(int)Math.Round(16*Scale);
  var f=new FenceConfig(){Id=Store.NewId(),Kind=kind,Title=title,Display=screen.DeviceName,Width=w,Height=h};
  var area=screen.WorkingArea;var s=screen.Bounds;
  if(kind=="other"){f.X=area.Right-s.Left-w-gap;f.Y=area.Bottom-s.Top-h-gap;return f;}
  // Next free slot in a row along the top of the work area, wrapping to the next row.
  int count=Store.Fences.Count(x=>x.Kind!="other"&&x.Display==f.Display);
  int perRow=Math.Max(1,(area.Width-gap)/(w+gap));
  f.X=area.Left-s.Left+gap+(count%perRow)*(w+gap);f.Y=area.Top-s.Top+gap+(count/perRow)*(h+gap);
  return f;
 }

 // ---- Editing (commands, menus)
 public FenceConfig AddPreset(string preset,string display){
  string title;FenceRule rule;if(!FenceRule.TryPreset(preset,out title,out rule))return null;
  var f=NewFence("rule",title,display);f.Rule=rule;return Add(f);
 }
 public FenceConfig AddRule(string title,FenceRule rule,string display){var f=NewFence("rule",title,display);f.Rule=rule;return Add(f);}
 public FenceConfig AddFree(string title,string display){return Add(NewFence("free",title,display));}
 public FenceConfig AddFolder(string folder,string title,string display){
  var f=NewFence("folder",String.IsNullOrEmpty(title)?Path.GetFileName(folder.TrimEnd('\\'))??folder:title,display);f.Folder=Path.GetFullPath(folder);
  var added=Add(f);Rewatch();Refresh();return added;
 }
 public void AddFolderInteractive(string display){
  using(var dialog=new Forms.FolderBrowserDialog(){Description="选择要在桌面分区里显示的文件夹",ShowNewFolderButton=false}){
   if(dialog.ShowDialog()==Forms.DialogResult.OK)AddFolder(dialog.SelectedPath,null,display);
  }
 }
 FenceConfig Add(FenceConfig f){Store.Fences.Insert(Store.Other==null?Store.Fences.Count:Store.Fences.IndexOf(Store.Other),f);Save();Refresh();Say("已新建分区："+f.Title);return f;}
 public bool Remove(string id){
  var f=Store.Find(id);if(f==null||f.Kind=="other")return false;
  Store.Fences.Remove(f);
  if(!Store.Takeover&&Store.Other!=null)Store.Fences.Remove(Store.Other);// "其他" only exists during takeover
  Save();if(f.Kind=="folder")Rewatch();Refresh();Say("已删除分区："+f.Title+"（文件没有变动）");return true;
 }
 public void SetUnlocked(bool on){Store.Unlocked=on;Save();foreach(var v in views.Values)v.Refresh();Say(on?"分区布局已解锁：拖动标题栏移动，拖右下角改大小。":"分区布局已锁定。");Raise();}
 public void SetTitle(FenceView v,string title){v.Config.Title=title;Save();v.Refresh();Raise();}
 public void ToggleCollapsed(FenceView v){v.Config.Collapsed=!v.Config.Collapsed;Save();v.Refresh();Place(v);Raise();}
 public void Moved(FenceView v,System.Drawing.Rectangle b){
  var screen=ScreenOf(v.Config);if(screen==null)return;
  v.Config.X=b.Left-screen.Bounds.Left;v.Config.Y=b.Top-screen.Bounds.Top;v.Config.Width=b.Width;
  if(!v.Config.Collapsed)v.Config.Height=b.Height;
  Save();Raise();
 }
 public bool SetGeometry(string id,int? x,int? y,int? w,int? h){
  var f=Store.Find(id);if(f==null)return false;
  if(x.HasValue)f.X=x.Value;if(y.HasValue)f.Y=y.Value;if(w.HasValue)f.Width=w.Value;if(h.HasValue)f.Height=h.Value;
  Save();var v=View(id);if(v!=null)Place(v);Raise();return true;
 }

 // ---- Items
 public void LoadIcon(DesktopEntry e,Action<BitmapSource> done){
  string key=e.Key+"|"+e.Modified.Ticks;BitmapSource icon;
  if(icons.TryGetValue(key,out icon)){done(icon);return;}
  int size=(int)Math.Round(40*Scale);
  dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{
   BitmapSource b;if(!icons.TryGetValue(key,out b)){b=ShellItems.Icon(e.Path,size);icons[key]=b;}
   if(b!=null)done(b);
  }));
 }
 public void Open(IEnumerable<DesktopEntry> list){foreach(var e in list.Take(16))if(!ShellItems.Open(e.Path))Say("无法打开："+e.Name);}
 public void Recycle(IList<DesktopEntry> list){if(ShellItems.Recycle(list.Select(x=>x.Path).ToList()))ScheduleRefresh();}
 public void Rename(DesktopEntry e,string name){
  // Explorer hides known extensions (and always .lnk / .url); keep the real one unless the user typed it.
  string real=Path.GetFileName(e.Path),ext=e.IsFolder?"":Path.GetExtension(real);
  if(ext.Length>0&&!e.Name.EndsWith(ext,StringComparison.OrdinalIgnoreCase)&&!name.EndsWith(ext,StringComparison.OrdinalIgnoreCase))name+=ext;
  string error;if(!ShellItems.Rename(e.Path,name,out error)){Say(error);return;}
  Renamed(e.Path,Path.Combine(Path.GetDirectoryName(e.Path),name));ScheduleRefresh();
 }
 public void ShowItemMenu(FenceView v,IList<DesktopEntry> list,int x,int y,Action rename){
  try{var verb=ShellItems.ShowMenu(v.Window.Handle,list.Select(e=>e.Path).ToList(),x,y,rename);if(verb!=null)ScheduleRefresh();}
  catch(Exception e){Say(e.Message);}
 }

 // ---- Drag and drop into a fence
 static string[] Files(IDataObject data){return data.GetDataPresent(DataFormats.FileDrop)?data.GetData(DataFormats.FileDrop) as string[]:null;}
 static string SourceFence(IDataObject data){return data.GetDataPresent(FenceView.DragFormat)?data.GetData(FenceView.DragFormat) as string:null;}
 static bool Ctrl(DragDropKeyStates k){return (k&DragDropKeyStates.ControlKey)!=0;}
 static bool Shift(DragDropKeyStates k){return (k&DragDropKeyStates.ShiftKey)!=0;}
 DragDropEffects TransferEffect(string[] files,string folder,DragDropKeyStates keys){
  bool copy=Ctrl(keys)||(!Shift(keys)&&ShellItems.DefaultIsCopy(files,folder));return copy?DragDropEffects.Copy:DragDropEffects.Move;
 }
 public DragDropEffects DropEffect(FenceView v,IDataObject data,DragDropKeyStates keys){
  var files=Files(data);if(files==null||files.Length==0)return DragDropEffects.None;
  string source=SourceFence(data);var kind=v.Config.Kind;
  if(kind=="folder"){
   if(source==v.Config.Id)return DragDropEffects.None;
   if(files.All(x=>String.Equals(Path.GetDirectoryName(x),v.Config.Folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))return DragDropEffects.None;
   return TransferEffect(files,v.Config.Folder,keys);
  }
  bool fromDesktop=files.All(ShellItems.IsOnDesktop);
  if(fromDesktop){
   if(kind=="free")return DragDropEffects.Move;                                   // assign / reorder
   var src=source==null?null:Store.Find(source);
   return src!=null&&src.Kind=="free"?DragDropEffects.Move:DragDropEffects.None;  // take it out of a free fence
  }
  if(Ctrl(keys)&&Shift(keys))return DragDropEffects.None;                         // no shortcuts in fences
  return TransferEffect(files,ShellItems.UserDesktop,keys);                       // put it on the desktop
 }
 public void Drop(FenceView v,string[] files,int index,DragDropEffects effect){
  if(files==null||files.Length==0)return;var kind=v.Config.Kind;
  if(kind=="folder"){ShellItems.Transfer(files,v.Config.Folder,effect==DragDropEffects.Copy);ScheduleRefresh();return;}
  var onDesktop=new List<string>();
  if(files.All(ShellItems.IsOnDesktop))onDesktop.AddRange(files);
  else{
   if(!ShellItems.Transfer(files,ShellItems.UserDesktop,effect==DragDropEffects.Copy))return;
   onDesktop.AddRange(files.Select(x=>Path.Combine(ShellItems.UserDesktop,Path.GetFileName(x))).Where(x=>File.Exists(x)||Directory.Exists(x)));
  }
  foreach(var f in Store.Fences.Where(x=>x.Kind=="free"))f.Items.RemoveAll(x=>onDesktop.Contains(x,StringComparer.OrdinalIgnoreCase));
  if(kind=="free"){
   // index counts the tiles shown; map it to the stored list (which may contain items no longer on the desktop).
   var shown=v.Entries.Select(x=>x.Path).Where(x=>!onDesktop.Contains(x,StringComparer.OrdinalIgnoreCase)).ToList();
   int at=index>=v.Entries.Count||shown.Count==0?v.Config.Items.Count:Math.Max(0,v.Config.Items.FindIndex(x=>String.Equals(x,v.Entries[Math.Min(index,v.Entries.Count-1)].Path,StringComparison.OrdinalIgnoreCase)));
   if(at<0||at>v.Config.Items.Count)at=v.Config.Items.Count;
   v.Config.Items.InsertRange(at,onDesktop);
  }
  Save();Refresh();
 }

 // ---- Health: Explorer restarts destroy our child windows (their parent goes away).
 public void WindowLost(FenceView v){if(!stopping)dispatcher.BeginInvoke(new Action(Health));}
 // A new Explorer shows its icons again: leave clean mode so fences and icons agree.
 public void ShellRestarted(){takeoverApplied=false;if(Clean){Clean=false;cleanHidIcons=false;Raise();}dispatcher.BeginInvoke(new Action(Health));}
 public void DisplaysChanged(){if(started)dispatcher.BeginInvoke(new Action(delegate{foreach(var v in views.Values)Place(v);}));}
 void Health(){
  if(!started||stopping)return;
  bool remounted=false;
  foreach(var v in views.Values.ToList()){
   if(v.Removed)continue;
   try{
    if(v.Window.IsClosed){v.Recreate();Place(v);remounted=true;continue;}
    if(!v.Window.Revealed&&v.Window.IsVisible&&!DesktopHost.IsAboveIcons(v.Window.Handle)){Place(v);remounted=true;}
   }catch{}// Explorer still starting; the next tick retries.
  }
  if(remounted)++Remounts;
  if(Store.Takeover&&(remounted||!takeoverApplied)){takeoverApplied=false;ApplyTakeover();}
 }
}
}
