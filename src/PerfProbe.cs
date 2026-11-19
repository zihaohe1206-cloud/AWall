using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace AWall {
// Resource use of AWall plus everything it started (mpv, WebView2 browser / GPU / renderer
// processes), sampled while a wallpaper plays. Used by --test-library and --self-test so that
// "lightweight" is a measured number rather than a claim. Written for the C# 5 compiler.
public static class PerfProbe {
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
 struct ProcessEntry {
  public uint Size,Usage,ProcessId;public IntPtr DefaultHeapId;public uint ModuleId,Threads,ParentProcessId;public int PriorityBase;public uint Flags;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string ExeFile;
 }
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint processId);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true,SetLastError=true)] static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 sealed class Snap {public int Id;public string Name;public TimeSpan Cpu;public long WorkingSet,Private;}

 // AWall's own process and all descendants that started after it (guards against PID reuse).
 static List<int> Tree(){
  var self=Process.GetCurrentProcess();int root=self.Id;DateTime started=self.StartTime;
  var children=new Dictionary<int,List<int>>();
  IntPtr snap=CreateToolhelp32Snapshot(2,0);// TH32CS_SNAPPROCESS
  if(snap==IntPtr.Zero||snap==new IntPtr(-1))return new List<int>(){root};
  try{
   var entry=new ProcessEntry();entry.Size=(uint)Marshal.SizeOf(typeof(ProcessEntry));
   if(Process32FirstW(snap,ref entry))do{
    int parent=(int)entry.ParentProcessId,id=(int)entry.ProcessId;
    if(id!=parent){List<int> list;if(!children.TryGetValue(parent,out list)){list=new List<int>();children[parent]=list;}list.Add(id);}
   }while(Process32NextW(snap,ref entry));
  }finally{CloseHandle(snap);}
  var result=new List<int>(){root};var queue=new Queue<int>();queue.Enqueue(root);
  while(queue.Count>0){
   List<int> list;if(!children.TryGetValue(queue.Dequeue(),out list))continue;
   foreach(int id in list){
    if(result.Contains(id))continue;
    try{using(var p=Process.GetProcessById(id)){if(p.StartTime<started)continue;}}catch{continue;}
    result.Add(id);queue.Enqueue(id);
   }
  }
  return result;
 }
 static List<Snap> Read(List<int> ids){
  var list=new List<Snap>();
  foreach(int id in ids){try{using(var p=Process.GetProcessById(id)){list.Add(new Snap(){Id=id,Name=p.ProcessName,Cpu=p.TotalProcessorTime,WorkingSet=p.WorkingSet64,Private=p.PrivateMemorySize64});}}catch{}}
  return list;
 }
 // Task Manager's "GPU Engine" counters. Missing on older Windows or without permission:
 // the GPU fields are then null rather than guessed.
 static List<KeyValuePair<string,PerformanceCounter>> GpuCounters(List<int> ids){
  var result=new List<KeyValuePair<string,PerformanceCounter>>();
  try{
   var category=new PerformanceCounterCategory("GPU Engine");
   foreach(string name in category.GetInstanceNames()){
    string kind=name.EndsWith("engtype_3D")?"3d":name.EndsWith("engtype_VideoDecode")?"video":null;if(kind==null)continue;
    if(!ids.Any(id=>name.StartsWith("pid_"+id+"_")))continue;
    var counter=new PerformanceCounter("GPU Engine","Utilization Percentage",name,true);
    try{counter.NextValue();result.Add(new KeyValuePair<string,PerformanceCounter>(kind,counter));}catch{counter.Dispose();}
   }
  }catch{}
  return result;
 }
 static double MB(long bytes){return Math.Round(bytes/1048576.0,1);}
 // Samples for `seconds`. CPU is the share of all logical processors used by processes alive at
 // both ends of the window; memory is the total at the end.
 public static async Task<Dictionary<string,object>> Measure(int seconds){
  seconds=Math.Max(1,Math.Min(60,seconds));
  var before=await Task.Run(delegate{var ids=Tree();return Tuple.Create(Read(ids),GpuCounters(ids));});
  var clock=Stopwatch.StartNew();
  await Task.Delay(seconds*1000);
  return await Task.Run(delegate{
   double wall=Math.Max(0.1,clock.Elapsed.TotalSeconds);
   var after=Read(Tree());
   double cpu=0;
   foreach(var a in after){var b=before.Item1.FirstOrDefault(x=>x.Id==a.Id);if(b!=null)cpu+=(a.Cpu-b.Cpu).TotalSeconds;}
   object gpu3d=null,gpuVideo=null;
   if(before.Item2.Count>0){
    double d=0,v=0;
    foreach(var c in before.Item2){try{float value=c.Value.NextValue();if(c.Key=="3d")d+=value;else v+=value;}catch{}finally{c.Value.Dispose();}}
    gpu3d=Math.Round(d,1);gpuVideo=Math.Round(v,1);
   }
   var result=new Dictionary<string,object>();
   result["seconds"]=Math.Round(wall,1);
   result["processes"]=after.Count;
   result["workingSetMB"]=MB(after.Sum(x=>x.WorkingSet));
   result["privateMB"]=MB(after.Sum(x=>x.Private));
   result["cpuPercent"]=Math.Round(cpu/wall/Environment.ProcessorCount*100,1);
   result["gpu3dPercent"]=gpu3d;
   result["gpuVideoDecodePercent"]=gpuVideo;
   result["breakdown"]=after.GroupBy(x=>x.Name).Select(g=>g.Key+" x"+g.Count()+" "+Math.Round(g.Sum(x=>x.Private)/1048576.0)+"MB").ToArray();
   return result;
  });
 }
 // Size of the folder AWall.exe runs from (the WebView2 runtime is part of Windows and not counted).
 public static Dictionary<string,object> Footprint(){
  var result=new Dictionary<string,object>();string dir=AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\','/');
  result["folder"]=dir;
  try{
   long total=0,mpv=0;string mpvDir=Path.Combine(dir,"mpv")+Path.DirectorySeparatorChar;
   foreach(var file in Directory.GetFiles(dir,"*",SearchOption.AllDirectories)){long n=new FileInfo(file).Length;total+=n;if(file.StartsWith(mpvDir,StringComparison.OrdinalIgnoreCase))mpv+=n;}
   result["totalMB"]=MB(total);result["mpvMB"]=MB(mpv);
  }catch(Exception e){result["error"]=e.Message;}
  return result;
 }
 static double Num(Dictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)&&v!=null?Convert.ToDouble(v):double.NaN;}
 static double Avg(IEnumerable<Dictionary<string,object>> list,string key){var values=list.Select(x=>Num(x,key)).Where(x=>!double.IsNaN(x)).ToArray();return values.Length==0?double.NaN:Math.Round(values.Average(),1);}
 static string Show(double value,string unit){return double.IsNaN(value)?"未测到":value.ToString("0.#")+unit;}
 static string TypeName(string type){return type=="video"?"MP4":type=="scene"?"PKG":type=="web"?"网页":type;}
 // perItem: (library type, Measure() result) for every wallpaper that played successfully.
 public static Dictionary<string,object> Summarize(string backend,int screens,Dictionary<string,object> footprint,Dictionary<string,object> idle,List<KeyValuePair<string,Dictionary<string,object>>> perItem,out string text){
  var summary=new Dictionary<string,object>();var byType=new Dictionary<string,object>();
  var lines=new StringBuilder();
  lines.AppendLine("AWall 性能自检  "+DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
  lines.AppendLine("MP4 播放器："+(backend=="mpv"?"mpv":"内置（WebView2）")+"    显示器："+screens+" 块");
  lines.AppendLine("程序体积："+Show(Num(footprint,"totalMB")," MB")+"（其中 mpv "+Show(Num(footprint,"mpvMB")," MB")+"；不含 Windows 自带的 WebView2 运行时）");
  lines.AppendLine("空闲（未播放壁纸）：内存 "+Show(Num(idle,"privateMB")," MB")+"，CPU "+Show(Num(idle,"cpuPercent"),"%"));
  foreach(var group in perItem.GroupBy(x=>x.Key)){
   var list=group.Select(x=>x.Value).ToList();
   var stats=new Dictionary<string,object>();
   stats["count"]=list.Count;
   foreach(var key in new[]{"privateMB","workingSetMB","cpuPercent","gpu3dPercent","gpuVideoDecodePercent"}){double a=Avg(list,key);stats["avg_"+key]=double.IsNaN(a)?null:(object)a;}
   double peak=list.Select(x=>Num(x,"privateMB")).Where(x=>!double.IsNaN(x)).DefaultIfEmpty(double.NaN).Max();
   stats["max_privateMB"]=double.IsNaN(peak)?null:(object)peak;
   byType[group.Key]=stats;
   lines.AppendLine(TypeName(group.Key)+" ×"+list.Count+"：平均内存 "+Show(Avg(list,"privateMB")," MB")+"（最高 "+Show(peak," MB")+"），CPU "+Show(Avg(list,"cpuPercent"),"%")+"，GPU 3D "+Show(Avg(list,"gpu3dPercent"),"%")+"，视频解码 "+Show(Avg(list,"gpuVideoDecodePercent"),"%"));
  }
  lines.AppendLine("说明：内存为专用内存（Private Bytes），包含 AWall 及其启动的全部子进程；多屏时每块显示器各有一个播放器。");
  summary["time"]=DateTime.Now.ToString("s");summary["videoBackend"]=backend;summary["screens"]=screens;
  summary["footprint"]=footprint;summary["idle"]=idle;summary["byType"]=byType;
  text=lines.ToString();
  return summary;
 }
}
}
