using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AWall {
public sealed class ScenePlayer : IDisposable {
 // The property alone is applied too late for the browser's earliest surface.
 static ScenePlayer(){Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR","FF000000",EnvironmentVariableTarget.Process);}
 static readonly HashSet<string> activeProfiles=new HashSet<string>();
 // Set this before creating the native controller, including its initial about:blank.
 public readonly WebView2 View = new WebView2(){DefaultBackgroundColor=System.Drawing.Color.Black};
 public bool Ready; public bool Failed; public int Warnings; public string LastError="";
 public string ReadyInfo=""; public event Action<string> Report;
 bool disposed,pointerBusy,pauseBusy; bool? paused,appliedPause; string log,profile; Item item; readonly HashSet<string> warnings=new HashSet<string>();
 readonly TaskCompletionSource<bool> closed=new TaskCompletionSource<bool>();
 bool IsVideo {get {return item.Type=="video";}}
 string Kind {get {return IsVideo?"MP4":"PKG";}}
 const string AppHost="awall.local", MediaHost="wallpaper.awall.local";
 const string Assets="E:\\Steam\\steamapps\\common\\wallpaper_engine\\assets";
 // Shared Wallpaper Engine assets are immutable for the life of the process.
 // Enumerating materials\*.tex recursively and reading TEX files used to run
 // synchronously on the UI thread for every player and every switch, which
 // stalled all WebView2 callbacks (including other monitors) during PKG load.
 static readonly object assetLock=new object();
 static Task<byte[]> materialIndex;
 static readonly Dictionary<string,byte[]> assetCache=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
 static long assetCacheBytes; const long AssetCacheLimit=256L*1024*1024;
 static readonly Dictionary<string,Task> pkgWarmups=new Dictionary<string,Task>(StringComparer.OrdinalIgnoreCase);
 static Task<byte[]> MaterialIndex(){
  lock(assetLock){
   if(materialIndex==null||materialIndex.IsFaulted)materialIndex=Task.Run(delegate{
    var dir=Path.Combine(Assets,"materials");
    var names=Directory.Exists(dir)?Directory.GetFiles(dir,"*.tex",SearchOption.AllDirectories).Select(x=>x.Substring(dir.Length+1).Replace('\\','/')).Select(x=>x.Substring(0,x.Length-4)).ToArray():new string[0];
    return Encoding.UTF8.GetBytes(new JavaScriptSerializer(){MaxJsonLength=int.MaxValue}.Serialize(new{names=names}));
   });
   return materialIndex;
  }
 }
 static Task<byte[]> ReadAsset(string path){
  lock(assetLock){byte[] cached;if(assetCache.TryGetValue(path,out cached))return Task.FromResult(cached);}
  return Task.Run(delegate{
   if(!File.Exists(path))return (byte[])null;
   var bytes=File.ReadAllBytes(path);
   lock(assetLock){if(!assetCache.ContainsKey(path)&&assetCacheBytes+bytes.Length<=AssetCacheLimit){assetCache[path]=bytes;assetCacheBytes+=bytes.Length;}}
   return bytes;
  });
 }
 // Read the PKG once in the background while the WebView2 browser process is
 // still starting, so the later copy into shared memory comes from the OS file
 // cache. Players for several monitors share one warm-up read.
 static Task WarmPkg(string path){
  lock(pkgWarmups){
   Task task;if(pkgWarmups.TryGetValue(path,out task))return task;
   task=Task.Run(delegate{
    try{var chunk=new byte[1<<20];using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,1<<16,FileOptions.SequentialScan))while(f.Read(chunk,0,chunk.Length)>0){}}
    finally{lock(pkgWarmups)pkgWarmups.Remove(path);}
   });
   pkgWarmups[path]=task;return task;
  }
 }
 Task pkgWarm; CoreWebView2SharedBuffer pkgBuffer;
 bool IsPkg {get {return !IsVideo&&Path.GetExtension(item.File).Equals(".pkg",StringComparison.OrdinalIgnoreCase);}}
 public ScenePlayer(Item value,string display) {
  item=value;
  if(!IsVideo){MaterialIndex();if(IsPkg)pkgWarm=WarmPkg(item.File);}
  string key=new string(display.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
  int slot=0;
  // Adjacent wallpapers must not share a browser process: pausing the old
  // renderer can otherwise stall startup of the replacement. Reuse a free slot,
  // including after cancellation, without ever reusing the live player's slot.
  string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall","WebView2",key);
  do{profile=Path.Combine(folder,"slot-"+slot++);}while(activeProfiles.Contains(profile));
  activeProfiles.Add(profile);
  log=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall",(value.Type=="video"?"video-":"scene-")+Path.GetFileName(Path.GetDirectoryName(value.File))+".log");
 }
 void Write(string line){ try{File.AppendAllText(log,DateTime.Now.ToString("s")+" "+line+Environment.NewLine);}catch{} }
 void Response(CoreWebView2WebResourceRequestedEventArgs e,byte[] bytes,string mime,int code,string cache="no-store"){e.Response=View.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes),code,code==200?"OK":code==403?"Forbidden":"Not Found","Content-Type: "+mime+"\r\nAccess-Control-Allow-Origin: https://awall.local\r\nCache-Control: "+cache);}
 void Json(CoreWebView2WebResourceRequestedEventArgs e,object value){Response(e,Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)),"application/json",200);}
 void Resource(object s,CoreWebView2WebResourceRequestedEventArgs e){
  try{
   var uri=new Uri(e.Request.Uri);
   if(uri.Scheme=="https"&&(uri.Host==AppHost||uri.Host==MediaHost)){
    if(uri.Host!=AppHost||!uri.AbsolutePath.StartsWith("/api/local-assets"))return;
    string rel=Uri.UnescapeDataString(uri.AbsolutePath.Substring("/api/local-assets".Length)).TrimStart('/');
    if(rel==""){Json(e,new{ok=Directory.Exists(Assets),roots=new[]{new{id="we",dir="local"}}});return;}
    if(rel=="we/materials/index.json"){Deferred(e,MaterialIndex(),"application/json");return;}
    if(!rel.StartsWith("we/"))throw new IOException("Unknown assets root");
    var path=Path.GetFullPath(Path.Combine(Assets,rel.Substring(3).Replace('/',Path.DirectorySeparatorChar)));
    if(!path.StartsWith(Assets+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){Response(e,new byte[0],"text/plain",404);return;}
    Deferred(e,ReadAsset(path),"application/octet-stream");return;
   }
   if(uri.Scheme=="data"||uri.Scheme=="blob"||uri.Scheme=="about")return;
   Response(e,new byte[0],"text/plain",403);
  }catch{Response(e,new byte[0],"text/plain",404);}
 }
 // File work runs on the thread pool; only the response object is created
 // back on the UI thread. The engine requests these with cache:"force-cache",
 // so let the per-profile HTTP cache keep them across switches.
 async void Deferred(CoreWebView2WebResourceRequestedEventArgs e,Task<byte[]> work,string mime){
  var deferral=e.GetDeferral();
  try{
   byte[] bytes=null;try{bytes=await work;}catch{}
   if(disposed||View.CoreWebView2==null)return;
   if(bytes==null)Response(e,new byte[0],"text/plain",404);
   else Response(e,bytes,mime,200,"max-age=31536000, immutable");
  }catch{}
  finally{deferral.Complete();}
 }
 MpvPlayer mpv;
 public async Task Initialize(IntPtr host){
  if(IsVideo){mpv=new MpvPlayer(item.File,host);mpv.Report=delegate(string error){if(disposed)return;if(error!=null){Fail(error);return;}Ready=true;if(paused.HasValue)Pause(paused.Value);if(Report!=null)Report("MP4 / mpv 首帧已就绪。");};await mpv.Initialize();return;}
  try{
   // A replacement renders beneath the old wallpaper until its first frame.
   // Native occlusion tracking would otherwise throttle that staging renderer.
   Write("INIT create-environment profile="+profile);
   var env=await CoreWebView2Environment.CreateAsync(null,profile,new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required --disable-features=CalculateNativeWinOcclusion"));
   if(disposed)return;
   Write("INIT create-controller");
   await View.EnsureCoreWebView2Async(env);if(disposed)return;
   Write("INIT controller-ready");
   var core=View.CoreWebView2;
   core.Settings.AreDefaultContextMenusEnabled=false;core.Settings.AreDevToolsEnabled=false;
   core.Settings.IsStatusBarEnabled=false;core.Settings.AreHostObjectsAllowed=false;core.Settings.IsZoomControlEnabled=false;
   core.SetVirtualHostNameToFolderMapping(AppHost,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"scene"),CoreWebView2HostResourceAccessKind.DenyCors);
   core.SetVirtualHostNameToFolderMapping(MediaHost,Path.GetDirectoryName(item.File),CoreWebView2HostResourceAccessKind.Allow);
   core.AddWebResourceRequestedFilter("*",CoreWebView2WebResourceContext.All);
   core.WebResourceRequested+=Resource;
   core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
   core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
   core.DownloadStarting+=delegate(object sender,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
   core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){if(!e.Uri.StartsWith("https://awall.local/",StringComparison.OrdinalIgnoreCase))e.Cancel=true;};
   core.ProcessFailed+=delegate(object sender,CoreWebView2ProcessFailedEventArgs e){Fail("WebView2: "+e.ProcessFailedKind);};
   core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){
    if(disposed)return;
    if(!e.Source.StartsWith("https://awall.local/",StringComparison.OrdinalIgnoreCase))return;
    try{
     var msg=new JavaScriptSerializer(){MaxJsonLength=4*1024*1024}.Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);
     string type=Convert.ToString(msg["type"]);object val=msg.ContainsKey("value")?msg["value"]:null;
     if(type=="boot")SendLoad(core);
     else if(type=="ready"){if(Ready||Failed)return;Ready=true;ReadyInfo=new JavaScriptSerializer().Serialize(val);if(paused.HasValue)Pause(paused.Value);Write("READY "+ReadyInfo);if(Report!=null)Report(IsVideo?"MP4 首帧已就绪。":"PKG 首帧已就绪；诊断警告 "+Warnings+" 条。");}
     else if(type=="error")Fail(Convert.ToString(val));
     else if(type=="diagnostic"){var d=val as Dictionary<string,object>;string level=Convert.ToString(d["level"]);string message=Convert.ToString(d["message"]);if(level=="warn"){if(!warnings.Add(message))return;Warnings=warnings.Count;if(Report!=null)Report("PKG 兼容性警告 "+Warnings+" 条："+message);}Write(level+" "+message);}
    }catch(Exception ex){Write("MESSAGE "+ex.Message);}
   };
   core.Navigate(IsVideo?"https://awall.local/video.html":"https://awall.local/index.html?localAssets=1");
  }catch(Exception ex){Write("INIT EXCEPTION "+ex.ToString());Fail(ex.Message);}
 }
 // PKG bytes used to be fetched through the virtual host mapping, which pipes
 // the whole package (often hundreds of MB) through the browser's network
 // stack before the renderer can parse it. Copy it into WebView2 shared memory
 // instead; the page receives an ArrayBuffer over the same memory with no
 // further copy. Any failure falls back to the old fetch path.
 async void SendLoad(CoreWebView2 core){
  bool buffered=false;
  if(IsPkg){try{buffered=await PostPkgBuffer(core);}catch(Exception ex){Write("PKGBUFFER fallback "+ex.Message);}}
  if(disposed)return;
  try{core.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new{type="load",video=IsVideo?Path.GetFileName(item.File):null,pkg=IsPkg?Path.GetFileName(item.File):null,buffered=buffered}));}
  catch(Exception ex){Write("LOAD "+ex.Message);}
 }
 async Task<bool> PostPkgBuffer(CoreWebView2 core){
  long length=new FileInfo(item.File).Length;
  if(length<=0||length>int.MaxValue)return false;
  var clock=Stopwatch.StartNew();
  if(pkgWarm!=null)try{await pkgWarm;}catch{}
  if(disposed)return false;
  long warmMs=clock.ElapsedMilliseconds;
  var buffer=core.Environment.CreateSharedBuffer((ulong)length);
  try{
   IntPtr target=buffer.Buffer;string file=item.File;
   long copied=await Task.Run(delegate{
    long offset=0;var chunk=new byte[1<<20];
    using(var f=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,1<<16,FileOptions.SequentialScan)){
     int read;while(offset<length&&(read=f.Read(chunk,0,(int)Math.Min(chunk.Length,length-offset)))>0){Marshal.Copy(chunk,0,new IntPtr(target.ToInt64()+offset),read);offset+=read;}
    }
    return offset;
   });
   if(disposed)return false;
   if(copied!=length)throw new IOException("PKG 读取长度不一致："+copied+"/"+length);
   core.PostSharedBufferToScript(buffer,CoreWebView2SharedBufferAccess.ReadWrite,"{\"type\":\"pkg\"}");
   pkgBuffer=buffer;buffer=null;
   Write("PKGBUFFER "+length+" bytes warm="+warmMs+"ms total="+clock.ElapsedMilliseconds+"ms");
   return true;
  }finally{if(buffer!=null)try{buffer.Dispose();}catch{}}
 }
 void Fail(string message){if(disposed)return;Failed=true;Ready=false;LastError=message;Write("ERROR "+message);if(Report!=null)Report(Kind+" 加载失败："+message);}
 public async void Pause(bool value){
  paused=value;if(mpv!=null){mpv.Pause(value);return;}if(disposed||!Ready||View.CoreWebView2==null||pauseBusy)return;
  pauseBusy=true;
  try{while(!disposed&&Ready&&appliedPause!=paused){bool desired=paused.Value;await View.CoreWebView2.ExecuteScriptAsync("window.awallPause && window.awallPause("+(desired?"true":"false")+")");appliedPause=desired;}}
  catch(Exception ex){Write("PAUSE "+ex.Message);}finally{pauseBusy=false;}
 }
 public async void Pointer(double u,double v){if(IsVideo||!Ready||disposed||paused==true||pointerBusy)return;pointerBusy=true;try{await View.CoreWebView2.ExecuteScriptAsync("window.awallPointer("+u.ToString(System.Globalization.CultureInfo.InvariantCulture)+","+v.ToString(System.Globalization.CultureInfo.InvariantCulture)+")");}catch{}finally{pointerBusy=false;}}
 public async Task<string> Snapshot(string path){if(mpv!=null)return await mpv.Snapshot(path);if(!Ready)throw new InvalidOperationException(Kind+" 尚未显示首帧。");using(var file=File.Create(path))await View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg,file);return path;}
 public async Task<BitmapSource> CaptureFrame(){
  if(mpv!=null)return await mpv.CaptureFrame();
  if(disposed||!Ready)throw new InvalidOperationException(Kind+" 尚未显示首帧。");
  using(var stream=new MemoryStream()){
   // JPEG encodes/decodes far faster than PNG at 4K; the frame only backs the
   // 350ms transition cover, so lossless output is unnecessary.
   var capture=View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg,stream);
   // Disposing a controller with a pending capture can leave its COM task
   // unresolved. Wake cancelled switching requests independently of that task.
   if(await Task.WhenAny(capture,closed.Task)!=capture)throw new ObjectDisposedException("ScenePlayer");
   await capture;
   if(disposed)throw new ObjectDisposedException("ScenePlayer");
   stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
  }
 }
 public async Task SeekNearEnd(){if(mpv!=null){await mpv.SeekNearEnd();return;}await View.CoreWebView2.ExecuteScriptAsync("video.currentTime=Math.max(0,video.duration-0.2)");}
 public async Task<string> Stats(){if(mpv!=null)return await mpv.Stats();return View.CoreWebView2==null?"null":await View.CoreWebView2.ExecuteScriptAsync("window.awallStats && window.awallStats()");}

 public void Dispose(){if(disposed)return;disposed=true;Ready=false;Report=null;closed.TrySetResult(true);try{if(mpv!=null)mpv.Dispose();View.Dispose();if(pkgBuffer!=null)try{pkgBuffer.Dispose();}catch{}}finally{activeProfiles.Remove(profile);}}
}
}
