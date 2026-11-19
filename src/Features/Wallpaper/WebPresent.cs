using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AWall {
// How often an HTML wallpaper changes, declared in its <head>:
//   <meta name="awall-present" content="interval=300">   re-render every 300 s (>= 60) and hand the image to Windows
//   <meta name="awall-present" content="static">         render once and hand the image to Windows
//   (no tag, or content="realtime")                       live window, as before
// Pages that only change every few minutes (data art, calendars, weather) then cost nothing between updates.
public sealed class WebPresent {
 public PresentMode Mode=PresentMode.Realtime;
 public int IntervalSeconds;
 static readonly Regex Meta=new Regex("<meta\\b[^>]*\\bname\\s*=\\s*[\"']awall-present[\"'][^>]*>",RegexOptions.IgnoreCase);
 static readonly Regex Content=new Regex("\\bcontent\\s*=\\s*[\"']([^\"']*)[\"']",RegexOptions.IgnoreCase);
 static readonly Dictionary<string,Tuple<DateTime,WebPresent>> cache=new Dictionary<string,Tuple<DateTime,WebPresent>>(StringComparer.OrdinalIgnoreCase);

 // Cached by file and modification time; unreadable files count as realtime.
 public static WebPresent Of(string file){
  var result=new WebPresent();
  try{
   if(String.IsNullOrEmpty(file)||!File.Exists(file))return result;
   var stamp=File.GetLastWriteTimeUtc(file);Tuple<DateTime,WebPresent> hit;
   lock(cache){if(cache.TryGetValue(file,out hit)&&hit.Item1==stamp)return hit.Item2;}
   result=Parse(File.ReadAllText(file));
   lock(cache){cache[file]=Tuple.Create(stamp,result);}
  }catch{}
  return result;
 }
 public static WebPresent Parse(string html){
  var result=new WebPresent();
  var meta=Meta.Match(html??"");if(!meta.Success)return result;
  var content=Content.Match(meta.Value);if(!content.Success)return result;
  string value=content.Groups[1].Value.Trim().ToLowerInvariant();
  if(value=="static"){result.Mode=PresentMode.Static;return result;}
  if(value.StartsWith("interval")){
   int seconds;string number=value.Substring("interval".Length).TrimStart('=',':',' ');
   if(int.TryParse(number,out seconds)&&seconds>0){result.Mode=PresentMode.Interval;result.IntervalSeconds=seconds;}
  }
  return result;
 }
}
}
