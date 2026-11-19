// Import from Wallpaper Engine: locate the workshop folder, list compatible items (MP4 / PKG),
// skip ones already in the library, and copy the chosen ones into wallpapers\mp4 or wallpapers\pkg.
// Written for the C# 5 compiler that ships with .NET Framework 4.x (no interpolation, no ?.).
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace AWall {

public sealed class WeCandidate {
 public string Id, Title, Type, File, ProjectJson, Preview;
 public long Size;
 public bool Duplicate;
 public string DuplicateOf = "";
}

// Finding <steam library>\steamapps\workshop\content\431960 (431960 = Wallpaper Engine's Steam AppID).
public static class WeLocator {
 const string WorkshopTail = "steamapps\\workshop\\content\\431960";

 // Order: last used folder, Steam's own library list, common install folders, then a shallow drive search.
 public static List<string> Find(string history, Action<string> progress) {
  var found = new List<string>();
  Action<string> add = delegate(string dir) {
   try {
    if (String.IsNullOrEmpty(dir)) return;
    dir = Path.GetFullPath(dir).TrimEnd('\\');
    if (Directory.Exists(dir) && !found.Any(x => x.Equals(dir, StringComparison.OrdinalIgnoreCase))) found.Add(dir);
   } catch { }
  };
  add(history);

  progress("正在从 Steam 配置查找 Wallpaper Engine…");
  foreach (var steam in SteamRoots()) {
   add(Path.Combine(steam, WorkshopTail));
   foreach (var lib in LibraryFolders(steam)) add(Path.Combine(lib, WorkshopTail));
  }
  if (found.Count > 0) return found;

  progress("正在检查常见安装位置…");
  var drives = DriveInfo.GetDrives().Where(d => { try { return d.DriveType == DriveType.Fixed && d.IsReady; } catch { return false; } }).Select(d => d.RootDirectory.FullName).ToList();
  string[] common = { "Steam", "SteamLibrary", "Program Files (x86)\\Steam", "Program Files\\Steam", "Games\\Steam", "Games\\SteamLibrary" };
  foreach (var drive in drives) foreach (var c in common) add(Path.Combine(drive, c, WorkshopTail));
  if (found.Count > 0) return found;

  // Live search: look for a "steamapps" folder up to 3 levels below each drive root.
  foreach (var drive in drives) {
   progress("正在搜索 " + drive + " …");
   foreach (var steamapps in SearchSteamApps(drive, 3)) add(Path.Combine(Path.GetDirectoryName(steamapps), WorkshopTail));
  }
  return found;
 }

 static IEnumerable<string> SteamRoots() {
  var roots = new List<string>();
  try { using (var k = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")) if (k != null) roots.Add(Convert.ToString(k.GetValue("SteamPath")).Replace('/', '\\')); } catch { }
  try { using (var k = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam")) if (k != null) roots.Add(Convert.ToString(k.GetValue("InstallPath"))); } catch { }
  try { using (var k = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Valve\\Steam")) if (k != null) roots.Add(Convert.ToString(k.GetValue("InstallPath"))); } catch { }
  return roots.Where(x => !String.IsNullOrWhiteSpace(x) && Directory.Exists(x)).Distinct(StringComparer.OrdinalIgnoreCase);
 }

 // libraryfolders.vdf lists every Steam library: "path"  "E:\\SteamLibrary"
 static IEnumerable<string> LibraryFolders(string steam) {
  var result = new List<string>();
  foreach (var vdf in new[] { Path.Combine(steam, "steamapps\\libraryfolders.vdf"), Path.Combine(steam, "config\\libraryfolders.vdf") }) {
   try {
    if (!File.Exists(vdf)) continue;
    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\"")) result.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
   } catch { }
  }
  return result;
 }

 static IEnumerable<string> SearchSteamApps(string root, int depth) {
  var result = new List<string>();
  var queue = new Queue<KeyValuePair<string, int>>();
  queue.Enqueue(new KeyValuePair<string, int>(root, 0));
  while (queue.Count > 0) {
   var cur = queue.Dequeue();
   string[] subs;
   try { subs = Directory.GetDirectories(cur.Key); } catch { continue; }
   foreach (var sub in subs) {
    string name = Path.GetFileName(sub);
    if (name.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) { result.Add(sub); continue; }
    if (name.StartsWith("$") || name.Equals("Windows", StringComparison.OrdinalIgnoreCase) || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase)) continue;
    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
    if (cur.Value + 1 < depth) queue.Enqueue(new KeyValuePair<string, int>(sub, cur.Value + 1));
   }
  }
  return result;
 }

 // workshop\content\431960 -> steamapps\common\wallpaper_engine\assets
 public static string AssetsFor(string workshop) {
  try {
   var steamapps = Directory.GetParent(workshop).Parent.Parent.FullName;
   var assets = Path.Combine(steamapps, "common\\wallpaper_engine\\assets");
   return Directory.Exists(assets) ? assets : null;
  } catch { return null; }
 }
}

public static class WeLibrary {
 // Lists compatible items; unsupported counts web / application / loose scenes.
 public static List<WeCandidate> Scan(string workshop, out int unsupported) {
  var list = new List<WeCandidate>(); unsupported = 0;
  foreach (var dir in Directory.GetDirectories(workshop)) {
   try {
    var pj = Path.Combine(dir, "project.json");
    if (!File.Exists(pj)) { unsupported++; continue; }
    var j = new JavaScriptSerializer() { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(pj));
    Func<string, string> get = k => j.ContainsKey(k) && j[k] != null ? Convert.ToString(j[k]) : "";
    string type = get("type").ToLowerInvariant(); if (type == "") type = "scene";
    string entry = get("file") == "" ? "" : Path.Combine(dir, get("file"));
    string file = null;
    if (type == "video" && entry.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) && File.Exists(entry)) file = entry;
    else if (type == "scene") {
     if (entry.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase) && File.Exists(entry)) file = entry;
     else if (File.Exists(Path.Combine(dir, "scene.pkg"))) file = Path.Combine(dir, "scene.pkg");
    }
    if (file == null) { unsupported++; continue; }
    string id = get("workshopid"); if (id == "") id = Path.GetFileName(dir);
    list.Add(new WeCandidate() {
     Id = id, Title = get("title") == "" ? id : get("title"), Type = type, File = file, ProjectJson = pj,
     Preview = Path.Combine(dir, get("preview") == "" ? "preview.jpg" : get("preview")), Size = new FileInfo(file).Length
    });
   } catch { unsupported++; }
  }
  return list.OrderBy(x => x.Type == "video" ? 0 : 1).ThenBy(x => x.Title).ToList();
 }

 // Duplicate = same workshop id in an existing sidecar .json, or a library file with identical size and SHA-256.
 public static void MarkDuplicates(List<WeCandidate> list, string root, Action<string> progress) {
  var ids = new Dictionary<string, string>();
  var bySize = new Dictionary<long, List<string>>();
  foreach (var sub in new[] { "pkg", "mp4" }) {
   var dir = Path.Combine(root, sub);
   if (!Directory.Exists(dir)) continue;
   foreach (var json in Directory.GetFiles(dir, "*.json")) {
    try {
     var j = new JavaScriptSerializer() { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(File.ReadAllText(json));
     if (j.ContainsKey("workshopid") && j["workshopid"] != null) ids[Convert.ToString(j["workshopid"])] = Path.GetFileNameWithoutExtension(json);
    } catch { }
   }
   foreach (var f in Directory.GetFiles(dir, sub == "pkg" ? "*.pkg" : "*.mp4")) {
    long len = new FileInfo(f).Length; List<string> same;
    if (!bySize.TryGetValue(len, out same)) bySize[len] = same = new List<string>();
    same.Add(f);
   }
  }
  var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
  Func<string, string> hash = delegate(string path) {
   string h;
   if (!hashes.TryGetValue(path, out h)) {
    using (var sha = SHA256.Create()) using (var s = File.OpenRead(path)) h = BitConverter.ToString(sha.ComputeHash(s));
    hashes[path] = h;
   }
   return h;
  };
  foreach (var c in list) {
   string name;
   if (ids.TryGetValue(c.Id, out name)) { c.Duplicate = true; c.DuplicateOf = name; continue; }
   List<string> same;
   if (!bySize.TryGetValue(c.Size, out same)) continue;
   progress("正在比对 " + c.Title + " …");
   foreach (var f in same) {
    try { if (hash(f) == hash(c.File)) { c.Duplicate = true; c.DuplicateOf = Path.GetFileNameWithoutExtension(f); break; } } catch { }
   }
  }
 }

 static string CleanName(string text, string fallback) {
  var invalid = Path.GetInvalidFileNameChars();
  var name = new string((text ?? "").Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim().TrimEnd('.');
  if (name.Length > 80) name = name.Substring(0, 80).Trim();
  return name == "" ? fallback : name;
 }

 // Copies each item as <title>.<ext> plus <title>.json (project.json, needed by PKG and used for de-duplication)
 // and <title>.<preview ext>. Copies go through a .part file so a cancelled copy never looks like a wallpaper.
 public static int Import(IEnumerable<WeCandidate> chosen, string root, Action<string> progress) {
  int done = 0; var items = chosen.ToList();
  foreach (var c in items) {
   var folder = Path.Combine(root, c.Type == "video" ? "mp4" : "pkg");
   Directory.CreateDirectory(folder);
   string ext = c.Type == "video" ? ".mp4" : ".pkg";
   string name = CleanName(c.Title, c.Id);
   if (File.Exists(Path.Combine(folder, name + ext))) name = name + " (" + c.Id + ")";
   string target = Path.Combine(folder, name + ext);
   progress("正在导入 " + (done + 1) + "/" + items.Count + "：" + name);
   string part = target + ".part";
   File.Copy(c.File, part, true);
   if (File.Exists(target)) File.Delete(target);
   File.Move(part, target);
   File.Copy(c.ProjectJson, Path.Combine(folder, name + ".json"), true);
   if (File.Exists(c.Preview)) File.Copy(c.Preview, Path.Combine(folder, name + Path.GetExtension(c.Preview).ToLowerInvariant()), true);
   done++;
  }
  return done;
 }

 public static void CopyTree(string from, string to, Action<string> progress) {
  foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(to + dir.Substring(from.Length));
  Directory.CreateDirectory(to);
  var files = Directory.GetFiles(from, "*", SearchOption.AllDirectories); int i = 0;
  foreach (var f in files) {
   if (++i % 200 == 0) progress("正在复制共享资源 " + i + "/" + files.Length + " …");
   File.Copy(f, to + f.Substring(from.Length), true);
  }
 }
}
}
