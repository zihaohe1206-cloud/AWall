using System;
using System.Linq;
using System.Collections.Generic;

namespace AWall {
// Every user-visible action is a named command ("wallpaper.next", "rotation.set" ...). The tray, the main
// window and later a CLI / named pipe / MCP bridge call the same entries, so nothing is reachable only by
// clicking. Handlers run on the UI thread; external callers must marshal onto it before Execute.
public sealed class CommandResult {
 public bool Ok; public string Message; public object Data;
 public static CommandResult Success(string message){return new CommandResult(){Ok=true,Message=message};}
 public static CommandResult Success(string message,object data){return new CommandResult(){Ok=true,Message=message,Data=data};}
 public static CommandResult Fail(string message){return new CommandResult(){Ok=false,Message=message};}
}
// Named string arguments, e.g. parsed from "wallpaper.apply file=C:\a.mp4 display=2".
public sealed class CommandArgs {
 readonly Dictionary<string,string> values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 public CommandArgs Set(string key,string value){values[key]=value;return this;}
 public string Get(string key){string value;return values.TryGetValue(key,out value)?value:null;}
 public bool Has(string key){return values.ContainsKey(key);}
 // on/true/1/yes -> true, off/false/0/no -> false, anything else -> fallback.
 public bool Flag(string key,bool fallback){
  string v=(Get(key)??"").Trim().ToLowerInvariant();
  if(v=="on"||v=="true"||v=="1"||v=="yes")return true;if(v=="off"||v=="false"||v=="0"||v=="no")return false;return fallback;
 }
 public bool IsFlag(string key){string v=(Get(key)??"").Trim().ToLowerInvariant();return v=="on"||v=="true"||v=="1"||v=="yes"||v=="off"||v=="false"||v=="0"||v=="no";}
 public static CommandArgs Parse(IEnumerable<string> tokens){
  var args=new CommandArgs();
  if(tokens!=null)foreach(var token in tokens){if(token==null)continue;int at=token.IndexOf('=');if(at>0)args.Set(token.Substring(0,at).Trim(),token.Substring(at+1));}
  return args;
 }
 public IDictionary<string,string> ToDictionary(){return new Dictionary<string,string>(values);}
}
public sealed class CommandInfo {public string Name,Usage,Description;}
public sealed class CommandBus {
 sealed class Entry {public CommandInfo Info;public Func<CommandArgs,CommandResult> Run;}
 readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
 // Raised after every command (for logging / UI refresh). Exceptions from handlers are swallowed.
 public event Action<string,CommandArgs,CommandResult> Executed;
 public void Register(string name,string usage,string description,Func<CommandArgs,CommandResult> run){
  if(String.IsNullOrEmpty(name)||run==null)throw new ArgumentException("命令名和处理函数不能为空。");
  if(entries.ContainsKey(name))throw new InvalidOperationException("命令重复注册："+name);
  entries[name]=new Entry(){Info=new CommandInfo(){Name=name,Usage=String.IsNullOrEmpty(usage)?name:usage,Description=description??""},Run=run};
 }
 public bool Has(string name){return name!=null&&entries.ContainsKey(name);}
 public List<CommandInfo> List(){return entries.Values.Select(x=>x.Info).OrderBy(x=>x.Name,StringComparer.Ordinal).ToList();}
 // Never throws: unknown commands and handler exceptions come back as failed results.
 public CommandResult Execute(string name,CommandArgs args){
  if(args==null)args=new CommandArgs();
  Entry entry;CommandResult result;
  if(name==null||!entries.TryGetValue(name,out entry))result=CommandResult.Fail("未知命令："+name);
  else{try{result=entry.Run(args)??CommandResult.Success("");}catch(Exception e){result=CommandResult.Fail(e.Message);}}
  var handler=Executed;if(handler!=null){try{handler(name,args,result);}catch{}}
  return result;
 }
 public CommandResult Execute(string name){return Execute(name,null);}
}
}
