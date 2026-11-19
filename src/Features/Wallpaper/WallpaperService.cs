using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AWall {
// Wallpaper playback controller (docs/ARCHITECTURE.md §5): plans per display, transitions with covers
// and rollback, PKG swap, standby players, pause rules, desktop recovery, rotation and the saved layout.
// It knows nothing about the main window: the UI calls its methods (through the command table) and
// redraws on its events. All members run on the UI thread that created it.
public partial class WallpaperService {
 readonly Dispatcher dispatcher=Dispatcher.CurrentDispatcher;
 readonly string data=AppPaths.Data;

 // ---- Set by the host before Start()
 public bool TestMode;
 // The library list owned by the UI. Restoring a layout may add items to it (then LibraryChanged fires).
 public Func<List<Item>> Library;
 // Folder whose HTML wallpapers may receive an automatic preview image.
 public string LibraryRoot=AppPaths.DefaultWallpaperRoot;

 // ---- Events for the UI
 public event Action<string> Status;          // a line for the status bar / AWall.log
 public event Action Changed;                 // playback state changed (now playing, pause, loading)
 public event Action RotationChanged;         // rotation settings or list changed
 public event Action DisplaysChanged;         // monitor topology changed
 public event Action LibraryChanged;          // items were added to the library list
 public event Action<Item> PreviewSaved;      // an automatic preview image was written for this item
 public event Action<Item> Restoring;         // a saved layout is being restored; argument: primary item

 // ---- Playback state
 List<Player> players=new List<Player>(),previousPlayers=new List<Player>();
 bool transitioning,fading,previousManualPause,swapping,manualPause,locked,coveredNow,exiting,recoveryBusy,pauseWhenCovered=true;
 DispatcherTimer transitionTimer,fadeTimer,timer,pointerTimer;
 List<TransitionCover> covers=new List<TransitionCover>();
 int transitionVersion,recoveryAttempts;
 Dictionary<string,Item> recoveryPlan,pendingPlan;
 DateTime nextRecovery;
 Stopwatch transitionClock;double preparationMs;
 // Wallpaper per display, keyed by Screen.DeviceName. While a transition runs this is the target;
 // previousLayout is what CancelTransition restores. Displays whose wallpaper does not change keep
 // their running player (kept) instead of being rebuilt.
 Dictionary<string,Item> layout=new Dictionary<string,Item>(),previousLayout=new Dictionary<string,Item>();
 HashSet<Player> kept=new HashSet<Player>(),announced=new HashSet<Player>(); Action finishTransition;
 string topology=ScreenTopology();

 // ---- Read-only state for the UI and commands
 // The primary display's wallpaper: now-playing text, lock-screen snapshot, auto preview.
 public Item Current {get {return PrimaryItem(layout);}}
 public IReadOnlyDictionary<string,Item> Layout {get {return layout;}}
 public bool Paused {get {return manualPause;}}
 public bool Loading {get {return transitioning||swapping;}}
 // Anything shown, loading or being replaced.
 public bool Active {get {return Current!=null||players.Count>0||previousPlayers.Count>0||transitioning||swapping;}}
 public bool HasPlayers {get {return players.Count>0;}}
 public bool HasVideo {get {return players.Any(p=>p.Item!=null&&p.Item.Type=="video");}}
 public bool PauseWhenCovered {get {return pauseWhenCovered;}}

 // ---- For the self-tests (Diagnostics)
 internal List<Player> Players {get {return players;}}
 internal List<Player> PreviousPlayers {get {return previousPlayers;}}
 internal List<TransitionCover> Covers {get {return covers;}}
 internal bool Transitioning {get {return transitioning;}}
 internal Dictionary<string,Item> RecoveryPlan {set {recoveryPlan=value;}}

 List<Item> Items {get {var source=Library;var list=source==null?null:source();return list??new List<Item>();}}
 void Say(string message){var handler=Status;if(handler!=null)handler(message);}
 void Notify(){var handler=Changed;if(handler!=null)handler();}

 public void TransitionLog(string phase){try{File.AppendAllText(Path.Combine(data,"transitions.log"),DateTime.Now.ToString("s")+" version="+transitionVersion+" phase="+phase+" elapsedMs="+(transitionClock==null?0:transitionClock.ElapsedMilliseconds)+Environment.NewLine);}catch{}}

 // ---- Lifecycle
 public void Start(){
  SystemEvents.SessionSwitch+=SessionChanged;SystemEvents.DisplaySettingsChanged+=DisplayChanged;
  timer=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(2)};timer.Tick+=delegate{Tick();RotationTick();StaticTick();};timer.Start();
  pointerTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(50)};pointerTimer.Tick+=delegate{ForwardPointer();};pointerTimer.Start();
 }
 // Self-tests drive playback themselves.
 public void StopTimers(){if(timer!=null)timer.Stop();if(pointerTimer!=null)pointerTimer.Stop();}
 // App exit: keep the saved layout for the next start, close every window.
 public void Shutdown(){
  exiting=true;StopTimers();SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;
  SaveLayout();StopPlayback(false);CloseStandbys();
 }
 void ForwardPointer(){
  foreach(var p in previousPlayers.Concat(players)){if(p.Scene!=null){var cursor=Forms.Cursor.Position;var b=p.Monitor.Bounds;if(b.Contains(cursor))p.Scene.Pointer((double)(cursor.X-b.Left)/b.Width,(double)(cursor.Y-b.Top)/b.Height);}}
 }

 // ---- Display helpers
 public static bool SameItem(Item a,Item b){return a!=null&&b!=null&&a.Type==b.Type&&String.Equals(a.File,b.File,StringComparison.OrdinalIgnoreCase);}
 public static string ItemKey(Item item){return item==null?"":item.Type+"|"+(item.File??"").ToLowerInvariant();}
 // Displays numbered left to right, as in the "应用到" row and the display=1..n command argument.
 public static Forms.Screen[] OrderedScreens(){return Forms.Screen.AllScreens.OrderBy(x=>x.Bounds.Left).ThenBy(x=>x.Bounds.Top).ToArray();}
 static string ScreenTopology(){return String.Join(";",Forms.Screen.AllScreens.Select(x=>x.DeviceName+":"+x.Bounds+":"+x.Primary));}
 static Item PrimaryItem(Dictionary<string,Item> plan){
  if(plan==null||plan.Count==0)return null;Item item;
  foreach(var s in Forms.Screen.AllScreens.OrderByDescending(x=>x.Primary).ThenBy(x=>x.Bounds.Left))if(plan.TryGetValue(s.DeviceName,out item)&&item!=null)return item;
  return plan.Values.FirstOrDefault(x=>x!=null);
 }
 // Every current display gets an item: its own entry, otherwise the plan's primary item.
 static Dictionary<string,Item> CompletePlan(Dictionary<string,Item> plan,Forms.Screen[] screens){
  var result=new Dictionary<string,Item>();var fallback=PrimaryItem(plan);if(fallback==null)return result;
  foreach(var s in screens){Item item;result[s.DeviceName]=plan.TryGetValue(s.DeviceName,out item)&&item!=null?item:fallback;}
  return result;
 }
 internal static Dictionary<string,Item> Uniform(Item item){var plan=new Dictionary<string,Item>();foreach(var s in Forms.Screen.AllScreens)plan[s.DeviceName]=item;return plan;}
 static Dictionary<string,Item> LayoutOf(IEnumerable<Player> list){var result=new Dictionary<string,Item>();foreach(var p in list)if(p.Item!=null&&!p.IsClosed)result[p.Monitor.DeviceName]=p.Item;return result;}
 // The chosen display gets `item`; the others keep what they show now (or get `item` if empty).
 Dictionary<string,Item> PlanFor(Item item,string target){
  var screens=Forms.Screen.AllScreens;if(target!=null&&!screens.Any(s=>s.DeviceName==target))target=null;
  var plan=new Dictionary<string,Item>();
  foreach(var s in screens){Item keep;plan[s.DeviceName]=target!=null&&s.DeviceName!=target&&layout.TryGetValue(s.DeviceName,out keep)&&keep!=null?keep:item;}
  return plan;
 }
 bool Healthy(Player p,Forms.Screen[] screens){return !p.IsClosed&&p.Ready&&!p.Failed&&screens.Any(s=>s.DeviceName==p.Monitor.DeviceName&&s.Bounds==p.Monitor.Bounds);}
 // True when every display already shows exactly this plan: live displays by a healthy player,
 // static ones by the image AWall handed to Windows.
 bool PlayingPlan(Dictionary<string,Item> plan){
  var screens=Forms.Screen.AllScreens;if(!screens.All(s=>plan.ContainsKey(s.DeviceName)&&plan[s.DeviceName]!=null))return false;
  var live=screens.Where(s=>IsLive(plan[s.DeviceName])).ToArray();
  return players.Count==live.Length&&players.All(p=>Healthy(p,screens)&&live.Any(s=>s.DeviceName==p.Monitor.DeviceName)&&SameItem(p.Item,plan[p.Monitor.DeviceName]))
   &&screens.Where(s=>!IsLive(plan[s.DeviceName])).All(s=>StaticShows(s.DeviceName,plan[s.DeviceName]));
 }
 static string PlanTitle(Dictionary<string,Item> plan){var distinct=plan.Values.GroupBy(x=>ItemKey(x)).Select(g=>g.First()).ToList();return distinct.Count==1?distinct[0].Title:distinct.Count+" 个壁纸";}

 // ---- Applying wallpapers
 // A user's choice ("设为动态桌面", wallpaper.apply): also cancels pending recovery and restarts the rotation clock.
 public async Task ApplyChosenAsync(Item item,string target){
  if(item==null)return;recoveryPlan=null;recoveryAttempts=0;ResetRotationClock();
  await ApplyPlanAsync(false,PlanFor(item,target));
 }
 // `target` null = every display, otherwise a Screen.DeviceName.
 public async Task ApplyItemAsync(Item item,string target,bool force=false){if(item==null)return;await ApplyPlanAsync(force,PlanFor(item,target));}
 // Rebuild every display with what it shows now (e.g. after switching the MP4 backend).
 public async Task ReloadAsync(){await ApplyPlanAsync(true,new Dictionary<string,Item>(layout));}
 // Shows plan[display] on every display. Displays whose wallpaper does not change keep their running
 // player (unless forced); the others get new players that stay off-screen until all are ready, behind
 // a cover of the old frame that then fades out. Any failure restores the previous wallpapers.
 async Task ApplyPlanAsync(bool force,Dictionary<string,Item> requestedPlan){
  var screens=Forms.Screen.AllScreens;var plan=CompletePlan(requestedPlan,screens);if(plan.Count==0)return;
  foreach(var check in plan.Values.Distinct()){if(!check.Supported){Say("不支持的素材类型："+check.Type);return;}if(!File.Exists(check.File))throw new Exception("素材入口文件不存在："+check.Title);}
  if(!force&&!transitioning&&PlayingPlan(plan)){Say("当前壁纸已在播放。");return;}
  // PKG -> PKG: swap the scene inside the already running page instead of building a new
  // WebView2. While a swap runs, remember only the latest requested plan and apply it after.
  if(!force&&swapping){if(plan.Values.All(x=>x.Type=="scene")){pendingPlan=plan;Say("正在切换中，稍后加载："+PlanTitle(plan));return;}pendingPlan=null;}
  if(!force&&!transitioning&&!swapping&&players.Count==screens.Length&&players.All(p=>Healthy(p,screens))){
   var changed=players.Where(p=>plan.ContainsKey(p.Monitor.DeviceName)&&!SameItem(p.Item,plan[p.Monitor.DeviceName])).ToList();
   if(changed.Count>0&&changed.All(p=>p.Scene.CanSwap&&plan[p.Monitor.DeviceName].Type=="scene")){await SwapAsync(changed,plan);return;}
  }
  CancelTransition();
  transitionClock=Stopwatch.StartNew();preparationMs=0;TransitionLog("begin");
  int version=++transitionVersion;previousManualPause=manualPause;
  // Live displays get players; the others are drawn by Windows (WallpaperService.Static.cs).
  var liveScreens=screens.Where(s=>IsLive(plan[s.DeviceName])).ToArray();
  var staticScreens=screens.Where(s=>!IsLive(plan[s.DeviceName])).ToArray();
  var keep=new HashSet<Player>();
  if(!force&&!swapping)foreach(var p in players)if(Healthy(p,screens)&&Native.IsWindow(p.Handle)&&plan.ContainsKey(p.Monitor.DeviceName)&&IsLive(plan[p.Monitor.DeviceName])&&SameItem(p.Item,plan[p.Monitor.DeviceName]))keep.Add(p);
  var fresh=liveScreens.Where(s=>!keep.Any(p=>p.Monitor.DeviceName==s.DeviceName)).ToArray();
  var freshStatic=staticScreens.Where(s=>force||!StaticShows(s.DeviceName,plan[s.DeviceName])).ToArray();
  var changing=fresh.Concat(freshStatic).ToArray();
  if(keep.Count>0)TransitionLog("kept "+keep.Count+" display(s)");
  if(staticScreens.Length>0)TransitionLog("static "+staticScreens.Length+" display(s), "+freshStatic.Length+" to set");
  previousPlayers=players.Where(p=>!keep.Contains(p)).ToList();previousLayout=layout;
  players=new List<Player>(keep);layout=plan;kept=keep;announced=new HashSet<Player>();manualPause=false;transitioning=true;
  var batch=players;bool coversReady=false;int liveCount=liveScreens.Length;
  var staticWork=new Task<string>[0];bool staticDone=true;
  Action finish=delegate{if(!coversReady||!staticDone||version!=transitionVersion||players!=batch||!transitioning||fading||batch.Count!=liveCount||!batch.All(x=>x.Ready))return;try{TransitionLog("all-content-ready");
    // Static displays: hand the image to Windows, then drop the old player under the cover so the fade reveals it.
    for(int i=0;i<freshStatic.Length;i++){var screen=freshStatic[i];CommitStatic(screen,plan[screen.DeviceName],staticWork[i].Result);foreach(var old in previousPlayers.Where(x=>x.Monitor.DeviceName==screen.DeviceName).ToArray()){previousPlayers.Remove(old);old.Close();}}
    if(freshStatic.Length>0)TransitionLog("system-wallpaper "+freshStatic.Length+" display(s)");
    foreach(var s in liveScreens)statics.Remove(s.DeviceName);
    foreach(var ready in batch)if(!keep.Contains(ready))ready.Present();TransitionLog("attached");foreach(var cover in covers)cover.Raise();StopTransitionTimer();BeginFade(batch,version);}catch(Exception e){CancelTransition();Say("桌面挂载失败，已恢复原壁纸："+e.Message);}};
  finishTransition=finish;
  transitionTimer=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(60)};
  transitionTimer.Tick+=delegate{if(transitioning&&players==batch){CancelTransition();Say("壁纸加载超时，已保留原壁纸。");}};transitionTimer.Start();
  try{
   // Start the old-frame capture, then immediately start the replacement
   // backends in their off-screen staging windows. They are never presented
   // before coversReady, so the visual barrier is unchanged, but WebView2
   // browser startup / mpv startup now overlaps capture + cover setup.
   TransitionLog("capture-start");var oldBatch=previousPlayers.ToArray();
   var capture=Task.WhenAll(changing.Select(screen=>CaptureBackdrop(screen,oldBatch)).ToArray());
   if(freshStatic.Length>0){staticDone=false;staticWork=freshStatic.Select(s=>PrepareStatic(plan[s.DeviceName],s)).ToArray();WhenStaticReady(staticWork,version,delegate{staticDone=true;finish();});}
   TransitionLog("backends-start");
   foreach(var screen in fresh){var wanted=plan[screen.DeviceName];var p=TakeStandby(wanted,screen)??new Player(wanted,screen);Hook(p);batch.Add(p);p.Show();}
   // With a pre-started standby the new scene is often ready before the old
   // frame capture finishes (mpv/WebView capture can take ~1s). Do not let the
   // cover gate the switch: once all new content has been ready for 100ms and
   // the capture is still pending, present with a direct cut instead of a fade.
   long readyAt=-1;
   while(!capture.IsCompleted){
    if(version!=transitionVersion)return;
    if(staticDone&&batch.Count==liveCount&&batch.All(x=>x.Ready)){if(readyAt<0)readyAt=transitionClock.ElapsedMilliseconds;else if(transitionClock.ElapsedMilliseconds-readyAt>=100)break;}
    await Task.WhenAny(capture,Task.Delay(25));
   }
   if(version!=transitionVersion)return;
   if(capture.IsCompleted&&!capture.IsFaulted&&!capture.IsCanceled){
    var frames=capture.Result;TransitionLog("capture-done");
    for(int i=0;i<changing.Length;i++){var cover=new TransitionCover(changing[i],frames[i]);covers.Add(cover);cover.Show();}
    await dispatcher.InvokeAsync(delegate{},DispatcherPriority.Render);await Task.Delay(50);if(version!=transitionVersion)return;
   }else{TransitionLog("covers-skipped capture-pending");capture.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
   foreach(var old in oldBatch)old.Pause(true);
   preparationMs=transitionClock.Elapsed.TotalMilliseconds;TransitionLog("covers-ready");
   coversReady=true;finish();
   var shown=changing.Length>0?plan[changing[0].DeviceName]:PrimaryItem(plan);var route=RouteOf(shown);
   Say((route==PresentRoute.SystemWallpaper?"正在设为系统壁纸：":route==PresentRoute.SnapshotToSystemWallpaper?"正在渲染网页快照：":shown.Type=="scene"?"正在加载 PKG：":shown.Type=="web"?"正在加载网页：":"正在加载 MP4：")+shown.Title+(screens.Length>1&&changing.Length<screens.Length?"（"+changing.Length+" 块显示器）":"")+"。锁屏图片需点击“同步锁屏快照”。");}catch{if(version!=transitionVersion)return;CancelTransition();throw;}
 }
 void Hook(Player p){p.Report=delegate(string message){PlayerReport(p,message);};}
 // Reports from any live player (kept or new). A failure during a transition rolls back; a failure
 // afterwards stops the wallpaper.
 void PlayerReport(Player p,string message){
  if(!players.Contains(p))return;
  if(p.Ready&&announced.Add(p))TransitionLog("backend-ready "+p.Monitor.DeviceName);
  Say(message);
  dispatcher.BeginInvoke(new Action(delegate{
   if(!players.Contains(p))return;
   var failed=players.FirstOrDefault(x=>x.Failed);
   if(failed!=null){string error=failed.LastError;if(transitioning){CancelTransition();Say("壁纸加载失败，已保留原壁纸："+error);}else{Stop();Say("壁纸播放器已退出："+error);}return;}
   var next=finishTransition;if(transitioning&&next!=null)next();
  }),DispatcherPriority.ContextIdle);
 }
 async Task<BitmapSource> CaptureBackdrop(Forms.Screen screen,Player[] old){
  var p=old.FirstOrDefault(x=>x.Monitor.DeviceName==screen.DeviceName&&x.Ready&&!x.Failed&&Native.IsWindow(x.Handle));
  if(p!=null)try{return await p.Scene.CaptureFrame();}catch(Exception e){TransitionLog("capture-fallback: "+e.Message);}
  return StartupBackdrop.Capture(screen);
 }
 void BeginFade(List<Player> batch,int version){
  TransitionLog("fade-start");fading=true;var elapsed=Stopwatch.StartNew();fadeTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(15)};
  fadeTimer.Tick+=delegate{
   if(version!=transitionVersion||players!=batch)return;
   try{
    if(batch.Any(x=>x.Failed)){CancelTransition();Say("新壁纸播放失败，已恢复原壁纸。");return;}
    double progress=Math.Min(1,elapsed.Elapsed.TotalMilliseconds/350.0);
    foreach(var cover in covers)cover.SetAlpha((byte)Math.Round(255*(1-progress)));
    if(progress<1&&covers.Count>0)return;
    CloseCovers();transitioning=false;StopFade();var old=previousPlayers;previousPlayers=new List<Player>();previousLayout=new Dictionary<string,Item>();kept=new HashSet<Player>();finishTransition=null;
    foreach(var retired in old)retired.Close();Tick();recoveryPlan=null;recoveryAttempts=0;SaveLayout();TransitionLog("complete");ScheduleStandby();Say("壁纸切换完成。");AutoPreview();
   }catch(Exception e){CancelTransition();Say("壁纸切换失败，已恢复原壁纸："+e.Message);}
  };fadeTimer.Start();
 }
 // PKG startup cost is dominated by WebView2: a new browser process (~1s) plus
 // loading index.html and the 1.1MB engine script (~1s). Keep one pre-started
 // player per monitor parked off-screen at "boot" so the next PKG switch only
 // pays for mounting the scene. It uses its own profile slot, so it never
 // shares a browser process with the live wallpaper. Costs one idle WebView2.
 Dictionary<string,Player> standbys=new Dictionary<string,Player>();
 Player TakeStandby(Item item,Forms.Screen screen){
  if(item.Type!="scene")return null;Player p;
  if(!standbys.TryGetValue(screen.DeviceName,out p))return null;standbys.Remove(screen.DeviceName);
  if(p.IsClosed||p.Failed||!p.Scene.Standby||p.Monitor.Bounds!=screen.Bounds){try{p.Close();}catch{}return null;}
  try{p.Assign(item);TransitionLog("standby-used "+screen.DeviceName);return p;}catch{try{p.Close();}catch{}return null;}
 }
 async Task SwapAsync(List<Player> targets,Dictionary<string,Item> plan){
  swapping=true;int version=++transitionVersion;var batch=players;
  transitionClock=Stopwatch.StartNew();preparationMs=0;TransitionLog("swap-begin "+targets.Count+" display(s)");
  Say("正在加载 PKG："+plan[targets[0].Monitor.DeviceName].Title+"（同页切换）。");
  bool[] results;
  try{results=await Task.WhenAll(targets.Select(p=>p.Scene.Swap(plan[p.Monitor.DeviceName])).ToArray());}
  finally{swapping=false;}
  var next=pendingPlan;pendingPlan=null;
  if(version!=transitionVersion||players!=batch)return;
  for(int i=0;i<targets.Count;i++)if(results[i])targets[i].Item=plan[targets[i].Monitor.DeviceName];
  layout=WithStatics(LayoutOf(players));
  if(results.All(x=>x)){manualPause=false;Tick();SaveLayout();TransitionLog("swap-complete");Say("壁纸切换完成。");}
  else if(results.Any(x=>x)){TransitionLog("swap-partial rebuild");await ApplyPlanAsync(true,plan);return;}
  else{string error=String.Join("；",targets.Select(p=>p.Scene.LastSwapError).Where(x=>!String.IsNullOrEmpty(x)).Distinct().ToArray());TransitionLog("swap-failed "+error);Say("壁纸加载失败，已保留原壁纸："+error);}
  if(next!=null&&!PlayingPlan(next))await ApplyPlanAsync(false,next);
 }
 // After a library scan or a finished switch: pre-start the PKG standby players.
 public void ScheduleStandby(){
  if(TestMode||exiting||!Items.Any(x=>x.Type=="scene"))return;
  // Let the wallpaper that just appeared settle before starting another browser.
  var delay=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(500)};delay.Tick+=delegate{delay.Stop();PrepareStandby();};delay.Start();
 }
 void PrepareStandby(){
  if(TestMode||exiting||transitioning)return;
  // Only images / snapshots on screen: Windows draws everything, so do not keep an idle browser around.
  // The next PKG switch then starts cold.
  if(players.Count==0&&statics.Count>0){if(standbys.Count>0){CloseStandbys();TransitionLog("standby-closed static-only");}return;}
  var screens=Forms.Screen.AllScreens;
  foreach(var key in standbys.Keys.ToArray()){var old=standbys[key];if(!old.IsClosed&&!old.Failed&&screens.Any(s=>s.DeviceName==key&&s.Bounds==old.Monitor.Bounds))continue;standbys.Remove(key);try{old.Close();}catch{}}
  foreach(var screen in screens){if(standbys.ContainsKey(screen.DeviceName))continue;
   try{var p=new Player(null,screen);standbys[screen.DeviceName]=p;p.Show();TransitionLog("standby-start "+screen.DeviceName);}catch(Exception e){TransitionLog("standby-failed "+e.Message);}}
 }
 void CloseStandbys(){foreach(var p in standbys.Values.ToArray()){try{p.Close();}catch{}}standbys.Clear();}
 void StopFade(){if(fadeTimer!=null){fadeTimer.Stop();fadeTimer=null;}fading=false;}
 void CloseCovers(){var old=covers;covers=new List<TransitionCover>();foreach(var cover in old)cover.Close();}
 void StopTransitionTimer(){if(transitionTimer!=null){transitionTimer.Stop();transitionTimer=null;}}
 void CancelTransition(){
  if(!transitioning)return;TransitionLog("rollback");++transitionVersion;transitioning=false;finishTransition=null;StopTransitionTimer();StopFade();
  var pending=players;var restore=previousPlayers;var keep=kept;players=restore;manualPause=previousManualPause;
  previousPlayers=new List<Player>();previousLayout=new Dictionary<string,Item>();kept=new HashSet<Player>();
  try{foreach(var p in restore.ToArray()){try{if(!p.Ready||p.Failed||!Native.IsWindow(p.Handle))throw new InvalidOperationException("旧播放器已失效");p.Present();}catch(Exception e){TransitionLog("rollback-unavailable: "+e.Message);players.Remove(p);if(!p.IsClosed)p.Close();}}}
  finally{foreach(var p in pending)if(!keep.Contains(p)&&!p.IsClosed)p.Close();CloseCovers();}
  // Kept players never stopped showing their wallpaper; they simply stay.
  foreach(var p in keep){if(p.IsClosed||p.Failed){if(!p.IsClosed)p.Close();}else players.Add(p);}
  // Static displays only change when a transition commits, so they still show what `statics` says.
  layout=WithStatics(LayoutOf(players));
  Tick();
 }

 // ---- Stop, pause
 // "停止壁纸": close everything and forget the saved layout.
 public void Stop(){if(Current==null&&players.Count==0&&previousPlayers.Count==0&&!transitioning&&!swapping){Notify();return;}StopPlayback(true);}
 internal void StopPlayback(bool clearLayout){recoveryPlan=null;recoveryAttempts=0;++transitionVersion;transitioning=false;finishTransition=null;StopTransitionTimer();StopFade();CloseCovers();var all=previousPlayers.Concat(players).Distinct().ToArray();players=new List<Player>();previousPlayers=new List<Player>();layout=new Dictionary<string,Item>();previousLayout=new Dictionary<string,Item>();kept=new HashSet<Player>();statics.Clear();foreach(var p in all)p.Close();if(clearLayout)try{File.Delete(LayoutPath);}catch(Exception e){Say("清除壁纸布局失败："+e.Message);}Say("动态壁纸已停止。");}
 public void SetPaused(bool value){manualPause=value;Tick();SaveLayout();}
 public void SetPauseWhenCovered(bool value){pauseWhenCovered=value;SaveLayout();}

 // ---- Every 2 s: pause rules, desktop health, recovery
 internal void Tick(){try{bool covered=false;IntPtr fg=Native.GetForegroundWindow();var cls=new StringBuilder(256);Native.GetClassName(fg,cls,256);Native.Rect r; if(pauseWhenCovered&&Native.GetWindowRect(fg,out r)&&!Native.IsIconic(fg)&&cls.ToString()!="Progman"&&cls.ToString()!="WorkerW")foreach(var s in Forms.Screen.AllScreens){var b=s.Bounds;if(r.Left<=b.Left&&r.Top<=b.Top&&r.Right>=b.Right&&r.Bottom>=b.Bottom)covered=true;}
  if(!transitioning&&(players.Any(p=>!Native.IsWindow(p.Handle))||(recoveryPlan!=null&&players.Count==0&&recoveryAttempts<5&&DateTime.UtcNow>=nextRecovery))){RebuildDesktop();return;}
  foreach(var p in previousPlayers.Concat(players).Where(p=>Native.IsWindow(p.Handle))){if(p.ParentHandle!=IntPtr.Zero&&!DesktopHost.IsHealthy(p.ParentHandle))p.Attach();if(p.Ready){p.Pause((transitioning&&covers.Count>0&&previousPlayers.Contains(p))||manualPause||locked||covered);}}
  foreach(var cover in covers){if(!DesktopHost.IsHealthy(cover.ParentHandle))cover.Attach();cover.Raise();}
  coveredNow=covered;
  Notify();
 }catch(Exception e){Say("桌面恢复失败："+e.Message);}}
 // Explorer restarted (TaskbarCreated): re-mount every display.
 public void ShellRestarted(){dispatcher.BeginInvoke(new Action(delegate{recoveryAttempts=0;nextRecovery=DateTime.MinValue;RebuildDesktop();}));}
 async void RebuildDesktop(){await RebuildDesktopAsync();}
 internal async Task RebuildDesktopAsync(){
  if(exiting||recoveryBusy)return;var plan=layout.Count>0?new Dictionary<string,Item>(layout):recoveryPlan;if(plan==null||plan.Count==0)return;
  recoveryPlan=plan;recoveryBusy=true;bool keepPause=manualPause;int version=transitionVersion;
  try{DesktopHost.Invalidate();var pending=ApplyPlanAsync(true,plan);version=transitionVersion;await pending;
   if(version==transitionVersion&&layout.Count>0){manualPause=keepPause;Tick();}
  }catch(Exception e){
   if(version==transitionVersion||Current==null){++recoveryAttempts;nextRecovery=DateTime.UtcNow.AddSeconds(Math.Min(10,recoveryAttempts*2));Say("等待 Windows 桌面恢复："+e.Message);}
  }finally{recoveryBusy=false;}
 }
 internal async Task WaitForTransition(){var started=Stopwatch.StartNew();while(transitioning&&started.Elapsed.TotalSeconds<65)await Task.Delay(25);if(transitioning)throw new Exception("切换未结束");}
 void SessionChanged(object s,SessionSwitchEventArgs e){dispatcher.BeginInvoke(new Action(delegate{locked=e.Reason==SessionSwitchReason.SessionLock;if(e.Reason==SessionSwitchReason.SessionUnlock)locked=false;Tick();}));}
 void DisplayChanged(object s,EventArgs e){dispatcher.BeginInvoke(new Action(delegate{string next=ScreenTopology();if(next!=topology){topology=next;var handler=DisplaysChanged;if(handler!=null)handler();if(Current!=null)RebuildDesktop();}}));}

 // ---- Things done with the playing wallpaper
 public async void SyncLock(){if(!CanSyncLock){Say("请先加载壁纸，再同步锁屏快照。");return;}try{
   // A static primary display already has the image Windows shows; otherwise capture a player's frame.
   var primary=Forms.Screen.PrimaryScreen;string image=primary==null?null:StaticImage(primary.DeviceName);
   if(image==null){if(players.Count==0)image=statics.Values.First().Image;else{var p=players.FirstOrDefault(x=>x.Monitor.Primary)??players[0];image=await p.Snapshot(Path.Combine(data,"lock-"+Guid.NewGuid().ToString("N")+".jpg"));}}Say("正在保存锁屏快照…");string result=Path.Combine(data,"lock-result.txt");int code=await LockScreen.Apply(image,result);Say(code==0?"锁屏静态快照已同步。Win+L 后显示图片，不播放视频。":"锁屏设置失败，详情见 "+result);}catch(Exception e){Say(e.Message);}}
 // A parameter of an HTML wallpaper changed: apply it live wherever that wallpaper plays.
 public void SetWebProperty(Item item,string name,object value){foreach(var p in players)if(p.Scene!=null&&SameItem(p.Item,item))p.Scene.SetProperty(name,value);}
 // HTML wallpapers arrive without a preview: save the primary monitor's frame as <name>.jpg
 // after the first successful play. Only for files inside the library, never the user's own folders.
 async void AutoPreview(){
  var item=Current;if(TestMode||item==null||item.Type!="web"||players.Count==0)return;
  if(!String.IsNullOrEmpty(item.Preview)&&File.Exists(item.Preview))return;
  if(!Path.GetFullPath(item.File).StartsWith(LibraryRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
  try{
   await Task.Delay(1500);// let intro animations settle
   if(Current!=item||transitioning)return;
   var p=players.FirstOrDefault(x=>x.Monitor.Primary)??players[0];if(!p.Ready||p.Failed||!SameItem(p.Item,item))return;
   string target=Path.ChangeExtension(item.File,".jpg");
   await p.Snapshot(target);item.Preview=target;item.ResetThumb();
   var handler=PreviewSaved;if(handler!=null)handler(item);
  }catch(Exception e){TransitionLog("auto-preview failed: "+e.Message);}
 }
}
}
