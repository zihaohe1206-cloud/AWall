using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace AWall {
// %LOCALAPPDATA%\AWall\settings.ini: one key=value per line (theme, libraryRoot, videoBackend,
// rotation, rotationMinutes, rotationOrder, weWorkshop ...). No UI, no Windows APIs.
public static class AppSettings {
 public static string FilePath {get {return Path.Combine(AppPaths.Data,"settings.ini");}}
 // Null when the key is missing or the file cannot be read.
 public static string Get(string key){
  try{if(File.Exists(FilePath))foreach(var line in File.ReadAllLines(FilePath))if(line.StartsWith(key+"="))return line.Substring(key.Length+1).Trim();}catch{}
  return null;
 }
 // Replaces the key's line. Throws on I/O failure so the caller decides how to report it.
 public static void Set(string key,string value){
  Directory.CreateDirectory(AppPaths.Data);
  var lines=File.Exists(FilePath)?File.ReadAllLines(FilePath).Where(x=>!x.StartsWith(key+"=")).ToList():new List<string>();
  lines.Add(key+"="+value);File.WriteAllLines(FilePath,lines);
 }
}
}
