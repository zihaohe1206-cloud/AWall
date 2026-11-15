using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace AWall {
// One mpv process per display, embedded in the existing staged desktop HWND.
public sealed class MpvPlayer : IDisposable {
 readonly string file,pipe="awall-"+Guid.NewGuid().ToString("N");readonly IntPtr host;
 Process process;bool disposed,ready;bool? paused,appliedPause;bool pauseBusy;
 public Action<string> Report;
 public MpvPlayer(string file,IntPtr host){this.file=file;this.host=host;}
 static string Quote(string value){return "\""+value.Replace("\"","\\\"")+"\"";}
 public async Task<object> Command(params object[] command){
  return await Task.Run(delegate{
   if(disposed)throw new ObjectDisposedException("mpv");
   using(var client=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.None)){
    client.Connect(2000);
    using(var timeout=new System.Threading.Timer(delegate{try{client.Dispose();}catch{}},null,5000,System.Threading.Timeout.Infinite))
    using(var writer=new StreamWriter(client,new UTF8Encoding(false),1024,true))
    using(var reader=new StreamReader(client,Encoding.UTF8,false,1024,true)){
     writer.WriteLine(new JavaScriptSerializer().Serialize(new{command=command,request_id=1}));writer.Flush();
     string line;while((line=reader.ReadLine())!=null){var response=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);if(!response.ContainsKey("request_id"))continue;if(Convert.ToString(response["error"])!="success")throw new IOException("mpv: "+response["error"]);return response.ContainsKey("data")?response["data"]:null;}
     throw new IOException("mpv IPC disconnected");
    }
   }
  });
 }
 public async Task Initialize(){
  try{
   string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mpv","mpv.exe");if(!File.Exists(exe))throw new FileNotFoundException("缺少 mpv 播放器",exe);
   process=new Process(){StartInfo=new ProcessStartInfo(exe,"--no-config --load-scripts=no --script="+Quote(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mpv","awall-ready.lua"))+" --idle=yes --force-window=yes --no-border --no-osc --input-default-bindings=no --input-cursor=no --cursor-autohide=no --stop-screensaver=no --audio=no --hwdec=auto-safe --loop-file=inf --keep-open=yes --panscan=1 --wid="+host.ToInt64()+" --input-ipc-server="+pipe){UseShellExecute=false,CreateNoWindow=true}};
   process.Start();
   bool connected=false;for(int i=0;i<30&&!disposed;i++){try{await Command("get_property","idle-active");connected=true;break;}catch{if(process.HasExited)throw new IOException("mpv 提前退出，代码 "+process.ExitCode);}await Task.Delay(50);}
   if(disposed)return;if(!connected)throw new IOException("mpv IPC 启动超时");
   await Command("loadfile",file,"replace");
   // The bundled Lua script marks playback-restart after initial loading.
   bool loaded=false;for(int i=0;i<300&&!disposed;i++){
    if(process.HasExited)throw new IOException("mpv 播放失败");
    try{var configured=await Command("get_property","user-data/awall-ready");if(Convert.ToBoolean(configured)){loaded=true;break;}}catch{}
    await Task.Delay(50);
   }
   if(disposed)return;if(!loaded)throw new IOException("mpv 首帧加载超时");ready=true;Pause(paused??false);if(Report!=null)Report(null);
   MonitorExit();
  }catch(Exception e){if(!disposed&&Report!=null)Report(e.Message);}
 }
 async void MonitorExit(){try{await Task.Run(delegate{process.WaitForExit();});if(!disposed&&Report!=null)Report("mpv 播放器已退出");}catch{}}
 public async void Pause(bool value){paused=value;if(!ready||disposed||pauseBusy)return;pauseBusy=true;try{while(!disposed&&paused!=appliedPause){bool desired=paused.Value;await Command("set_property","pause",desired);appliedPause=desired;}}catch(Exception e){if(!disposed&&Report!=null)Report(e.Message);}finally{pauseBusy=false;}}
 public async Task<string> Snapshot(string path){await Command("screenshot-to-file",path,"window");return path;}
 public async Task<BitmapSource> CaptureFrame(){string path=Path.Combine(Path.GetTempPath(),"awall-"+Guid.NewGuid().ToString("N")+".png");try{await Snapshot(path);using(var stream=File.OpenRead(path)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}}finally{try{File.Delete(path);}catch{}}}
 public async Task<string> Stats(){var result=new Dictionary<string,object>();result["backend"]="mpv";result["paused"]=await Command("get_property","pause");result["mediaPaused"]=result["paused"];result["time"]=await Command("get_property","time-pos");result["duration"]=await Command("get_property","duration");result["width"]=await Command("get_property","width");result["height"]=await Command("get_property","height");result["loop"]=true;result["ended"]=false;return new JavaScriptSerializer().Serialize(result);}
 public async Task SeekNearEnd(){double duration=Convert.ToDouble(await Command("get_property","duration"));await Command("seek",Math.Max(0,duration-0.2),"absolute");}
 public void Dispose(){if(disposed)return;disposed=true;Report=null;if(process!=null){try{if(!process.HasExited)process.Kill();}catch{} /* MonitorExit owns WaitForExit; do not dispose underneath it. */}}
}
}




