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
// --self-test, --test-library, --test-transitions. Drives the real window and players.
public partial class MainWindow {
 void RunTest(){var check=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(8)};Apply();check.Tick+=async delegate{check.Stop();try{var p=wallpaper.Players[0];await p.Snapshot(Path.Combine(data,"test-frame.jpg"));var perf=await PerfProbe.Measure(3);System.IO.File.WriteAllText(Path.Combine(data,"selftest.txt"),"items="+items.Count+"; videos="+items.Count(x=>x.Type=="video")+"; parent="+p.ParentHandle+"; media="+await p.Stats()+"; snapshot=ok; videoBackend="+(MpvActive?"mpv":"builtin")+"; footprint="+new JavaScriptSerializer().Serialize(PerfProbe.Footprint())+"; perf="+new JavaScriptSerializer().Serialize(perf));}catch(Exception e){System.IO.File.WriteAllText(Path.Combine(data,"selftest.txt"),e.ToString());}Exit();};check.Start();}
 async void TestLibrary(){
  wallpaper.StopTimers();wallpaper.SetPauseWhenCovered(false);pauseCovered.IsChecked=false;
  var results=new List<object>();string folder=Path.Combine(data,"compatibility");Directory.CreateDirectory(folder);
  // Resource use while each wallpaper plays (AWall + all child processes), plus an idle baseline.
  int perfSeconds=5;var perfArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--perf-seconds="));if(perfArg!=null&&(!int.TryParse(perfArg.Substring(15),out perfSeconds)||perfSeconds<1))perfSeconds=5;
  var footprint=PerfProbe.Footprint();await Task.Delay(1500);var idle=await PerfProbe.Measure(3);
  var perfItems=new List<KeyValuePair<string,Dictionary<string,object>>>();
  var onlyArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--only="));string only=onlyArg==null?null:onlyArg.Substring(7);foreach(var item in items.Where(x=>x.Supported&&(only==null||(only.Split(',').Contains(Path.GetFileName(Path.GetDirectoryName(x.File)))||only.Split(',').Contains(Path.GetFileNameWithoutExtension(x.File))))).ToArray()){
   library.SelectedItem=item;string id=Path.GetFileNameWithoutExtension(item.File);
   try{await ApplyAsync();var player=wallpaper.Players.First();var started=DateTime.UtcNow;
    while(!player.Ready&&!player.Failed&&(DateTime.UtcNow-started).TotalSeconds<50)await Task.Delay(250);
    if(!player.Ready)throw new Exception(player.LastError);
    await Task.Delay(1500);if(player.Failed)throw new Exception(player.LastError);
    var perf=await PerfProbe.Measure(perfSeconds);if(player.Failed)throw new Exception(player.LastError);
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
    results.Add(new{id=id,title=item.Title,type=item.Type,success=true,warnings=player.Scene!=null?player.Scene.Warnings:0,frameChanged=frameChanged,pixelRange=hi-lo,pause=pauseStats,stats=stats,perf=perf});
    perfItems.Add(new KeyValuePair<string,Dictionary<string,object>>(item.Type,perf));
   }catch(Exception ex){results.Add(new{id=id,title=item.Title,type=item.Type,success=false,error=ex.ToString()});}
   // Long enough for the previous wallpaper's processes to exit before the next measurement.
   wallpaper.Stop();System.IO.File.WriteAllText(Path.Combine(folder,"results.json"),new JavaScriptSerializer().Serialize(results));await Task.Delay(1500);
  }
  try{string text;var summary=PerfProbe.Summarize(MpvActive?"mpv":"builtin",Forms.Screen.AllScreens.Length,footprint,idle,perfItems,out text);
   System.IO.File.WriteAllText(Path.Combine(folder,"perf-summary.json"),new JavaScriptSerializer().Serialize(summary));
   System.IO.File.WriteAllText(Path.Combine(folder,"perf-summary.txt"),text,Encoding.UTF8);
  }catch(Exception e){System.IO.File.WriteAllText(Path.Combine(folder,"perf-summary.txt"),"性能汇总失败："+e,Encoding.UTF8);}
  Exit();
 }
 Task WaitForTransition(){return wallpaper.WaitForTransition();}
 async void TestTransitions(){
  var results=new List<string>();wallpaper.StopTimers();wallpaper.SetPauseWhenCovered(false);pauseCovered.IsChecked=false;
  string resultPath=Path.Combine(data,"transition-test.json");
  try{
   var video=items.First(x=>x.Type=="video");var scene=items.First(x=>x.Type=="scene"&&System.IO.File.Exists(x.File));
   library.SelectedItem=video;await ApplyAsync();await WaitForTransition();
   if(current!=video||wallpaper.Players.Count==0||!wallpaper.Players.All(x=>x.Ready)||wallpaper.Covers.Count!=0)throw new Exception("首次播放失败");if(wallpaper.Players.Any(x=>Native.GetParent(x.Handle)!=DesktopHost.Parent()))throw new Exception("首帧就绪后未正确挂桌面");results.Add("first-play/checked-parent: pass");
   var stable=wallpaper.Players.ToArray();await ApplyAsync();if(!wallpaper.Players.SequenceEqual(stable))throw new Exception("相同素材重复启动");results.Add("same-wallpaper-noop: pass");
   var original=wallpaper.Players.ToArray();library.SelectedItem=scene;await ApplyAsync();
   if(wallpaper.Players.Any(x=>!x.Ready&&x.ParentHandle!=IntPtr.Zero))throw new Exception("未就绪播放器提前挂入桌面");
   if(wallpaper.Covers.Count!=original.Length||wallpaper.PreviousPlayers.Count!=original.Length)throw new Exception("快照遮罩未创建");
   var oldCovers=wallpaper.Covers.ToArray();await Task.Delay(100);
   if(wallpaper.Transitioning){foreach(var old in original){string stats=await old.Stats();if(!stats.Contains("\"paused\":true"))throw new Exception("旧播放器未暂停");}}
   await WaitForTransition();if(current!=scene||!wallpaper.Players.All(x=>x.Ready)||wallpaper.Covers.Count!=0||wallpaper.PreviousPlayers.Count!=0||oldCovers.Any(x=>Native.IsWindow(x.Handle)))throw new Exception("场景切换或遮罩清理失败");results.Add("video-to-scene/freeze/fade/cleanup: pass");
   library.SelectedItem=video;await ApplyAsync();await WaitForTransition();if(current!=video||!wallpaper.Players.All(x=>x.Ready))throw new Exception("视频切换失败");results.Add("scene-to-video: pass");
   library.SelectedItem=scene;await wallpaper.RebuildDesktopAsync();await WaitForTransition();if(current!=video||!wallpaper.Players.All(x=>x.Ready))throw new Exception("恢复错误使用了预览素材");results.Add("host-rebuild-keeps-playing-item: pass");
   wallpaper.SetPaused(true);wallpaper.StopPlayback(false);await wallpaper.RestoreLayoutAsync();await WaitForTransition();if(current==null||current.File!=video.File||!wallpaper.Players.All(x=>x.Ready))throw new Exception("启动布局恢复失败");await Task.Delay(200);if(!wallpaper.Paused||!(await wallpaper.Players[0].Stats()).Contains("\"paused\":true"))throw new Exception("启动恢复未保留暂停设置");wallpaper.SetPaused(false);results.Add("saved-layout-restore/pause: pass");
   // Simulate loss of one desktop-owned HWND without restarting the user's shell.
   wallpaper.Players[0].Close();library.SelectedItem=scene;await wallpaper.RebuildDesktopAsync();await WaitForTransition();if(current.File!=video.File||!wallpaper.Players.All(x=>x.Ready))throw new Exception("桌面窗口销毁后未重建");results.Add("destroyed-window-rebuild: pass");
   wallpaper.StopPlayback(false);wallpaper.RecoveryPlan=WallpaperService.Uniform(video);await wallpaper.RebuildDesktopAsync();await WaitForTransition();if(current!=video||!wallpaper.Players.All(x=>x.Ready))throw new Exception("桌面恢复未使用保留目标");results.Add("deferred-shell-recovery-target: pass");
   library.SelectedItem=video;

   original=wallpaper.Players.ToArray();string brokenFile=Path.Combine(data,"test-invalid.mp4");System.IO.File.WriteAllText(brokenFile,"not a video");/* exists but cannot be decoded */var broken=new Item(){Title="invalid transition test",Type="video",File=brokenFile,Preview=""};items.Add(broken);Bind();library.SelectedItem=broken;
   await ApplyAsync();await WaitForTransition();if(current!=video||!wallpaper.Players.SequenceEqual(original)||wallpaper.Covers.Count!=0||!wallpaper.Players.All(x=>x.Ready))throw new Exception("失败后没有恢复旧播放器");results.Add("failed-load-rollback: pass");
   library.SelectedItem=scene;var stale=ApplyAsync();library.SelectedItem=video;var latest=ApplyAsync();await Task.WhenAll(stale,latest);await WaitForTransition();if(current!=video||wallpaper.Covers.Count!=0||wallpaper.PreviousPlayers.Count!=0)throw new Exception("连续切换遗留状态");results.Add("rapid-switch: pass");
   library.SelectedItem=scene;var stopping=ApplyAsync();wallpaper.Stop();await stopping;await Task.Delay(250);if(wallpaper.Players.Count!=0||wallpaper.Covers.Count!=0||wallpaper.PreviousPlayers.Count!=0||wallpaper.Transitioning)throw new Exception("停止后异步请求重新创建窗口");results.Add("stop-during-capture: pass");
   System.IO.File.WriteAllText(resultPath,new JavaScriptSerializer().Serialize(new{success=true,checks=results}));
  }catch(Exception e){System.IO.File.WriteAllText(resultPath,new JavaScriptSerializer().Serialize(new{success=false,checks=results,error=e.ToString()}));}
  Exit();
 }
}
}
