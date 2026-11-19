using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace AWall {
// One mpv process per display, embedded in the existing staged desktop HWND.
public sealed class MpvPlayer : IDisposable {
 // Keep this unnamed, non-inheritable Job alive for the application lifetime.
 // The kernel closes it on an AWall crash and terminates all assigned mpv
 // processes, independently of managed Dispose or an external watchdog.
 sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid {
  public JobHandle():base(true){}
  protected override bool ReleaseHandle(){return CloseHandle(handle);}
 }
 [StructLayout(LayoutKind.Sequential)] struct BasicJobLimits {
  public long PerProcessUserTimeLimit,PerJobUserTimeLimit;
  public uint LimitFlags;
  public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;
  public uint ActiveProcessLimit;
  public UIntPtr Affinity;
  public uint PriorityClass,SchedulingClass;
 }
 [StructLayout(LayoutKind.Sequential)] struct IoCounters {
  public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount;
  public ulong ReadTransferCount,WriteTransferCount,OtherTransferCount;
 }
 [StructLayout(LayoutKind.Sequential)] struct ExtendedJobLimits {
  public BasicJobLimits BasicLimitInformation;
  public IoCounters IoInfo;
  public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;
 }
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern JobHandle CreateJobObject(IntPtr attributes,string name);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(JobHandle job,int infoClass,ref ExtendedJobLimits info,uint length);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(JobHandle job,IntPtr child);
 [DllImport("kernel32.dll",SetLastError=true)] static extern bool CloseHandle(IntPtr handle);
 static readonly object jobLock=new object();
 static JobHandle ownerJob;
 static JobHandle OwnerJob(){
  lock(jobLock){
   if(ownerJob!=null)return ownerJob;
   // Native x64 ABI: BASIC_LIMIT_INFORMATION 64 bytes, IO_COUNTERS 48,
   // followed by four SIZE_T fields: extended information is 144 bytes.
   if(IntPtr.Size!=8||Marshal.SizeOf(typeof(BasicJobLimits))!=64||Marshal.SizeOf(typeof(ExtendedJobLimits))!=144)
    throw new InvalidOperationException("mpv Job Object 结构布局无效，要求 x64 / 144 字节。");
   var job=CreateJobObject(IntPtr.Zero,null);
   if(job.IsInvalid){int error=Marshal.GetLastWin32Error();job.Dispose();throw new Win32Exception(error,"无法创建 mpv 归属 Job Object");}
   var info=new ExtendedJobLimits();info.BasicLimitInformation.LimitFlags=0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
   if(!SetInformationJobObject(job,9,ref info,(uint)Marshal.SizeOf(typeof(ExtendedJobLimits)))){
    int error=Marshal.GetLastWin32Error();job.Dispose();throw new Win32Exception(error,"无法启用 mpv 崩溃回收 Job Object");
   }
   ownerJob=job;Log("process ownership job initialized; extendedInfoBytes=144 killOnJobClose=true");return job;
  }
 }
 readonly string file,pipe="awall-"+Guid.NewGuid().ToString("N");readonly IntPtr host;
 readonly object lifecycle=new object();
 readonly TaskCompletionSource<bool> closed=new TaskCompletionSource<bool>();
 Process process;Task<int> exitTask;SynchronizationContext reportContext;
 volatile bool disposed;bool ready,shutdownStarted;int failureReported;bool? paused,appliedPause;bool pauseBusy;
 public Action<string> Report;
 public MpvPlayer(string file,IntPtr host){this.file=file;this.host=host;}
 static void Log(string message){try{string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"mpv-lifecycle.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}}
 void Fail(string message){
  if(disposed||Interlocked.Exchange(ref failureReported,1)!=0)return;
  ready=false;Log(message);BeginShutdown();
  Action notify=delegate{if(!disposed&&Report!=null)Report(message);};
  if(reportContext!=null&&SynchronizationContext.Current!=reportContext)reportContext.Post(delegate(object unused){notify();},null);else notify();
 }
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
  reportContext=SynchronizationContext.Current;
  var deadline=Task.Delay(30000);
  try{
   if(disposed)return;
   string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mpv","mpv.exe");if(!File.Exists(exe))throw new FileNotFoundException("缺少 mpv 播放器",exe);
   var ownership=OwnerJob();
   lock(lifecycle){
    if(disposed)return;if(process!=null||exitTask!=null)throw new InvalidOperationException("mpv 已启动。");
    var started=new Process(){StartInfo=new ProcessStartInfo(exe,"--no-config --load-scripts=no --script="+Quote(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mpv","awall-ready.lua"))+" --idle=yes --force-window=yes --no-border --no-osc --input-default-bindings=no --input-cursor=no --cursor-autohide=no --stop-screensaver=no --audio=no --hwdec=auto-safe --loop-file=inf --keep-open=yes --panscan=1 --wid="+host.ToInt64()+" --input-ipc-server="+pipe){UseShellExecute=false,CreateNoWindow=true}};
    try{if(!started.Start())throw new IOException("mpv 未能启动");}catch{started.Dispose();throw;}
    process=started;
    // Exactly one owner waits and releases the Process. Shutdown never disposes
    // underneath this waiter; all other native Process access holds lifecycle.
    exitTask=Task.Run(delegate{try{started.WaitForExit();return started.ExitCode;}finally{lock(lifecycle){if(process==started)process=null;started.Dispose();}}});
    // Assignment is checked before the first await or IPC request. Nested jobs
    // are supported on current Windows; failure is fatal rather than leaving
    // an unmanaged child alive outside our crash cleanup boundary.
    if(!AssignProcessToJobObject(ownership,started.Handle)){
     int error=Marshal.GetLastWin32Error();
     try{if(!started.HasExited)started.Kill();}catch(Exception killError){Log("ownership assignment cleanup failed: "+killError.Message);}
     ObserveFailedStartExit(exitTask);
     throw new Win32Exception(error,"mpv 无法加入 AWall 归属 Job Object，已终止启动 (Win32 "+error+")");
    }
    Log("mpv assigned to ownership job pid="+started.Id);
   }
   MonitorExit();
   while(!disposed){
    try{await StartupCommand(deadline,"get_property","idle-active");break;}
    catch(IOException){if(exitTask.IsCompleted)throw;}
    catch(TimeoutException){if(deadline.IsCompleted)throw;}
    await StartupDelay(deadline);
   }
   if(disposed)return;
   await StartupCommand(deadline,"loadfile",file,"replace");
   // The bundled Lua script marks playback-restart after initial loading.
   bool loaded=false;while(!disposed){
    try{var configured=await StartupCommand(deadline,"get_property","user-data/awall-ready");if(Convert.ToBoolean(configured)){loaded=true;break;}}
    catch(IOException){if(exitTask.IsCompleted)throw;}
    catch(TimeoutException){if(deadline.IsCompleted)throw;}
    await StartupDelay(deadline);
   }
   if(disposed||failureReported!=0)return;if(!loaded)throw new IOException("mpv 首帧加载超时");ready=true;Pause(paused??false);if(Report!=null)Report(null);
  }catch(Exception e){if(!disposed)Fail(e.Message);}
 }
 async Task<object> StartupCommand(Task deadline,params object[] command){
  var request=Command(command);var winner=await Task.WhenAny(request,deadline,closed.Task,exitTask);
  if(winner!=request){ObserveAbandonedCommand(request);
   if(disposed||winner==closed.Task)throw new ObjectDisposedException("mpv");
   if(winner==exitTask)throw new IOException("mpv 提前退出，代码 "+await exitTask);
   throw new TimeoutException("mpv 启动或首帧加载超过 30 秒");
  }
  var result=await request;
  if(disposed)throw new ObjectDisposedException("mpv");
  if(deadline.IsCompleted)throw new TimeoutException("mpv 启动或首帧加载超过 30 秒");
  return result;
 }
 static async void ObserveAbandonedCommand(Task<object> request){try{await request;}catch(Exception e){Log("abandoned startup IPC: "+e.Message);}}
 static async void ObserveFailedStartExit(Task<int> waiter){try{await waiter;}catch(Exception e){Log("failed startup process wait: "+e.Message);}}
 async Task StartupDelay(Task deadline){
  var winner=await Task.WhenAny(Task.Delay(50),deadline,closed.Task,exitTask);
  if(disposed)throw new ObjectDisposedException("mpv");
  if(winner==exitTask)throw new IOException("mpv 提前退出，代码 "+await exitTask);
  if(deadline.IsCompleted)throw new TimeoutException("mpv 启动或首帧加载超过 30 秒");
 }
 async void MonitorExit(){try{int code=await exitTask;if(!disposed)Fail("mpv 播放器已退出，代码 "+code);}catch(Exception e){if(!disposed)Fail("mpv 进程监测失败："+e.Message);}}
 public async void Pause(bool value){paused=value;if(!ready||disposed||pauseBusy)return;pauseBusy=true;try{while(!disposed&&paused!=appliedPause){bool desired=paused.Value;await Command("set_property","pause",desired);appliedPause=desired;}}catch(Exception e){if(!disposed)Fail(e.Message);}finally{pauseBusy=false;}}
 public async Task<string> Snapshot(string path){await Command("screenshot-to-file",path,"window");return path;}
 public async Task<BitmapSource> CaptureFrame(){string path=Path.Combine(Path.GetTempPath(),"awall-"+Guid.NewGuid().ToString("N")+".png");try{await Snapshot(path);using(var stream=File.OpenRead(path)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}}finally{try{File.Delete(path);}catch{}}}
 public async Task<string> Stats(){var result=new Dictionary<string,object>();result["backend"]="mpv";result["paused"]=await Command("get_property","pause");result["mediaPaused"]=result["paused"];result["time"]=await Command("get_property","time-pos");result["duration"]=await Command("get_property","duration");result["width"]=await Command("get_property","width");result["height"]=await Command("get_property","height");result["loop"]=true;result["ended"]=false;return new JavaScriptSerializer().Serialize(result);}
 public async Task SeekNearEnd(){double duration=Convert.ToDouble(await Command("get_property","duration"));await Command("seek",Math.Max(0,duration-0.2),"absolute");}
 void BeginShutdown(){
  lock(lifecycle){if(shutdownStarted)return;shutdownStarted=true;}
  Task.Run(delegate{
   // A separate short-lived IPC connection sends quit without waiting for a
   // response: a clean mpv exit may close its pipe before replying.
   try{using(var client=new NamedPipeClientStream(".",pipe,PipeDirection.Out,PipeOptions.None)){
    using(var timeout=new Timer(delegate{try{client.Dispose();}catch{}},null,500,Timeout.Infinite)){
     client.Connect(350);var bytes=Encoding.UTF8.GetBytes("{\"command\":[\"quit\"]}\n");client.Write(bytes,0,bytes.Length);client.Flush();
    }
   }}catch(Exception e){Log("quit IPC: "+e.Message);}
   Task<int> waiter;lock(lifecycle){waiter=exitTask;}
   bool exited=waiter==null;
   if(waiter!=null)try{exited=waiter.Wait(100);}catch(Exception e){Log("shutdown wait failed: "+e.Message);}
   if(!exited){
    lock(lifecycle){if(process!=null){try{if(!process.HasExited)process.Kill();}catch(Exception e){Log("kill failed: "+e.Message);}}}
   }
   // MonitorExit owns reporting; its waiter owns native process disposal.
  });
 }
 public void Dispose(){lock(lifecycle){if(disposed)return;disposed=true;ready=false;Report=null;closed.TrySetResult(true);}BeginShutdown();}
}
}
