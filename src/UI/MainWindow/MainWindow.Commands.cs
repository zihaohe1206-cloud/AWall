using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Forms = System.Windows.Forms;

namespace AWall {
// The command table. Tray items and buttons call these; a CLI / named pipe / MCP bridge will call the
// same names later (docs/ARCHITECTURE.md). Handlers run on the UI thread.
public partial class MainWindow {
 readonly CommandBus commands=new CommandBus();
 public CommandBus Commands {get {return commands;}}
 void RegisterCommands(){
  commands.Executed+=delegate(string name,CommandArgs args,CommandResult result){
   try{File.AppendAllText(Path.Combine(data,"commands.log"),DateTime.Now.ToString("s")+" "+name+" "+String.Join(" ",args.ToDictionary().Select(x=>x.Key+"="+x.Value).ToArray())+" -> "+(result.Ok?"ok":"fail")+" "+result.Message+Environment.NewLine);}catch{}
  };
  commands.Register("app.show","app.show","打开主窗口",delegate(CommandArgs a){ShowMain();return CommandResult.Success("已打开主窗口。");});
  commands.Register("app.exit","app.exit","退出 AWall",delegate(CommandArgs a){Dispatcher.BeginInvoke(new Action(Exit));return CommandResult.Success("正在退出。");});
  commands.Register("app.commands","app.commands","列出全部命令",delegate(CommandArgs a){var list=commands.List();return CommandResult.Success(list.Count+" 个命令",list);});
  commands.Register("wallpaper.status","wallpaper.status","当前每块显示器的壁纸与暂停状态",delegate(CommandArgs a){return CommandResult.Success(current==null?"未播放":(wallpaper.Paused?"已暂停：":"正在播放：")+NowTitle(),StatusData());});
  commands.Register("wallpaper.apply","wallpaper.apply file=<路径或标题> [display=all|1..n]","把素材库里的素材设为动态壁纸（display 按“应用到”从左到右编号）",CmdApply);
  commands.Register("wallpaper.pause","wallpaper.pause","暂停壁纸",delegate(CommandArgs a){return SetPause(true);});
  commands.Register("wallpaper.resume","wallpaper.resume","继续播放壁纸",delegate(CommandArgs a){return SetPause(false);});
  commands.Register("wallpaper.toggle-pause","wallpaper.toggle-pause","暂停 / 继续",delegate(CommandArgs a){return SetPause(!wallpaper.Paused);});
  commands.Register("wallpaper.next","wallpaper.next","轮播到下一张",delegate(CommandArgs a){if(current==null)return CommandResult.Fail("当前没有壁纸。");wallpaper.RotateNow(true);return CommandResult.Success("正在切换到下一张。");});
  commands.Register("wallpaper.stop","wallpaper.stop","停止动态壁纸并清除恢复布局",delegate(CommandArgs a){wallpaper.Stop();return CommandResult.Success("动态壁纸已停止。");});
  commands.Register("wallpaper.lock-snapshot","wallpaper.lock-snapshot","把主显示器当前画面设为锁屏图片",delegate(CommandArgs a){if(!wallpaper.HasPlayers)return CommandResult.Fail("请先加载壁纸，再同步锁屏快照。");wallpaper.SyncLock();return CommandResult.Success("正在同步锁屏快照。");});
  commands.Register("library.scan","library.scan","重新扫描素材库",delegate(CommandArgs a){Scan();return CommandResult.Success("已扫描 "+items.Count+" 个素材。");});
  commands.Register("library.list","library.list","列出素材库",delegate(CommandArgs a){return CommandResult.Success(items.Count+" 个素材",items.Select(x=>new Dictionary<string,object>{{"title",x.Title},{"type",x.Type},{"file",x.File}}).ToList());});
  commands.Register("rotation.set","rotation.set [on=on|off] [minutes=5|15|30|60|180] [order=sequential|random]","设置轮播",CmdRotation);
  commands.Register("autostart.set","autostart.set on=on|off","开机自动启动",CmdAutostart);
  commands.Register("system-wallpaper.set","system-wallpaper.set file=<图片> [display=all|1..n]","设为 Windows 系统壁纸（静态档：AWall 不参与渲染）",CmdSystemWallpaper);
 }
 // Runs a command from the UI. Handlers report their own progress; failures show in the status line.
 void Run(string name){Run(name,null);}
 void Run(string name,CommandArgs args){var result=commands.Execute(name,args);if(!result.Ok&&!String.IsNullOrEmpty(result.Message))SetStatus(result.Message);}

 CommandResult SetPause(bool value){
  if(current==null)return CommandResult.Fail("当前没有壁纸。");
  wallpaper.SetPaused(value);return CommandResult.Success(value?"壁纸已暂停。":"壁纸继续播放。");
 }
 object StatusData(){
  var screens=WallpaperService.OrderedScreens();var layout=wallpaper.Layout;var displays=new List<Dictionary<string,object>>();
  for(int i=0;i<screens.Length;i++){Item shown;layout.TryGetValue(screens[i].DeviceName,out shown);
   displays.Add(new Dictionary<string,object>{{"display",i+1},{"primary",screens[i].Primary},{"title",shown==null?null:shown.Title},{"type",shown==null?null:shown.Type},{"file",shown==null?null:shown.File}});}
  return new Dictionary<string,object>{{"paused",wallpaper.Paused},{"loading",wallpaper.Loading},{"displays",displays}};
 }
 // A library item by full path, then by title.
 Item FindItem(string key){
  if(String.IsNullOrEmpty(key))return null;
  var found=items.FirstOrDefault(x=>String.Equals(x.File,key,StringComparison.OrdinalIgnoreCase));if(found!=null)return found;
  try{string full=Path.GetFullPath(key);found=items.FirstOrDefault(x=>String.Equals(x.File,full,StringComparison.OrdinalIgnoreCase));if(found!=null)return found;}catch{}
  return items.FirstOrDefault(x=>String.Equals(x.Title,key,StringComparison.OrdinalIgnoreCase));
 }
 // display: empty or "all" = every display (device null), "1".."n" = numbered left to right.
 bool TryDisplay(string value,out string device,out string error){
  device=null;error=null;if(String.IsNullOrEmpty(value)||value.Equals("all",StringComparison.OrdinalIgnoreCase))return true;
  var screens=WallpaperService.OrderedScreens();int index;
  if(!int.TryParse(value,out index)||index<1||index>screens.Length){error="display 应为 all 或 1–"+screens.Length+"。";return false;}
  device=screens[index-1].DeviceName;return true;
 }
 CommandResult CmdApply(CommandArgs a){
  string key=a.Get("file");if(String.IsNullOrEmpty(key))return CommandResult.Fail("缺少参数 file。");
  var item=FindItem(key);if(item==null)return CommandResult.Fail("素材库里没有："+key);
  string device,error;if(!TryDisplay(a.Get("display"),out device,out error))return CommandResult.Fail(error);
  ApplyItem(item,device);return CommandResult.Success("正在加载："+item.Title);
 }
 async void ApplyItem(Item item,string device){try{await wallpaper.ApplyChosenAsync(item,device);}catch(Exception e){SetStatus(e.Message);}}
 CommandResult CmdRotation(CommandArgs a){
  int minutes=wallpaper.RotationMinutes;string order=a.Get("order");
  if(a.Has("minutes")&&(!int.TryParse(a.Get("minutes"),out minutes)||Array.IndexOf(WallpaperService.RotationChoices,minutes)<0))return CommandResult.Fail("minutes 只能是 5、15、30、60、180。");
  if(order!=null&&order!="sequential"&&order!="random")return CommandResult.Fail("order 应为 sequential 或 random。");
  if(a.Has("on")&&!a.IsFlag("on"))return CommandResult.Fail("on 应为 on 或 off。");
  wallpaper.ConfigureRotation(a.Has("minutes")?(int?)minutes:null,order,a.Has("on")?(bool?)a.Flag("on",wallpaper.RotationOn):null);
  return CommandResult.Success("轮播："+(wallpaper.RotationOn?"开":"关")+"，每 "+WallpaperService.RotationLabel(wallpaper.RotationMinutes)+"，"+(wallpaper.RotationRandom?"随机":"按顺序")+"。");
 }
 CommandResult CmdAutostart(CommandArgs a){
  if(!a.IsFlag("on"))return CommandResult.Fail("缺少参数 on=on|off。");
  bool on=a.Flag("on",false);SetAutostart(on);
  return Autostart.Enabled==on?CommandResult.Success(on?"已开启开机自动启动。":"已关闭开机自动启动。"):CommandResult.Fail("设置开机自动启动失败。");
 }
 // Static route of "能静则静": Windows draws the image, AWall renders nothing.
 CommandResult CmdSystemWallpaper(CommandArgs a){
  string file=a.Get("file");if(String.IsNullOrEmpty(file)||!File.Exists(file))return CommandResult.Fail("图片不存在："+file);
  string device,error;if(!TryDisplay(a.Get("display"),out device,out error))return CommandResult.Fail(error);
  bool dynamic=wallpaper.Active;
  // A live window would cover one display's system image; per-display mixing comes with the desktop runtime.
  if(device!=null&&dynamic)return CommandResult.Fail("动态壁纸正在播放，单块显示器的系统壁纸会被盖住。请先停止动态壁纸，或用 display=all。");
  SystemWallpaper.Set(file,device==null?null:Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==device));
  if(dynamic)wallpaper.Stop();
  SetStatus("已设为 Windows 系统壁纸："+Path.GetFileName(file)+"（AWall 不参与渲染）。");
  return CommandResult.Success("已设为系统壁纸。");
 }
}
}
