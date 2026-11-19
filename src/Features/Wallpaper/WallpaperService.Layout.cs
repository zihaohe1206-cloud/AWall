using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;

namespace AWall {
// Saved layout: wallpaper-layout.json write / restore (model in Core/Layout/SavedLayout.cs).
public partial class WallpaperService {
 string LayoutPath {get {return Path.Combine(data,TestMode?"wallpaper-layout-test.json":"wallpaper-layout.json");}}
 internal void SaveLayout(){
  if(transitioning||Current==null||players.Count==0||players.Any(x=>!x.Ready||x.Failed))return;
  try{var primary=Current;
   var value=new SavedLayout(){Title=primary.Title,Type=primary.Type,File=primary.File,Preview=primary.Preview,ManualPause=manualPause,PauseCovered=pauseWhenCovered,
    Screens=layout.Where(x=>x.Value!=null).Select(x=>new SavedScreen(){Device=x.Key,Title=x.Value.Title,Type=x.Value.Type,File=x.Value.File,Preview=x.Value.Preview}).ToList()};
   string temp=LayoutPath+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value));
   if(File.Exists(LayoutPath))File.Replace(temp,LayoutPath,null);else File.Move(temp,LayoutPath);
  }catch(Exception e){TransitionLog("save-layout-failed: "+e.Message);}
 }
 // A saved entry as a library item. Layouts saved before the library was reorganised point at
 // files that no longer exist; re-find them by title. Null when nothing usable is left.
 Item FindSaved(string title,string type,string file,string preview){
  if(String.IsNullOrEmpty(file)||(type!="video"&&type!="scene"&&type!="web"))return null;
  var items=Items;
  if(!File.Exists(file)){var moved=items.FirstOrDefault(x=>x.Type==type&&String.Equals(x.Title,title,StringComparison.OrdinalIgnoreCase));if(moved==null)return null;file=moved.File;preview=moved.Preview;}
  var item=items.FirstOrDefault(x=>String.Equals(x.File,file,StringComparison.OrdinalIgnoreCase)&&x.Type==type);
  if(item==null){item=new Item(){Title=title,Type=type,File=file,Preview=preview};items.Add(item);var handler=LibraryChanged;if(handler!=null)handler();}
  return item;
 }
 // Restores the layout of the last session (no-op when none was saved).
 public async Task RestoreLayoutAsync(){
  if(!File.Exists(LayoutPath))return;
  var saved=new JavaScriptSerializer().Deserialize<SavedLayout>(File.ReadAllText(LayoutPath));
  var primary=saved==null?null:FindSaved(saved.Title,saved.Type,saved.File,saved.Preview);
  if(primary==null)throw new IOException("上次壁纸文件不可用。");
  var plan=new Dictionary<string,Item>();
  if(saved.Screens!=null)foreach(var entry in saved.Screens){if(entry==null||String.IsNullOrEmpty(entry.Device))continue;var item=FindSaved(entry.Title,entry.Type,entry.File,entry.Preview);if(item!=null)plan[entry.Device]=item;}
  // Displays that were not saved (new, or renamed by Windows) get the primary wallpaper.
  foreach(var screen in Forms.Screen.AllScreens)if(!plan.ContainsKey(screen.DeviceName))plan[screen.DeviceName]=primary;
  pauseWhenCovered=saved.PauseCovered;
  var handler=Restoring;if(handler!=null)handler(primary);
  await ApplyPlanAsync(true,plan);manualPause=saved.ManualPause;Tick();
 }
 // Started by the Run key at sign-in: give Explorer a moment to create the desktop windows,
 // then restore; one retry if nothing could be shown yet.
 public async Task RestoreAfterLoginAsync(){
  await Task.Delay(3000);await RestoreLayoutAsync();await WaitForTransition();
  if(players.Count==0&&File.Exists(LayoutPath)){TransitionLog("autostart-restore-retry");await Task.Delay(10000);await RestoreLayoutAsync();}
 }
}
}
