using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
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
 public ScenePlayer(Item value,string display) {
  item=value;
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
 void Response(CoreWebView2WebResourceRequestedEventArgs e,byte[] bytes,string mime,int code){e.Response=View.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(bytes),code,code==200?"OK":"Not Found","Content-Type: "+mime+"\r\nAccess-Control-Allow-Origin: https://awall.local\r\nCache-Control: no-store");}
 void Json(CoreWebView2WebResourceRequestedEventArgs e,object value){Response(e,Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value)),"application/json",200);}
 void Resource(object s,CoreWebView2WebResourceRequestedEventArgs e){
  try{
   var uri=new Uri(e.Request.Uri);
   if(uri.Scheme=="https"&&(uri.Host==AppHost||uri.Host==MediaHost)){
    if(uri.Host!=AppHost||!uri.AbsolutePath.StartsWith("/api/local-assets"))return;
    string rel=Uri.UnescapeDataString(uri.AbsolutePath.Substring("/api/local-assets".Length)).TrimStart('/');
    if(rel==""){Json(e,new{ok=Directory.Exists(Assets),roots=new[]{new{id="we",dir="local"}}});return;}
    if(rel=="we/materials/index.json"){var dir=Path.Combine(Assets,"materials");Json(e,new{names=Directory.Exists(dir)?Directory.GetFiles(dir,"*.tex",SearchOption.AllDirectories).Select(x=>x.Substring(dir.Length+1).Replace('\\','/')).Select(x=>x.Substring(0,x.Length-4)).ToArray():new string[0]});return;}
    if(!rel.StartsWith("we/"))throw new IOException("Unknown assets root");
    var path=Path.GetFullPath(Path.Combine(Assets,rel.Substring(3).Replace('/',Path.DirectorySeparatorChar)));
    if(!path.StartsWith(Assets+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)){Response(e,new byte[0],"text/plain",404);return;}
    Response(e,File.ReadAllBytes(path),"application/octet-stream",200);return;
   }
   if(uri.Scheme=="data"||uri.Scheme=="blob"||uri.Scheme=="about")return;
   Response(e,new byte[0],"text/plain",403);
  }catch{Response(e,new byte[0],"text/plain",404);}
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
     if(type=="boot")core.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new{type="load",video=IsVideo?Path.GetFileName(item.File):null,pkg=Path.GetExtension(item.File).Equals(".pkg",StringComparison.OrdinalIgnoreCase)?Path.GetFileName(item.File):null}));
     else if(type=="ready"){if(Ready||Failed)return;Ready=true;ReadyInfo=new JavaScriptSerializer().Serialize(val);if(paused.HasValue)Pause(paused.Value);Write("READY "+ReadyInfo);if(Report!=null)Report(IsVideo?"MP4 首帧已就绪。":"PKG 首帧已就绪；诊断警告 "+Warnings+" 条。");}
     else if(type=="error")Fail(Convert.ToString(val));
     else if(type=="diagnostic"){var d=val as Dictionary<string,object>;string level=Convert.ToString(d["level"]);string message=Convert.ToString(d["message"]);if(level=="warn"){if(!warnings.Add(message))return;Warnings=warnings.Count;if(Report!=null)Report("PKG 兼容性警告 "+Warnings+" 条："+message);}Write(level+" "+message);}
    }catch(Exception ex){Write("MESSAGE "+ex.Message);}
   };
   core.Navigate(IsVideo?"https://awall.local/video.html":"https://awall.local/index.html?localAssets=1");
  }catch(Exception ex){Write("INIT EXCEPTION "+ex.ToString());Fail(ex.Message);}
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
   var capture=View.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
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

 public void Dispose(){if(disposed)return;disposed=true;Ready=false;Report=null;closed.TrySetResult(true);try{if(mpv!=null)mpv.Dispose();View.Dispose();}finally{activeProfiles.Remove(profile);}}
}
}
