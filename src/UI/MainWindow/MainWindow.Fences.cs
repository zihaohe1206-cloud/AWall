using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Forms = System.Windows.Forms;

namespace AWall {
// Fences (Features/Desktop/FenceService): commands and the tray submenu. The service draws on the desktop
// itself; the window only forwards commands and Explorer events.
public partial class MainWindow {
 readonly FenceService fences=new FenceService();
 Forms.ToolStripMenuItem trayFences;
 void StartFences(){
  fences.Status+=SetStatus;
  wallpaper.DisplaysChanged+=delegate{fences.DisplaysChanged();};
  try{fences.Start(Path.Combine(AppPaths.Data,"fences.json"));}catch(Exception e){SetStatus("桌面分区启动失败："+e.Message);}
 }
 void RegisterFenceCommands(){
  commands.Register("fence.list","fence.list","列出桌面分区",delegate(CommandArgs a){
   if(fences.Store==null)return CommandResult.Fail("桌面分区尚未启动。");
   var list=fences.Store.Fences.Select(f=>{var v=fences.View(f.Id);return new Dictionary<string,object>{
    {"id",f.Id},{"kind",f.Kind},{"title",f.Title},{"display",f.Display},{"x",f.X},{"y",f.Y},{"width",f.Width},{"height",f.Height},
    {"collapsed",f.Collapsed},{"shown",v!=null},{"count",v==null?0:v.Entries.Count},{"rule",f.Kind=="rule"?f.Rule.Describe():null},{"folder",f.Folder}};}).ToList();
   return CommandResult.Success(list.Count+" 个分区"+(fences.Store.Takeover?"（已接管桌面图标）":"")+(fences.Store.Unlocked?"，布局已解锁":""),list);
  });
  commands.Register("fence.add","fence.add kind=rule preset=images|documents|media|archives|shortcuts|folders|today | kind=rule title=<名称> [ext=.png,.jpg] [keywords=a,b] [days=N] | kind=free title=<名称> | kind=folder folder=<路径> [title=] [display=1..n]","新建桌面分区（规则 / 自由分区会接管桌面图标，文件不移动）",CmdFenceAdd);
  commands.Register("fence.remove","fence.remove id=<分区>","删除分区（文件不受影响）",delegate(CommandArgs a){
   if(fences.Store==null)return CommandResult.Fail("桌面分区尚未启动。");
   var id=a.Get("id");var f=id==null?null:fences.Store.Find(id);
   if(f==null)return CommandResult.Fail("没有这个分区："+id);if(f.Kind=="other")return CommandResult.Fail("“其他”分区随规则 / 自由分区自动出现，不能单独删除。");
   fences.Remove(f.Id);return CommandResult.Success("已删除分区："+f.Title);
  });
  commands.Register("fence.set","fence.set id=<分区> [title=] [collapsed=on|off] [x= y= width= height=]","修改分区标题、收起状态或位置（像素，相对所在显示器左上角）",CmdFenceSet);
  commands.Register("fence.lock","fence.lock on=on|off","锁定 / 解锁分区布局（解锁后拖动标题移动、拖右下角改大小）",delegate(CommandArgs a){
   if(fences.Store==null)return CommandResult.Fail("桌面分区尚未启动。");
   if(!a.IsFlag("on"))return CommandResult.Fail("on 应为 on 或 off。");
   fences.SetUnlocked(!a.Flag("on",true));return CommandResult.Success(fences.Store.Unlocked?"分区布局已解锁。":"分区布局已锁定。");
  });
  commands.Register("fence.refresh","fence.refresh","重新读取桌面和分区文件夹",delegate(CommandArgs a){fences.Refresh();return CommandResult.Success("已刷新，桌面上有 "+fences.DesktopCount+" 项。");});
 }
 static List<string> CsvList(string v){return String.IsNullOrEmpty(v)?new List<string>():v.Split(new[]{',','，',';'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Trim()).Where(x=>x.Length>0).ToList();}
 CommandResult CmdFenceAdd(CommandArgs a){
  if(fences.Store==null)return CommandResult.Fail("桌面分区尚未启动。");
  string device,error;if(!TryDisplay(a.Get("display"),out device,out error))return CommandResult.Fail(error);
  string kind=(a.Get("kind")??"").ToLowerInvariant(),title=a.Get("title");FenceConfig f=null;
  if(kind=="rule"){
   if(a.Has("preset")){f=fences.AddPreset(a.Get("preset"),device);if(f==null)return CommandResult.Fail("preset 应为 "+String.Join(" / ",FenceRule.PresetIds)+"。");if(!String.IsNullOrEmpty(title))fences.SetTitle(fences.View(f.Id),title);}
   else{
    var rule=new FenceRule(){Extensions=CsvList(a.Get("ext")).Select(x=>x==":folder"||x.StartsWith(".")?x.ToLowerInvariant():"."+x.ToLowerInvariant()).ToList(),Keywords=CsvList(a.Get("keywords"))};
    int days=0;if(a.Has("days")&&(!int.TryParse(a.Get("days"),out days)||days<0))return CommandResult.Fail("days 应为非负整数。");rule.Days=days;
    if(rule.Extensions.Count==0&&rule.Keywords.Count==0&&rule.Days==0)return CommandResult.Fail("规则分区需要 preset，或 ext / keywords / days 至少一项。");
    f=fences.AddRule(String.IsNullOrEmpty(title)?"分区":title,rule,device);
   }
  }else if(kind=="free")f=fences.AddFree(String.IsNullOrEmpty(title)?"收纳":title,device);
  else if(kind=="folder"){
   string folder=a.Get("folder");if(String.IsNullOrEmpty(folder)||!Directory.Exists(folder))return CommandResult.Fail("folder 不存在："+folder);
   f=fences.AddFolder(folder,title,device);
  }else return CommandResult.Fail("kind 应为 rule / free / folder。");
  return CommandResult.Success("已新建分区："+f.Title,new Dictionary<string,object>{{"id",f.Id}});
 }
 CommandResult CmdFenceSet(CommandArgs a){
  if(fences.Store==null)return CommandResult.Fail("桌面分区尚未启动。");
  var id=a.Get("id");var f=id==null?null:fences.Store.Find(id);if(f==null)return CommandResult.Fail("没有这个分区："+id);
  var v=fences.View(f.Id);
  if(a.Has("title")){if(String.IsNullOrWhiteSpace(a.Get("title")))return CommandResult.Fail("title 不能为空。");f.Title=a.Get("title").Trim();if(v!=null)fences.SetTitle(v,f.Title);}
  if(a.Has("collapsed")){if(!a.IsFlag("collapsed"))return CommandResult.Fail("collapsed 应为 on 或 off。");if(a.Flag("collapsed",false)!=f.Collapsed){if(v!=null)fences.ToggleCollapsed(v);else f.Collapsed=!f.Collapsed;}}
  int?[] g=new int?[4];string[] keys={"x","y","width","height"};
  for(int i=0;i<4;i++)if(a.Has(keys[i])){int n;if(!int.TryParse(a.Get(keys[i]),out n))return CommandResult.Fail(keys[i]+" 应为整数（像素）。");g[i]=n;}
  if(g.Any(x=>x.HasValue))fences.SetGeometry(f.Id,g[0],g[1],g[2],g[3]);
  return CommandResult.Success("已更新分区："+f.Title);
 }
 // Tray: 桌面分区 > new fence / lock.
 Forms.ToolStripMenuItem BuildFenceTray(){
  trayFences=new Forms.ToolStripMenuItem("桌面分区"){Padding=new Forms.Padding(4,4,12,4)};
  trayFences.DropDownOpening+=delegate{
   trayFences.DropDownItems.Clear();
   foreach(var id in FenceRule.PresetIds){string preset=id;trayFences.DropDownItems.Add("新建规则分区："+FenceRule.PresetTitle(id),null,delegate{Run("fence.add",new CommandArgs().Set("kind","rule").Set("preset",preset));});}
   trayFences.DropDownItems.Add("新建自由分区",null,delegate{Run("fence.add",new CommandArgs().Set("kind","free"));});
   trayFences.DropDownItems.Add("新建文件夹分区…",null,delegate{fences.AddFolderInteractive(null);});
   trayFences.DropDownItems.Add(new Forms.ToolStripSeparator());
   bool unlocked=fences.Store!=null&&fences.Store.Unlocked;
   trayFences.DropDownItems.Add(unlocked?"锁定分区布局":"解锁分区布局",null,delegate{Run("fence.lock",new CommandArgs().Set("on",unlocked?"on":"off"));});
  };
  trayFences.DropDownItems.Add("…");
  return trayFences;
 }
}
}
