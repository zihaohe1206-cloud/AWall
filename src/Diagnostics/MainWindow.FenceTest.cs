using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;

namespace AWall {
// --test-fences: fences on a private store and a scratch folder. Reads the real desktop but never changes it;
// the takeover check hides the desktop icons for a moment. Result: %LOCALAPPDATA%\AWall\fence-test.json.
public partial class MainWindow {
 [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr hwnd,IntPtr parent);
 static async Task<bool> WaitFor(Func<bool> condition,int ms){var until=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<until){if(condition())return true;await Task.Delay(100);}return condition();}

 async void TestFences(){
  var result=new Dictionary<string,object>();var steps=new List<object>();result["steps"]=steps;bool ok=true;
  Action<string,bool,object> step=delegate(string name,bool pass,object detail){steps.Add(new Dictionary<string,object>{{"name",name},{"pass",pass},{"detail",detail}});if(!pass)ok=false;};
  string store=Path.Combine(data,"fences-test.json"),folder=Path.Combine(data,"fence-test-folder");
  var svc=new FenceService();
  try{
   wallpaper.StopTimers();
   // ---- Sorting rules (no windows).
   var now=DateTime.Now;
   Func<string,bool,DesktopEntry> entry=delegate(string name,bool isFolder){return new DesktopEntry(){Path=@"C:\Desk\"+name,Name=name,Extension=isFolder?"":Path.GetExtension(name),IsFolder=isFolder,Created=name.StartsWith("old")?now.AddDays(-30):now};};
   var desk=new List<DesktopEntry>{entry("a.png",false),entry("old.docx",false),entry("报告.docx",false),entry("工程",true),entry("old.lnk",false),new DesktopEntry(){Path="::{645FF040-5081-101B-9F08-00AA002F954E}",Name="回收站",IsVirtual=true,Extension=""}};
   var cfg=new List<FenceConfig>();
   Func<string,string,FenceConfig> preset=delegate(string id,string p){string t;FenceRule r;FenceRule.TryPreset(p,out t,out r);return new FenceConfig(){Id=id,Kind="rule",Title=t,Rule=r};};
   cfg.Add(new FenceConfig(){Id="free",Kind="free",Items=new List<string>{@"C:\Desk\old.docx",@"C:\Desk\missing.txt"}});
   cfg.Add(preset("img","images"));cfg.Add(preset("doc","documents"));cfg.Add(preset("dir","folders"));cfg.Add(preset("new","today"));
   cfg.Add(new FenceConfig(){Id="kw",Kind="rule",Rule=new FenceRule(){Keywords=new List<string>{"报告"}}});
   cfg.Add(new FenceConfig(){Id="other",Kind="other"});
   var sorted=FenceStore.Classify(desk,cfg,now);
   Func<string,string> names=delegate(string id){return String.Join(",",sorted[id].Select(x=>x.Name));};
   bool rulesOk=names("free")=="old.docx"&&names("img")=="a.png"&&names("doc")=="报告.docx"&&names("dir")=="工程"&&names("new")==""&&names("kw")==""&&names("other")=="old.lnk,回收站";
   step("rules.classify",rulesOk,new{free=names("free"),img=names("img"),doc=names("doc"),dir=names("dir"),today=names("new"),kw=names("kw"),other=names("other")});
   var later=new List<FenceConfig>{preset("new","today"),preset("doc","documents"),new FenceConfig(){Id="other",Kind="other"}};
   var s2=FenceStore.Classify(desk,later,now);
   step("rules.order-matters",String.Join(",",s2["new"].Select(x=>x.Name))=="a.png,报告.docx,工程",String.Join(",",s2["new"].Select(x=>x.Name)));

   // ---- Store round trip.
   if(File.Exists(store))File.Delete(store);
   var st=new FenceStore(){Path=store,Unlocked=true};st.Fences.AddRange(cfg);st.Fences[0].X=12;st.Fences[0].Collapsed=true;st.Save();
   var back=FenceStore.Load(store);
   step("store.round-trip",back.Unlocked&&back.Fences.Count==cfg.Count&&back.Fences[0].X==12&&back.Fences[0].Collapsed&&back.Fences[0].Items.Count==2&&back.Find("img").Rule.Extensions.Contains(".png")&&back.Find("kw").Rule.Keywords[0]=="报告",new{count=back.Fences.Count});
   File.Delete(store);

   // ---- A folder fence on screen.
   if(Directory.Exists(folder))Directory.Delete(folder,true);Directory.CreateDirectory(folder);
   File.WriteAllText(Path.Combine(folder,"笔记.txt"),"AWall");File.WriteAllText(Path.Combine(folder,"b.csv"),"1,2");Directory.CreateDirectory(Path.Combine(folder,"子文件夹"));
   svc.Start(store);
   var f=svc.AddFolder(folder,"测试分区",null);var v=svc.View(f.Id);
   bool mounted=await WaitFor(delegate{return v.Window.Handle!=IntPtr.Zero&&DesktopHost.IsAboveIcons(v.Window.Handle);},3000);
   step("folder.mounted-above-icons",mounted,new{bounds=v.Window.Bounds.ToString()});
   step("folder.lists-items",v.Entries.Count==3&&v.Entries[0].IsFolder,String.Join(",",v.Entries.Select(x=>x.Name)));
   bool iconsLoaded=await WaitFor(delegate{return v.IconCount==3;},4000);
   step("folder.icons-loaded",iconsLoaded,v.IconCount);
   var b=v.Window.Bounds;var px=DesktopPixels(new[]{new System.Drawing.Point(b.Right-(int)(30*svc.Scale),b.Bottom-(int)(14*svc.Scale)),new System.Drawing.Point(b.Left+1,b.Top+1)});
   step("folder.drawn-with-rounded-corner",Near(px[0],0x1F,0x22,0x29)&&!Near(px[1],0x1F,0x22,0x29),new{inside=Hex(px[0]),corner=Hex(px[1])});
   File.WriteAllText(Path.Combine(folder,"新文件.md"),"#");
   step("folder.watch-create",await WaitFor(delegate{return v.Entries.Count==4;},3000),v.Entries.Count);
   string error;bool renamed=ShellItems.Rename(Path.Combine(folder,"b.csv"),"表格.csv",out error);
   step("folder.rename",renamed&&await WaitFor(delegate{return v.Entries.Any(x=>Path.GetFileName(x.Path)=="表格.csv")&&!v.Entries.Any(x=>Path.GetFileName(x.Path)=="b.csv");},3000),error);
   File.Delete(Path.Combine(folder,"新文件.md"));
   step("folder.watch-delete",await WaitFor(delegate{return v.Entries.Count==3;},3000),v.Entries.Count);
   int menu=ShellItems.MenuItemCount(new[]{Path.Combine(folder,"笔记.txt")});
   step("shell.context-menu",menu>3,menu);
   int virt=ShellItems.MenuItemCount(new[]{"::{645FF040-5081-101B-9F08-00AA002F954E}"});
   step("shell.context-menu-recycle-bin",virt>0,virt);
   var icon=ShellItems.Icon("::{645FF040-5081-101B-9F08-00AA002F954E}",48);
   step("shell.icon-virtual",icon!=null,icon==null?0:icon.PixelWidth);

   // ---- Geometry, collapse.
   svc.SetGeometry(f.Id,40,60,400,300);await Task.Delay(200);
   var screen=Forms.Screen.PrimaryScreen;var wb=v.Window.Bounds;
   step("layout.set-geometry",wb.Left==screen.Bounds.Left+Math.Max(40,screen.WorkingArea.Left-screen.Bounds.Left)&&wb.Width==400&&wb.Height==300,wb.ToString());
   svc.ToggleCollapsed(v);await Task.Delay(200);int collapsed=v.Window.Bounds.Height;
   svc.ToggleCollapsed(v);await Task.Delay(200);
   step("layout.collapse",collapsed<=(int)(FenceView.TitleHeight*svc.Scale)+4&&v.Window.Bounds.Height==300&&f.Height==300,new{collapsed=collapsed,expanded=v.Window.Bounds.Height});

   // ---- Recovery: parent lost (Explorer restart moves windows) and window destroyed.
   int before=svc.Remounts;SetParent(v.Window.Handle,IntPtr.Zero);
   step("recover.parent-lost",await WaitFor(delegate{return DesktopHost.IsAboveIcons(v.Window.Handle)&&svc.Remounts>before;},5000),svc.Remounts-before);
   var oldHandle=v.Window.Handle;before=svc.Remounts;v.Window.Close();
   step("recover.window-destroyed",await WaitFor(delegate{return !v.Window.IsClosed&&v.Window.Handle!=oldHandle&&DesktopHost.IsAboveIcons(v.Window.Handle);},5000),new{remounts=svc.Remounts-before,count=v.Entries.Count});
   result["perfFolderFence"]=await PerfProbe.Measure(3);

   // ---- Desktop takeover with a rule fence (the real desktop, read only).
   bool iconsBefore=DesktopIcons.Visible;result["iconsVisibleBefore"]=iconsBefore;
   var img=svc.AddPreset("images",null);
   var other=svc.Store.Other;var ov=other==null?null:svc.View(other.Id);var iv=svc.View(img.Id);
   int shown=(ov==null?0:ov.Entries.Count)+(iv==null?0:iv.Entries.Count);
   step("takeover.on",other!=null&&ov!=null&&(!iconsBefore||(!DesktopIcons.Visible&&DesktopIcons.HiddenByAWall))&&shown==svc.DesktopCount,new{desktop=svc.DesktopCount,images=iv==null?-1:iv.Entries.Count,other=ov==null?-1:ov.Entries.Count});
   result["perfTakeover"]=await PerfProbe.Measure(3);
   svc.Remove(img.Id);await Task.Delay(300);
   step("takeover.off",svc.Store.Other==null&&svc.View(other.Id)==null&&DesktopIcons.Visible==iconsBefore&&!DesktopIcons.HiddenByAWall,new{icons=DesktopIcons.Visible});
   step("folder.untouched",Directory.GetFileSystemEntries(folder).Length==3,Directory.GetFileSystemEntries(folder).Length);
  }catch(Exception e){ok=false;result["error"]=e.ToString();}
  finally{
   try{svc.Stop();}catch{}
   DesktopIcons.Restore();
   try{if(File.Exists(store))File.Delete(store);if(Directory.Exists(folder))Directory.Delete(folder,true);}catch{}
  }
  result["success"]=ok;
  File.WriteAllText(Path.Combine(data,"fence-test.json"),new JavaScriptSerializer().Serialize(result));
  await Task.Delay(500);Exit();
 }
}
}
