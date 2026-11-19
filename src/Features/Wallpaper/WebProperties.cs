using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AWall {
// Adjustable parameters of an HTML wallpaper. The page declares them itself, so a
// single AI-generated file stays self-contained:
//   <script type="application/json" id="awall-properties">
//   {"speed":{"type":"slider","label":"速度","min":0.2,"max":3,"step":0.1,"value":1},
//    "tint":{"type":"color","label":"主色","value":"#5ea8ff"},
//    "clock":{"type":"bool","label":"显示时钟","value":true},
//    "mode":{"type":"combo","label":"风格","options":[{"value":"calm","label":"平静"},{"value":"wild","label":"狂野"}],"value":"calm"}}
//   </script>
// The user's values live in %LOCALAPPDATA%\AWall\properties\<file>.json, never in the library file.
public sealed class WebProperty {
 public string Name, Type, Label; public double Min, Max=1, Step=0.01; public object Default;
 public List<KeyValuePair<string,string>> Options=new List<KeyValuePair<string,string>>();
 static readonly Regex Hex=new Regex("^#[0-9a-fA-F]{6}$");
 // Coerce a stored or incoming value to this property's type; null when it cannot be used.
 public object Normalize(object value){
  try{
   if(value==null)return null;
   if(Type=="slider"){double d=Convert.ToDouble(value,CultureInfo.InvariantCulture);if(Double.IsNaN(d)||Double.IsInfinity(d))return null;return Math.Max(Min,Math.Min(Max,d));}
   if(Type=="bool"){if(value is bool)return value;return Convert.ToBoolean(value,CultureInfo.InvariantCulture);}
   string s=Convert.ToString(value,CultureInfo.InvariantCulture);
   if(Type=="color")return Hex.IsMatch(s)?s.ToLowerInvariant():null;
   if(Type=="combo")return Options.Any(o=>o.Key==s)?s:null;
  }catch{}
  return null;
 }
}

public static class WebProperties {
 static readonly Regex Block=new Regex("<script\\b[^>]*\\bid\\s*=\\s*[\"']awall-properties[\"'][^>]*>(.*?)</script>",RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static string Folder {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall","properties");}}
 static string StorePath(Item item){return Path.Combine(Folder,Path.GetFileName(item.File)+".json");}

 // Declared properties in page order; empty when the page declares none or the block is malformed.
 public static List<WebProperty> Parse(Item item,out string error){
  error=null;var list=new List<WebProperty>();
  try{
   if(item==null||item.Type!="web"||!File.Exists(item.File))return list;
   var m=Block.Match(File.ReadAllText(item.File,Encoding.UTF8));if(!m.Success)return list;
   var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(m.Groups[1].Value);
   foreach(var pair in root){
    var d=pair.Value as Dictionary<string,object>;if(d==null)continue;
    var p=new WebProperty(){Name=pair.Key,Type=Str(d,"type","").ToLowerInvariant(),Label=Str(d,"label",pair.Key)};
    if(p.Type=="slider"){p.Min=Num(d,"min",0);p.Max=Num(d,"max",1);p.Step=Num(d,"step",(p.Max-p.Min)/100);if(p.Max<=p.Min||p.Step<=0)continue;}
    else if(p.Type=="combo"){
     var arr=d.ContainsKey("options")?d["options"] as System.Collections.ArrayList:null;
     var obj=d.ContainsKey("options")?d["options"] as Dictionary<string,object>:null;
     if(arr!=null)foreach(var o in arr){var od=o as Dictionary<string,object>;if(od!=null)p.Options.Add(new KeyValuePair<string,string>(Str(od,"value",""),Str(od,"label",Str(od,"value",""))));else if(o!=null)p.Options.Add(new KeyValuePair<string,string>(Convert.ToString(o),Convert.ToString(o)));}
     else if(obj!=null)foreach(var o in obj)p.Options.Add(new KeyValuePair<string,string>(o.Key,Convert.ToString(o.Value)));
     if(p.Options.Count==0)continue;
    }
    else if(p.Type!="bool"&&p.Type!="color")continue;
    p.Default=p.Normalize(d.ContainsKey("value")?d["value"]:null)??(p.Type=="slider"?(object)p.Min:p.Type=="bool"?(object)false:p.Type=="color"?"#ffffff":p.Options[0].Key);
    list.Add(p);
   }
  }catch(Exception e){error="参数声明格式有误："+e.Message;list.Clear();}
  return list;
 }
 public static List<WebProperty> Parse(Item item){string error;return Parse(item,out error);}

 // Defaults overridden by the user's saved values.
 public static Dictionary<string,object> Values(Item item,List<WebProperty> props){
  var values=props.ToDictionary(p=>p.Name,p=>p.Default);
  try{
   string path=StorePath(item);if(!File.Exists(path))return values;
   var saved=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));
   foreach(var p in props){object v;if(saved!=null&&saved.TryGetValue(p.Name,out v)){var n=p.Normalize(v);if(n!=null)values[p.Name]=n;}}
  }catch{}
  return values;
 }
 public static Dictionary<string,object> Values(Item item){return Values(item,Parse(item));}
 public static void Save(Item item,Dictionary<string,object> values){
  Directory.CreateDirectory(Folder);
  File.WriteAllText(StorePath(item),new JavaScriptSerializer().Serialize(values),Encoding.UTF8);
 }
 public static void Reset(Item item){try{File.Delete(StorePath(item));}catch{}}
 public static string Json(object value){return new JavaScriptSerializer().Serialize(value);}

 static string Str(Dictionary<string,object> d,string key,string fallback){object v;return d.TryGetValue(key,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):fallback;}
 static double Num(Dictionary<string,object> d,string key,double fallback){object v;try{return d.TryGetValue(key,out v)&&v!=null?Convert.ToDouble(v,CultureInfo.InvariantCulture):fallback;}catch{return fallback;}}
}
}
