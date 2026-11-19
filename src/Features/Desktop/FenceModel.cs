using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AWall {
// Fences (分区): AboveIcons blocks that show desktop items. They are views only; AWall never moves a
// desktop file to put it in a fence (docs/ARCHITECTURE.md "不接管资源管理器").
//   rule   - desktop items matching extensions / keywords / age
//   folder - live mirror of any folder (the only kind that shows files outside the desktop)
//   free   - desktop items the user dragged in, in the user's order
//   other  - everything on the desktop no rule / free fence took; exists while desktop takeover is on
public sealed class FenceRule {
 public List<string> Extensions=new List<string>(); // ".png"; ":folder" for folders
 public List<string> Keywords=new List<string>();   // any of them in the name
 public int Days;                                   // created within N days (0 = any age)
 public bool Matches(DesktopEntry e,DateTime now){
  if(e==null||e.IsVirtual)return false;
  if(Extensions.Count>0&&!Extensions.Any(x=>x==":folder"?e.IsFolder:!e.IsFolder&&String.Equals(x,e.Extension,StringComparison.OrdinalIgnoreCase)))return false;
  if(Keywords.Count>0&&!Keywords.Any(k=>k.Length>0&&e.Name.IndexOf(k,StringComparison.CurrentCultureIgnoreCase)>=0))return false;
  if(Days>0&&(now-e.Created).TotalDays>Days)return false;
  return Extensions.Count>0||Keywords.Count>0||Days>0;
 }
 public string Describe(){
  var parts=new List<string>();
  if(Extensions.Count>0)parts.Add(String.Join(" ",Extensions.Select(x=>x==":folder"?"文件夹":x)));
  if(Keywords.Count>0)parts.Add("名称含 "+String.Join(" / ",Keywords));
  if(Days>0)parts.Add(Days==1?"今天新建":Days+" 天内新建");
  return parts.Count==0?"（没有条件）":String.Join("；",parts);
 }
 // Built-in presets: id -> (title, rule).
 public static readonly string[] PresetIds={"images","documents","media","archives","shortcuts","folders","today"};
 public static bool TryPreset(string id,out string title,out FenceRule rule){
  rule=new FenceRule();title=null;
  switch((id??"").ToLowerInvariant()){
   case "images":title="图片";rule.Extensions.AddRange(new[]{".jpg",".jpeg",".png",".gif",".bmp",".webp",".heic",".svg",".ico",".tif",".tiff"});break;
   case "documents":title="文档";rule.Extensions.AddRange(new[]{".pdf",".doc",".docx",".xls",".xlsx",".ppt",".pptx",".txt",".md",".csv",".rtf",".odt"});break;
   case "media":title="影音";rule.Extensions.AddRange(new[]{".mp4",".mkv",".mov",".avi",".webm",".mp3",".flac",".wav",".m4a",".aac"});break;
   case "archives":title="压缩包";rule.Extensions.AddRange(new[]{".zip",".rar",".7z",".tar",".gz",".iso"});break;
   case "shortcuts":title="程序";rule.Extensions.AddRange(new[]{".lnk",".url",".exe",".appref-ms",".bat",".cmd"});break;
   case "folders":title="文件夹";rule.Extensions.Add(":folder");break;
   case "today":title="今天新建";rule.Days=1;break;
   default:return false;
  }
  return true;
 }
 public static string PresetTitle(string id){string t;FenceRule r;return TryPreset(id,out t,out r)?t:id;}
}

public sealed class FenceConfig {
 public string Id,Kind,Title,Display,Folder;
 public int X,Y,Width,Height; // pixels relative to the display's top-left corner; Height is the expanded height
 public bool Collapsed;
 public FenceRule Rule=new FenceRule();
 public List<string> Items=new List<string>(); // free fence: desktop paths, in display order
 public bool TakesDesktop {get {return Kind=="rule"||Kind=="free";}}
}

public sealed class FenceStore {
 public List<FenceConfig> Fences=new List<FenceConfig>();
 public bool Unlocked; // layout editing allowed (move / resize); locked by default
 public string Path;
 public FenceConfig Find(string id){return Fences.FirstOrDefault(x=>String.Equals(x.Id,id,StringComparison.OrdinalIgnoreCase));}
 public FenceConfig Other {get {return Fences.FirstOrDefault(x=>x.Kind=="other");}}
 public bool Takeover {get {return Fences.Any(x=>x.TakesDesktop);}}
 public string NewId(){int n=1;while(Find("f"+n)!=null)n++;return "f"+n;}

 public static FenceStore Load(string path){
  var store=new FenceStore(){Path=path};
  try{
   if(!File.Exists(path))return store;
   var j=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
   if(j==null)return store;
   object v;store.Unlocked=j.TryGetValue("unlocked",out v)&&v is bool&&(bool)v;
   var list=j.TryGetValue("fences",out v)?v as ArrayList:null;
   if(list!=null)foreach(var o in list){
    var d=o as Dictionary<string,object>;if(d==null)continue;
    var f=new FenceConfig(){Id=Str(d,"id"),Kind=(Str(d,"kind")??"").ToLowerInvariant(),Title=Str(d,"title"),Display=Str(d,"display"),Folder=Str(d,"folder"),
     X=Int(d,"x"),Y=Int(d,"y"),Width=Int(d,"width"),Height=Int(d,"height"),Collapsed=d.TryGetValue("collapsed",out v)&&v is bool&&(bool)v};
    if(String.IsNullOrEmpty(f.Id)||!new[]{"rule","folder","free","other"}.Contains(f.Kind)||store.Find(f.Id)!=null)continue;
    if(f.Kind=="other"&&store.Other!=null)continue;
    f.Items=Strings(d,"items");
    var r=d.TryGetValue("rule",out v)?v as Dictionary<string,object>:null;
    if(r!=null){f.Rule.Extensions=Strings(r,"ext").Select(x=>x.ToLowerInvariant()).ToList();f.Rule.Keywords=Strings(r,"keywords");f.Rule.Days=Int(r,"days");}
    store.Fences.Add(f);
   }
  }catch{}// A broken file starts empty; Save writes a fresh one.
  return store;
 }
 public void Save(){
  if(String.IsNullOrEmpty(Path))return;
  var list=Fences.Select(f=>new Dictionary<string,object>{
   {"id",f.Id},{"kind",f.Kind},{"title",f.Title},{"display",f.Display},{"folder",f.Folder},
   {"x",f.X},{"y",f.Y},{"width",f.Width},{"height",f.Height},{"collapsed",f.Collapsed},{"items",f.Items},
   {"rule",new Dictionary<string,object>{{"ext",f.Rule.Extensions},{"keywords",f.Rule.Keywords},{"days",f.Rule.Days}}}}).ToList();
  Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
  string temp=Path+".tmp";
  File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new Dictionary<string,object>{{"version",1},{"unlocked",Unlocked},{"fences",list}}));
  if(File.Exists(Path))File.Replace(temp,Path,null);else File.Move(temp,Path);
 }
 static string Str(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)&&v!=null?Convert.ToString(v):null;}
 static int Int(Dictionary<string,object> d,string k){object v;try{return d.TryGetValue(k,out v)&&v!=null?Convert.ToInt32(v):0;}catch{return 0;}}
 static List<string> Strings(Dictionary<string,object> d,string k){object v;var a=d.TryGetValue(k,out v)?v as ArrayList:null;return a==null?new List<string>():a.Cast<object>().Where(x=>x!=null).Select(x=>Convert.ToString(x)).ToList();}

 // Which desktop entries each fence shows. Free fences claim their items first (an item belongs to at
 // most one free fence), then rule fences in their order, then "other" gets the rest.
 public static Dictionary<string,List<DesktopEntry>> Classify(IList<DesktopEntry> desktop,IList<FenceConfig> fences,DateTime now){
  var result=new Dictionary<string,List<DesktopEntry>>(StringComparer.OrdinalIgnoreCase);
  var taken=new HashSet<string>();var byKey=new Dictionary<string,DesktopEntry>();
  foreach(var e in desktop)if(!byKey.ContainsKey(e.Key))byKey[e.Key]=e;
  foreach(var f in fences)if(f.Kind!="folder")result[f.Id]=new List<DesktopEntry>();
  foreach(var f in fences.Where(x=>x.Kind=="free"))foreach(var path in f.Items){
   DesktopEntry e;if(byKey.TryGetValue(path.ToLowerInvariant(),out e)&&taken.Add(e.Key))result[f.Id].Add(e);
  }
  foreach(var f in fences.Where(x=>x.Kind=="rule"))foreach(var e in desktop)if(!taken.Contains(e.Key)&&f.Rule.Matches(e,now)){taken.Add(e.Key);result[f.Id].Add(e);}
  var other=fences.FirstOrDefault(x=>x.Kind=="other");
  if(other!=null)foreach(var e in desktop)if(!taken.Contains(e.Key)){taken.Add(e.Key);result[other.Id].Add(e);}
  return result;
 }
}
}
