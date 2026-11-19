using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AWall {
// Typed library: root\pkg\<name>.pkg (+ <name>.json project, <name>.jpg|gif|png preview), root\mp4\<name>.mp4
// and root\html\<name>.html (single-file web wallpaper; <name>.jpg preview is generated on first play).
// Falls back to the old per-folder project.json scan only when neither typed folder exists yet.
// Pure file-system logic: no UI, no playback.
public static class LibraryScanner {
 static readonly string[] PreviewExts={".jpg",".gif",".png",".jpeg"};
 public static string SidecarPreview(string file){foreach(var ext in PreviewExts){var p=Path.ChangeExtension(file,ext);if(File.Exists(p))return p;}return "";}
 // Sorted: videos, then PKG scenes, then web pages; by title inside each type.
 public static List<Item> Scan(string root){
  var items=new List<Item>();
  string pkgDir=Path.Combine(root,"pkg"),mp4Dir=Path.Combine(root,"mp4"),htmlDir=Path.Combine(root,"html");
  if(Directory.Exists(pkgDir)||Directory.Exists(mp4Dir)||Directory.Exists(htmlDir)){ScanTyped(items,pkgDir,"*.pkg","scene");ScanTyped(items,mp4Dir,"*.mp4","video");ScanTyped(items,htmlDir,"*.html","web");}
  else if(Directory.Exists(root))foreach(var file in Directory.GetFiles(root,"project.json",SearchOption.AllDirectories)){try {var j=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(file));var dir=Path.GetDirectoryName(file);string type=j.ContainsKey("type")?Convert.ToString(j["type"]).ToLowerInvariant():"scene";string entry=j.ContainsKey("file")?Convert.ToString(j["file"]):"";var item=new Item(){Title=j.ContainsKey("title")?Convert.ToString(j["title"]):Path.GetFileName(dir),Type=type,File=Path.GetFullPath(Path.Combine(dir,entry)),Preview=Path.Combine(dir,j.ContainsKey("preview")?Convert.ToString(j["preview"]):"preview.jpg")};if(!item.File.StartsWith(dir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;if(type=="scene"&&!File.Exists(item.File)&&File.Exists(Path.Combine(dir,"scene.pkg")))item.File=Path.Combine(dir,"scene.pkg");items.Add(item);}catch{}}
  return items.OrderBy(x=>x.Type=="video"?0:x.Type=="scene"?1:2).ThenBy(x=>x.Title).ToList();
 }
 static void ScanTyped(List<Item> items,string folder,string pattern,string type){
  if(!Directory.Exists(folder))return;
  foreach(var file in Directory.GetFiles(folder,pattern,SearchOption.TopDirectoryOnly))items.Add(new Item(){Title=Path.GetFileNameWithoutExtension(file),Type=type,File=Path.GetFullPath(file),Preview=SidecarPreview(file)});
 }
 // AI-generated pages usually sit in Downloads; keep a copy in root\html so the library only depends on its own folder.
 // Returns the library path (an identical existing copy is reused).
 public static string CopyIntoLibrary(string root,string file){
  string dir=Path.Combine(root,"html");Directory.CreateDirectory(dir);
  if(String.Equals(Path.GetFullPath(Path.GetDirectoryName(file)),dir,StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(file);
  string name=Path.GetFileNameWithoutExtension(file),target=Path.Combine(dir,name+".html");
  for(int n=2;File.Exists(target);n++){if(File.ReadAllBytes(target).SequenceEqual(File.ReadAllBytes(file)))return target;target=Path.Combine(dir,name+" ("+n+").html");}
  File.Copy(file,target);return target;
 }
}
}
