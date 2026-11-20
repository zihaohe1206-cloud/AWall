using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Forms = System.Windows.Forms;

namespace AWall {
// "能静则静" for wallpapers (docs/ARCHITECTURE.md §2). A display whose wallpaper does not animate has
// no player at all: Windows draws it as its own wallpaper.
//   image                       -> SystemWallpaper: the file itself
//   HTML with awall-present     -> SnapshotToSystemWallpaper: render off-screen, save a PNG, release the
//                                  renderer; "interval=N" repeats every N s (>= 60), "static" renders once
// Every display picks its own route, so one display can play a video while another shows an image.
public partial class WallpaperService {
 sealed class StaticDisplay {public Item Item;public string Image;public int IntervalSeconds;public DateTime Next=DateTime.MaxValue;public bool Busy;}
 // Displays Windows draws for us, keyed by Screen.DeviceName. They never have a player.
 readonly Dictionary<string,StaticDisplay> statics=new Dictionary<string,StaticDisplay>();

 public static PresentRoute RouteOf(Item item){
  if(item==null)return PresentRoute.LiveWindow;
  if(item.Type=="image")return PresentRoute.SystemWallpaper;
  if(item.Type=="web"){
   var present=WebPresent.Of(item.File);
   // A static page still has to be rendered once, so both non-live answers mean "snapshot".
   return PresentPolicy.Choose(DesktopLayer.Wallpaper,present.Mode,present.IntervalSeconds,false)==PresentRoute.LiveWindow?PresentRoute.LiveWindow:PresentRoute.SnapshotToSystemWallpaper;
  }
  return PresentRoute.LiveWindow;
 }
 public static string RouteName(Item item){var route=RouteOf(item);return route==PresentRoute.SystemWallpaper?"system-wallpaper":route==PresentRoute.SnapshotToSystemWallpaper?"snapshot":"live";}
 static bool IsLive(Item item){return RouteOf(item)==PresentRoute.LiveWindow;}
 static int RefreshSeconds(Item item){if(item==null||item.Type!="web")return 0;var present=WebPresent.Of(item.File);return present.Mode==PresentMode.Interval?present.IntervalSeconds:0;}
 bool StaticShows(string device,Item item){StaticDisplay shown;return statics.TryGetValue(device,out shown)&&SameItem(shown.Item,item);}
 // The live players' layout plus the displays Windows draws.
 Dictionary<string,Item> WithStatics(Dictionary<string,Item> live){foreach(var pair in statics)if(!live.ContainsKey(pair.Key))live[pair.Key]=pair.Value.Item;return live;}
 public bool CanSyncLock {get {return players.Count>0||statics.Count>0;}}
 // Image Windows shows on this display because of AWall (null when a player or nothing is there).
 string StaticImage(string device){StaticDisplay shown;return statics.TryGetValue(device,out shown)?shown.Image:null;}

 // The image to hand to Windows for one display.
 async Task<string> PrepareStatic(Item item,Forms.Screen screen){
  if(item.Type=="image"){if(!File.Exists(item.File))throw new FileNotFoundException("图片不存在："+item.Title,item.File);return item.File;}
  return await RenderSnapshot(item,screen);
 }
 string SnapshotDir {get {return Path.Combine(data,TestMode?"snapshots-test":"snapshots");}}
 static string DeviceKey(string device){return new string((device??"").Select(c=>Path.GetInvalidFileNameChars().Contains(c)||c=='.'?'_':c).ToArray()).Trim('_');}
 // Off-screen staging player (never presented) at the display's size: wait for the first frame, let
 // intro animations settle, capture a PNG, close the player. Its WebView2 uses its own profile slot.
 async Task<string> RenderSnapshot(Item item,Forms.Screen screen){
  Directory.CreateDirectory(SnapshotDir);
  string path=Path.Combine(SnapshotDir,DeviceKey(screen.DeviceName)+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png");
  var clock=Stopwatch.StartNew();var p=new Player(item,screen);
  try{
   p.Show();
   // A presented player is sized to the monitor by DesktopHost.Attach; this one never is, and its WPF size
   // carries a few extra pixels. Size the off-screen window to the monitor's exact pixels so the page lays out
   // for the real viewport (SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED; Player keeps it off-screen).
   var b=screen.Bounds;Native.SetWindowPos(p.Handle,IntPtr.Zero,-32000,-32000,b.Width,b.Height,0x0004|0x0010|0x0020);
   while(!p.Ready&&!p.Failed){if(clock.Elapsed.TotalSeconds>30)throw new TimeoutException("网页快照超时："+item.Title);await Task.Delay(50);}
   if(p.Failed)throw new Exception("网页快照失败："+p.LastError);
   await Task.Delay(1000);
   if(p.IsClosed||p.Failed)throw new Exception("网页快照失败："+p.LastError);
   await p.Scene.SnapshotPng(path);
   TransitionLog("snapshot "+screen.DeviceName+" "+Path.GetFileName(path)+" ms="+clock.ElapsedMilliseconds);
   return path;
  }finally{try{p.Close();}catch{}}
 }
 // Called once the transition commits: Windows now draws this display.
 void CommitStatic(Forms.Screen screen,Item item,string image){
  SystemWallpaper.Set(image,screen);
  int interval=RefreshSeconds(item);
  statics[screen.DeviceName]=new StaticDisplay(){Item=item,Image=image,IntervalSeconds=interval,Next=interval>0?DateTime.UtcNow.AddSeconds(interval):DateTime.MaxValue};
  CleanSnapshots(screen.DeviceName,image);
  // An HTML snapshot doubles as the library preview when the page has none yet.
  if(!TestMode&&item.Type=="web"&&(String.IsNullOrEmpty(item.Preview)||!File.Exists(item.Preview))&&Path.GetFullPath(item.File).StartsWith(LibraryRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){
   try{string target=Path.ChangeExtension(item.File,".png");File.Copy(image,target,true);item.Preview=target;item.ResetThumb();var handler=PreviewSaved;if(handler!=null)handler(item);}catch(Exception e){TransitionLog("snapshot-preview failed: "+e.Message);}
  }
 }
 // Older snapshots of this display; the one Windows currently uses is kept.
 void CleanSnapshots(string device,string keep){
  try{if(!Directory.Exists(SnapshotDir))return;
   foreach(var file in Directory.GetFiles(SnapshotDir,DeviceKey(device)+"-*.png"))if(!String.Equals(file,keep,StringComparison.OrdinalIgnoreCase))try{File.Delete(file);}catch{}
  }catch{}
 }
 // Every 2 s with the other timers. Snapshots follow the same rules as live players: nothing is
 // rendered while paused, locked or while a window covers the desktop.
 void StaticTick(){
  if(statics.Count==0||transitioning||swapping||exiting||manualPause||locked||coveredNow)return;
  var now=DateTime.UtcNow;
  foreach(var pair in statics.ToArray()){var s=pair.Value;if(!s.Busy&&s.IntervalSeconds>0&&now>=s.Next)RefreshStatic(pair.Key,s);}
 }
 async void RefreshStatic(string device,StaticDisplay s){
  var screen=Forms.Screen.AllScreens.FirstOrDefault(x=>x.DeviceName==device);if(screen==null)return;
  s.Busy=true;
  try{
   string image=await RenderSnapshot(s.Item,screen);
   StaticDisplay current;
   if(exiting||!statics.TryGetValue(device,out current)||current!=s){try{File.Delete(image);}catch{}return;}
   SystemWallpaper.Set(image,screen);s.Image=image;CleanSnapshots(device,image);
  }catch(Exception e){TransitionLog("snapshot-refresh-failed "+e.Message);Say("网页快照刷新失败："+e.Message);}
  finally{s.Busy=false;s.Next=DateTime.UtcNow.AddSeconds(Math.Max(PresentPolicy.MinSnapshotSeconds,s.IntervalSeconds));}
 }
 // Waits for the static displays of one transition, then lets it finish (or rolls it back).
 async void WhenStaticReady(Task<string>[] work,int version,Action ready){
  try{await Task.WhenAll(work);}
  catch(Exception e){
   if(version!=transitionVersion)return;
   var error=work.Where(t=>t.IsFaulted).Select(t=>t.Exception.GetBaseException().Message).FirstOrDefault()??e.Message;
   CancelTransition();Say("静态壁纸准备失败，已保留原壁纸："+error);return;
  }
  if(version==transitionVersion)ready();
 }
 // An image of what a display shows now, for colour picking (fence palettes): the static image, a frame of
 // the live player, the item preview, or the Windows wallpaper. Null when nothing is known.
 public async Task<string> ThemeImageAsync(Forms.Screen screen,string framePath){
  StaticDisplay s;if(statics.TryGetValue(screen.DeviceName,out s)&&File.Exists(s.Image))return s.Image;
  var p=players.FirstOrDefault(x=>x.Monitor!=null&&x.Monitor.DeviceName==screen.DeviceName&&!x.IsClosed&&x.Ready);
  if(p!=null){try{await p.Snapshot(framePath);if(File.Exists(framePath))return framePath;}catch{}}
  Item item;if(layout.TryGetValue(screen.DeviceName,out item)&&item!=null&&!String.IsNullOrEmpty(item.Preview)&&File.Exists(item.Preview))return item.Preview;
  try{var system=SystemWallpaper.Get(screen);if(!String.IsNullOrEmpty(system)&&File.Exists(system))return system;}catch{}
  return null;
 }
}
}
