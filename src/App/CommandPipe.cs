using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Web.Script.Serialization;

namespace AWall {
// Local command channel: other programs, scripts and agents reach the command table through a named pipe.
// Protocol: one connection = one request. The client writes one UTF-8 JSON line
//   {"command":"wallpaper.apply","args":{"file":"C:\\a.mp4","display":"2"}}
// and the server answers with one JSON line {"ok":true,"message":"...","data":...}.
// The pipe only accepts the current user on this machine (network clients are denied).
public static class CommandPipe {
 public const int MaxRequestChars=65536;
 // Per logon session, like the Local\ mutex that makes AWall single-instance.
 public static string Name {get {return "AWall.Cmd.S"+Process.GetCurrentProcess().SessionId;}}
 static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
 public static Encoding Encoding {get {return Utf8;}}
 static JavaScriptSerializer Json(){return new JavaScriptSerializer(){MaxJsonLength=Int32.MaxValue,RecursionLimit=64};}

 public static string Serialize(object value){return Json().Serialize(value);}
 public static string Request(string command,IDictionary<string,string> args){
  return Serialize(new Dictionary<string,object>{{"command",command},{"args",args??new Dictionary<string,string>()}});
 }
 public static string Response(CommandResult result){
  var body=new Dictionary<string,object>{{"ok",result.Ok},{"message",result.Message??""},{"data",result.Data}};
  try{return Serialize(body);}
  catch(Exception e){body["data"]=null;body["message"]=(result.Message??"")+"（结果数据无法序列化："+e.Message+"）";return Serialize(body);}
 }
 public static string Failure(string message){return Response(CommandResult.Fail(message));}
 public static bool TryParseRequest(string line,out string command,out CommandArgs args,out string error){
  command=null;args=new CommandArgs();error=null;
  Dictionary<string,object> body;
  try{body=Json().DeserializeObject(line) as Dictionary<string,object>;}catch(Exception e){error="请求不是有效的 JSON："+e.Message;return false;}
  object value;
  if(body==null||!body.TryGetValue("command",out value)||!(value is string)||((string)value).Length==0){error="请求缺少 command。";return false;}
  command=(string)value;
  if(body.TryGetValue("args",out value)&&value!=null){
   var map=value as Dictionary<string,object>;if(map==null){error="args 应为对象。";return false;}
   foreach(var pair in map)args.Set(pair.Key,pair.Value==null?"":Convert.ToString(pair.Value,System.Globalization.CultureInfo.InvariantCulture));
  }
  return true;
 }
 public static Dictionary<string,object> ParseResponse(string line){
  try{return Json().DeserializeObject(line) as Dictionary<string,object>;}catch{return null;}
 }
 // Escapes everything outside ASCII as \uXXXX so JSON survives any console code page.
 public static string AsciiJson(string json){
  var sb=new StringBuilder(json.Length);
  foreach(char c in json){if(c<128)sb.Append(c);else sb.Append("\\u").Append(((int)c).ToString("x4"));}
  return sb.ToString();
 }
 // Reads up to '\n'; null when the line is empty, cut off or longer than the limit.
 public static string ReadLine(TextReader reader,int limit){
  var sb=new StringBuilder();
  while(true){int c=reader.Read();if(c<0)return sb.Length==0?null:sb.ToString();if(c=='\n')break;if(sb.Length>=limit)return null;sb.Append((char)c);}
  string line=sb.ToString().TrimEnd('\r');return line.Length==0?null:line;
 }
}

public sealed class CommandPipeServer {
 // Runs one command and returns the response line; it marshals onto the UI thread itself.
 // Returning null means the command could not run in time.
 readonly Func<string,CommandArgs,string> execute;
 CommandPipeServer(Func<string,CommandArgs,string> execute){this.execute=execute;}
 public static CommandPipeServer Start(Func<string,CommandArgs,string> execute){
  if(execute==null)throw new ArgumentNullException("execute");
  var server=new CommandPipeServer(execute);
  new Thread(server.Listen){IsBackground=true,Name="AWall command pipe"}.Start();
  return server;
 }
 void Listen(){
  int failures=0;
  while(true){
   NamedPipeServerStream pipe=null;
   try{pipe=Create();pipe.WaitForConnection();failures=0;}
   catch(Exception e){if(pipe!=null)pipe.Dispose();Log("监听失败："+e.Message);if(++failures>=20){Log("连续失败，命令管道停止。");return;}Thread.Sleep(1000);continue;}
   var connected=pipe;ThreadPool.QueueUserWorkItem(delegate{Serve(connected);});
  }
 }
 static NamedPipeServerStream Create(){
  var security=new PipeSecurity();
  security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
  security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
  return new NamedPipeServerStream(CommandPipe.Name,PipeDirection.InOut,NamedPipeServerStream.MaxAllowedServerInstances,PipeTransmissionMode.Byte,PipeOptions.None,4096,4096,security);
 }
 void Serve(NamedPipeServerStream pipe){
  using(pipe){
   try{
    string response;
    using(var reader=new StreamReader(pipe,CommandPipe.Encoding,false,4096,true)){
     string line=CommandPipe.ReadLine(reader,CommandPipe.MaxRequestChars);
     string command,error;CommandArgs args;
     if(line==null)response=CommandPipe.Failure("请求为空或超过 "+CommandPipe.MaxRequestChars+" 个字符。");
     else if(!CommandPipe.TryParseRequest(line,out command,out args,out error))response=CommandPipe.Failure(error);
     else response=execute(command,args)??CommandPipe.Failure("AWall 正忙，命令没有执行。");
    }
    var bytes=CommandPipe.Encoding.GetBytes(response+"\n");
    pipe.Write(bytes,0,bytes.Length);pipe.Flush();pipe.WaitForPipeDrain();
   }catch(Exception e){Log("处理请求失败："+e.Message);}
  }
 }
 static void Log(string message){
  try{Directory.CreateDirectory(AppPaths.Data);File.AppendAllText(Path.Combine(AppPaths.Data,"command-pipe.log"),DateTime.Now.ToString("s")+" "+message+Environment.NewLine);}catch{}
 }
}

public static class CommandClient {
 // Returns the parsed response, or null with an error when AWall is not running or did not answer.
 public static Dictionary<string,object> Send(string command,IDictionary<string,string> args,int connectTimeoutMs,out string error){
  error=null;
  try{
   using(var pipe=new NamedPipeClientStream(".",CommandPipe.Name,PipeDirection.InOut)){
    try{pipe.Connect(connectTimeoutMs);}catch(TimeoutException){error="AWall 没有在运行（或版本太旧，不支持命令）。";return null;}
    var request=CommandPipe.Encoding.GetBytes(CommandPipe.Request(command,args)+"\n");
    pipe.Write(request,0,request.Length);pipe.Flush();
    using(var reader=new StreamReader(pipe,CommandPipe.Encoding)){
     string line=reader.ReadLine();
     var response=line==null?null:CommandPipe.ParseResponse(line);
     if(response==null){error="AWall 没有返回结果。";return null;}
     return response;
    }
   }
  }catch(Exception e){error="无法连接 AWall："+e.Message;return null;}
 }
}

// The command-line front end shared by "AWall.exe --cmd ..." and AWallCmd.exe.
public static class CommandLine {
 public const int ExitOk=0,ExitFailed=1,ExitNotRunning=2,ExitUsage=64;
 public const string Usage=
  "用法：AWallCmd <命令> [key=value ...] [--json]\n"+
  "      AWall.exe --cmd <命令> [key=value ...] [--json]\n"+
  "\n"+
  "把命令交给正在运行的 AWall 执行并打印结果。\n"+
  "  AWallCmd help                 列出全部命令\n"+
  "  AWallCmd wallpaper.status     查看每块显示器的壁纸\n"+
  "  AWallCmd wallpaper.apply file=C:\\a.mp4 display=2\n"+
  "  --json                        输出完整 JSON（非 ASCII 字符转义为 \\uXXXX），适合脚本和 agent\n"+
  "  --utf8                        按 UTF-8 输出（Git Bash 等 UTF-8 终端里中文不乱码）\n"+
  "\n"+
  "退出码：0 成功，1 命令失败，2 AWall 未运行或无响应，64 用法错误。";
 [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);

 public static int Run(string[] tokens,TextWriter output,TextWriter errors){
  bool json=false;string name=null;var args=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
  foreach(var token in tokens??new string[0]){
   if(String.IsNullOrEmpty(token))continue;
   if(token=="--json"){json=true;continue;}
   // Git Bash / mintty and other UTF-8 terminals: the default console code page (GBK on Chinese Windows) shows as mojibake there.
   if(token=="--utf8"){var utf8=new UTF8Encoding(false);output=new StreamWriter(Console.OpenStandardOutput(),utf8){AutoFlush=true};errors=new StreamWriter(Console.OpenStandardError(),utf8){AutoFlush=true};continue;}
   int at=token.IndexOf('=');
   if(at>0){args[token.Substring(0,at)]=token.Substring(at+1);continue;}
   if(name==null&&at<0&&!token.StartsWith("-")){name=token;continue;}
   errors.WriteLine("无法识别的参数："+token);errors.WriteLine();errors.WriteLine(Usage);return ExitUsage;
  }
  if(name==null||name=="-h"||name=="/?"){(name==null?errors:output).WriteLine(Usage);return name==null?ExitUsage:ExitOk;}
  if(name.Equals("help",StringComparison.OrdinalIgnoreCase))name="app.commands";
  // Lets app.show bring the window to the front even though the request comes from another process.
  try{AllowSetForegroundWindow(-1);}catch{}
  string error;var response=CommandClient.Send(name,args,3000,out error);
  if(response==null){errors.WriteLine(error);return ExitNotRunning;}
  object value;bool ok=response.TryGetValue("ok",out value)&&value is bool&&(bool)value;
  if(json){output.WriteLine(CommandPipe.AsciiJson(CommandPipe.Serialize(response)));return ok?ExitOk:ExitFailed;}
  string message=response.TryGetValue("message",out value)?value as string:null;
  if(!String.IsNullOrEmpty(message))(ok?output:errors).WriteLine(message);
  object data;
  if(response.TryGetValue("data",out data)&&data!=null){
   if(name.Equals("app.commands",StringComparison.OrdinalIgnoreCase))WriteCommands(data,output);
   else{var sb=new StringBuilder();Pretty(data,sb,0);output.WriteLine(sb.ToString());}
  }
  return ok?ExitOk:ExitFailed;
 }
 static void WriteCommands(object data,TextWriter output){
  var list=data as IEnumerable;if(list==null)return;
  foreach(var entry in list){
   var map=entry as Dictionary<string,object>;if(map==null)continue;
   object usage,description;map.TryGetValue("Usage",out usage);map.TryGetValue("Description",out description);
   output.WriteLine("  "+usage);if(description!=null&&description.ToString().Length>0)output.WriteLine("      "+description);
  }
 }
 // Indented JSON for people; Chinese stays readable.
 static void Pretty(object value,StringBuilder sb,int depth){
  string pad=new string(' ',depth*2),inner=new string(' ',depth*2+2);
  var map=value as IDictionary<string,object>;
  if(map!=null){
   if(map.Count==0){sb.Append("{}");return;}
   sb.Append("{\n");int i=0;
   foreach(var pair in map){sb.Append(inner).Append(CommandPipe.Serialize(pair.Key)).Append(": ");Pretty(pair.Value,sb,depth+1);if(++i<map.Count)sb.Append(',');sb.Append('\n');}
   sb.Append(pad).Append('}');return;
  }
  if(!(value is string)&&value is IEnumerable){
   var items=new List<object>();foreach(var item in (IEnumerable)value)items.Add(item);
   if(items.Count==0){sb.Append("[]");return;}
   sb.Append("[\n");
   for(int i=0;i<items.Count;i++){sb.Append(inner);Pretty(items[i],sb,depth+1);if(i+1<items.Count)sb.Append(',');sb.Append('\n');}
   sb.Append(pad).Append(']');return;
  }
  sb.Append(CommandPipe.Serialize(value));
 }
}
}
