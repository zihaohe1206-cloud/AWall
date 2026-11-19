using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace AWall {
// HTML wallpapers run offline, with one exception: static library files (three.js, p5.js, addons ...)
// from three public CDNs. Each URL is downloaded once, on first use, into library\web-cache\<host>\
// and served from disk afterwards. Everything else stays blocked.
public static class WebCdn {
 static readonly string[] Hosts={"cdn.jsdelivr.net","unpkg.com","cdnjs.cloudflare.com"};
 // Only code / data a library needs; anything else (HTML, images, trackers) is refused.
 static readonly string[] MimeParts={"javascript","ecmascript","json","css","wasm"};
 const long MaxBytes=50L*1024*1024;
 // The exe runs from <project>\bin, so the library is a sibling of it.
 public static readonly string Root=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\','/')),"library","web-cache"));
 static readonly HttpClient http;
 static readonly Dictionary<string,Task<Entry>> inflight=new Dictionary<string,Task<Entry>>();
 static WebCdn(){
  // .NET Framework 4.x may default to TLS 1.0/1.1, which the CDNs refuse.
  ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
  // Redirects are passed to the browser (not followed here) so relative imports resolve against the final URL.
  http=new HttpClient(new HttpClientHandler(){AllowAutoRedirect=false,AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate}){Timeout=TimeSpan.FromSeconds(30)};
  http.DefaultRequestHeaders.UserAgent.ParseAdd("AWall/0.3 (+desktop wallpaper; library cache)");
 }
 public sealed class Entry {public string Mime,Location,Error;public byte[] Body;public bool Downloaded;}
 sealed class Meta {public string Url,Mime,Location,Fetched;}

 public static bool Allowed(Uri uri){
  return uri!=null&&uri.Scheme==Uri.UriSchemeHttps&&uri.IsDefaultPort&&Hosts.Contains(uri.Host.ToLowerInvariant());
 }
 static bool MimeAllowed(string mime){mime=(mime??"").ToLowerInvariant();return MimeParts.Any(x=>mime.Contains(x));}
 static string Key(Uri uri){
  using(var sha=SHA256.Create()){var hash=sha.ComputeHash(Encoding.UTF8.GetBytes(uri.AbsoluteUri));return String.Concat(hash.Take(10).Select(b=>b.ToString("x2")));}
 }

 // Concurrent requests for the same URL (several monitors) share one download.
 public static Task<Entry> Get(Uri uri){
  string url=uri.AbsoluteUri;
  lock(inflight){
   Task<Entry> task;if(inflight.TryGetValue(url,out task))return task;
   task=Load(uri);inflight[url]=task;
   task.ContinueWith(delegate{lock(inflight)inflight.Remove(url);});
   return task;
  }
 }

 static async Task<Entry> Load(Uri uri){
  try{
   string dir=Path.Combine(Root,uri.Host.ToLowerInvariant()),key=Key(uri);
   string metaPath=Path.Combine(dir,key+".json"),bodyPath=Path.Combine(dir,key+".bin");
   var json=new JavaScriptSerializer();
   if(File.Exists(metaPath)){
    var meta=json.Deserialize<Meta>(File.ReadAllText(metaPath,Encoding.UTF8));
    if(meta!=null&&meta.Url==uri.AbsoluteUri){
     if(!String.IsNullOrEmpty(meta.Location))return new Entry(){Location=meta.Location};
     if(File.Exists(bodyPath))return new Entry(){Mime=meta.Mime,Body=await Task.Run(delegate{return File.ReadAllBytes(bodyPath);})};
    }
   }
   using(var response=await http.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false)){
    int code=(int)response.StatusCode;
    if(code>=300&&code<400&&response.Headers.Location!=null){
     var target=response.Headers.Location.IsAbsoluteUri?response.Headers.Location:new Uri(uri,response.Headers.Location);
     if(!Allowed(target))return new Entry(){Error="重定向到未允许的地址 "+target.Host};
     Directory.CreateDirectory(dir);
     File.WriteAllText(metaPath,json.Serialize(new Meta(){Url=uri.AbsoluteUri,Location=target.AbsoluteUri,Fetched=DateTime.UtcNow.ToString("o")}),Encoding.UTF8);
     return new Entry(){Location=target.AbsoluteUri,Downloaded=true};
    }
    if(!response.IsSuccessStatusCode)return new Entry(){Error="HTTP "+code};
    string mime=response.Content.Headers.ContentType==null?"":response.Content.Headers.ContentType.MediaType;
    if(!MimeAllowed(mime))return new Entry(){Error="不是库文件（"+(mime==""?"未知类型":mime)+"）"};
    if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value>MaxBytes)return new Entry(){Error="文件超过 50MB"};
    var body=await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    if(body.LongLength>MaxBytes)return new Entry(){Error="文件超过 50MB"};
    Directory.CreateDirectory(dir);
    string temp=bodyPath+"."+Guid.NewGuid().ToString("N")+".tmp";
    File.WriteAllBytes(temp,body);
    if(File.Exists(bodyPath))File.Delete(bodyPath);
    File.Move(temp,bodyPath);
    File.WriteAllText(metaPath,json.Serialize(new Meta(){Url=uri.AbsoluteUri,Mime=mime,Fetched=DateTime.UtcNow.ToString("o")}),Encoding.UTF8);
    return new Entry(){Mime=mime,Body=body,Downloaded=true};
   }
  }catch(TaskCanceledException){return new Entry(){Error="下载超时（首次使用需要联网）"};}
  catch(HttpRequestException e){return new Entry(){Error="无法连接（首次使用需要联网）："+(e.InnerException!=null?e.InnerException.Message:e.Message)};}
  catch(Exception e){return new Entry(){Error=e.Message};}
 }
}
}
