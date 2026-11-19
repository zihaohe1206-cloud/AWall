using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AWall {
// Desktop beautification model (docs/ARCHITECTURE.md). The shared vocabulary for wallpapers and
// desktop blocks; the wallpaper feature already works, blocks are added on top of this model.

// Where a surface lives on the desktop.
public enum DesktopLayer {
 Wallpaper,  // bottom layer: what Windows calls the background
 BelowIcons, // decoration over the wallpaper, under the icons; clicks pass through to the desktop
 AboveIcons  // interactive blocks (fences, clickable widgets) above the icons
}
// How often the content changes. Decides how it is shown and therefore what it costs.
public enum PresentMode {
 Static,   // never changes
 Interval, // changes every few minutes (data-driven art, weather, calendar)
 Realtime  // animates (video, shader, live HTML)
}
// How the host actually shows a surface (see PresentPolicy).
public enum PresentRoute {
 SystemWallpaper,           // hand the image to Windows; AWall renders nothing
 SnapshotToSystemWallpaper, // render offscreen on each update, hand the image to Windows, release the renderer
 LiveWindow                 // a live renderer in a desktop window, with pause / frame-rate rules
}

// awall.json in a block package folder, e.g.
//   { "id": "clock", "name": "时钟", "version": "1.0", "kind": "web", "entry": "index.html",
//     "layer": "belowIcons", "present": "realtime", "fps": 1,
//     "size": { "width": 320, "height": 160 }, "anchor": "top-right",
//     "data": ["time"], "interactive": false }
public sealed class BlockManifest {
 public const string FileName="awall.json";
 static readonly string[] Kinds={"web","image","native"};
 static readonly string[] Anchors={"top-left","top","top-right","left","center","right","bottom-left","bottom","bottom-right"};
 public string Id,Name,Version,Kind,Entry,Anchor="center",Folder;
 public DesktopLayer Layer=DesktopLayer.BelowIcons;
 public PresentMode Present=PresentMode.Realtime;
 public int Fps=30,IntervalSeconds=300,Width,Height;
 public bool Interactive;
 public List<string> Data=new List<string>();
 public string EntryPath {get {return String.IsNullOrEmpty(Folder)||String.IsNullOrEmpty(Entry)?null:Path.Combine(Folder,Entry);}}

 // Null with an error message when the folder has no usable manifest.
 public static BlockManifest Load(string folder,out string error){
  error=null;
  try{
   string path=Path.Combine(folder,FileName);
   if(!File.Exists(path)){error="缺少 "+FileName+"："+folder;return null;}
   var j=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
   if(j==null){error=FileName+" 为空："+folder;return null;}
   var m=new BlockManifest(){Folder=Path.GetFullPath(folder)};
   m.Id=Str(j,"id",null);m.Name=Str(j,"name",m.Id);m.Version=Str(j,"version","1.0");
   m.Kind=(Str(j,"kind","web")??"").ToLowerInvariant();m.Entry=Str(j,"entry",m.Kind=="web"?"index.html":null);
   m.Anchor=(Str(j,"anchor","center")??"").ToLowerInvariant();
   m.Interactive=Bool(j,"interactive",false);
   m.Fps=Math.Max(1,Math.Min(60,Int(j,"fps",30)));
   m.IntervalSeconds=Math.Max(1,Int(j,"interval",300));
   object size;var s=j.TryGetValue("size",out size)?size as Dictionary<string,object>:null;
   if(s!=null){m.Width=Math.Max(0,Int(s,"width",0));m.Height=Math.Max(0,Int(s,"height",0));}
   object data;var list=j.TryGetValue("data",out data)?data as IEnumerable:null;
   if(list!=null&&!(data is string))foreach(var x in list)if(x!=null)m.Data.Add(Convert.ToString(x));
   DesktopLayer layer;string layerText=Str(j,"layer",null);
   if(layerText!=null){if(!Enum.TryParse(layerText,true,out layer)||!Enum.IsDefined(typeof(DesktopLayer),layer)){error="layer 应为 wallpaper / belowIcons / aboveIcons："+layerText;return null;}m.Layer=layer;}
   PresentMode present;string presentText=Str(j,"present",null);
   if(presentText!=null){if(!Enum.TryParse(presentText,true,out present)||!Enum.IsDefined(typeof(PresentMode),present)){error="present 应为 static / interval / realtime："+presentText;return null;}m.Present=present;}
   error=m.Validate();return error==null?m:null;
  }catch(Exception e){error="读取 "+FileName+" 失败："+e.Message;return null;}
 }
 // Null when valid.
 public string Validate(){
  if(String.IsNullOrEmpty(Id))return "缺少 id。";
  if(Id.IndexOfAny(Path.GetInvalidFileNameChars())>=0)return "id 只能用于文件名："+Id;
  if(!Kinds.Contains(Kind))return "kind 应为 web / image / native："+Kind;
  if(!Anchors.Contains(Anchor))return "anchor 不可用："+Anchor;
  if(Kind!="native"){
   if(String.IsNullOrEmpty(Entry))return "缺少 entry。";
   string full=Path.GetFullPath(Path.Combine(Folder,Entry));
   if(!full.StartsWith(Folder.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))return "entry 必须在包文件夹内："+Entry;
   if(!File.Exists(full))return "entry 文件不存在："+Entry;
  }
  if(Interactive&&Layer!=DesktopLayer.AboveIcons)return "interactive 的块必须放在 aboveIcons 层（图标下方的层点击会穿透到桌面）。";
  if(Kind=="image"&&Present==PresentMode.Realtime)return "image 块不会动，present 应为 static 或 interval。";
  return null;
 }
 static string Str(Dictionary<string,object> d,string key,string fallback){object v;return d.TryGetValue(key,out v)&&v!=null?Convert.ToString(v):fallback;}
 static int Int(Dictionary<string,object> d,string key,int fallback){object v;try{return d.TryGetValue(key,out v)&&v!=null?Convert.ToInt32(v):fallback;}catch{return fallback;}}
 static bool Bool(Dictionary<string,object> d,string key,bool fallback){object v;try{return d.TryGetValue(key,out v)&&v!=null?Convert.ToBoolean(v):fallback;}catch{return fallback;}}
}

// "能静则静": pick the cheapest route that still shows the content correctly.
public static class PresentPolicy {
 // Setting the system wallpaper writes a file and may fade, so snapshots are for minutes, not seconds.
 public const int MinSnapshotSeconds=60;
 public static PresentRoute Choose(BlockManifest block){
  if(block==null||block.Layer!=DesktopLayer.Wallpaper||block.Interactive)return PresentRoute.LiveWindow;
  if(block.Present==PresentMode.Static)return PresentRoute.SystemWallpaper;
  if(block.Present==PresentMode.Interval&&block.IntervalSeconds>=MinSnapshotSeconds)return PresentRoute.SnapshotToSystemWallpaper;
  return PresentRoute.LiveWindow;
 }
}

// A whole desktop state the user switches between (work / game / night). Saved as scenes\<name>.json later.
public sealed class DesktopScene {
 public string Name;
 // Display (numbered left to right, "all" for every display) -> wallpaper file or block id.
 public Dictionary<string,string> Wallpapers=new Dictionary<string,string>();
 public List<PlacedBlock> Blocks=new List<PlacedBlock>();
}
public sealed class PlacedBlock {
 public string Block,Display;
 public int X,Y,Width,Height;
 public Dictionary<string,object> Settings=new Dictionary<string,object>();
}
}
