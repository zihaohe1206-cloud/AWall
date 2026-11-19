using System;
using System.IO;
using Microsoft.Win32;

namespace AWall {
// Start with Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Run, value "AWall" = "<exe>" --autostart.
public static class Autostart {
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 public static string ExePath {get {return System.Reflection.Assembly.GetEntryAssembly().Location;}}
 public static string Value(){try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){return key==null?null:key.GetValue("AWall") as string;}}catch{return null;}}
 public static bool Enabled {get {return !String.IsNullOrEmpty(Value());}}
 // Throws when the registry cannot be written.
 public static void Set(bool on){
  using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(on)key.SetValue("AWall","\""+ExePath+"\" --autostart");else key.DeleteValue("AWall",false);}
 }
 // The Run entry names one AWall.exe; if that file is gone (folder moved), point it at this one.
 // Returns the old target when it was repaired, otherwise null. Throws if the registry write fails.
 public static string Repair(){
  string value=Value();if(String.IsNullOrEmpty(value))return null;
  string target=value.StartsWith("\"")&&value.IndexOf('"',1)>0?value.Substring(1,value.IndexOf('"',1)-1):value.Split(' ')[0];
  if(String.Equals(target,ExePath,StringComparison.OrdinalIgnoreCase)||File.Exists(target))return null;
  using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){key.SetValue("AWall","\""+ExePath+"\" --autostart");}
  return target;
 }
}
}
