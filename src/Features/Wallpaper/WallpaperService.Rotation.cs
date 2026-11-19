using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Forms = System.Windows.Forms;

namespace AWall {
// Rotation: settings.ini rotation / rotationMinutes / rotationOrder, the list in rotation.json, timer and next item.
public partial class WallpaperService {
 public static readonly int[] RotationChoices={5,15,30,60,180};
 public static string RotationLabel(int minutes){return minutes%60==0?(minutes/60)+" 小时":minutes+" 分钟";}
 bool rotationOn,rotationRandom; int rotationMinutes=15; DateTime nextRotation=DateTime.MaxValue; List<string> rotationList=new List<string>(); readonly Random random=new Random();
 public bool RotationOn {get {return rotationOn;}}
 public bool RotationRandom {get {return rotationRandom;}}
 public int RotationMinutes {get {return rotationMinutes;}}
 public int RotationListCount {get {return rotationList.Count;}}
 string RotationPath {get {return Path.Combine(data,"rotation.json");}}
 void RaiseRotationChanged(){var handler=RotationChanged;if(handler!=null)handler();}
 void SaveSetting(string key,string value){try{AppSettings.Set(key,value);}catch(Exception e){TransitionLog("save-setting-failed: "+e.Message);}}

 public void LoadSettings(){
  rotationOn=AppSettings.Get("rotation")=="on";rotationRandom=AppSettings.Get("rotationOrder")=="random";
  int minutes;if(int.TryParse(AppSettings.Get("rotationMinutes"),out minutes)&&minutes>0)rotationMinutes=minutes;
  try{if(File.Exists(RotationPath))rotationList=new JavaScriptSerializer().Deserialize<List<string>>(File.ReadAllText(RotationPath))??new List<string>();}catch{rotationList=new List<string>();}
 }
 void SaveRotationList(){try{File.WriteAllText(RotationPath,new JavaScriptSerializer().Serialize(rotationList));}catch(Exception e){Say("保存轮播列表失败："+e.Message);}}
 public bool InRotation(Item item){return item!=null&&rotationList.Any(x=>String.Equals(x,item.File,StringComparison.OrdinalIgnoreCase));}
 public void ToggleRotationItem(Item item){
  if(item==null)return;
  if(InRotation(item)){rotationList.RemoveAll(x=>String.Equals(x,item.File,StringComparison.OrdinalIgnoreCase));Say("已移出轮播："+item.Title+"（列表剩 "+rotationList.Count+" 项"+(rotationList.Count==0?"，将轮播整个素材库":"")+"）");}
  else{rotationList.Add(item.File);Say("已加入轮播："+item.Title+"（列表共 "+rotationList.Count+" 项）"+(rotationOn?"":"。打开下方“轮播”开关后生效。"));}
  SaveRotationList();RaiseRotationChanged();
 }
 // The rotation list, or the whole library when the list is empty.
 public List<Item> RotationCandidates(){
  var items=Items;
  if(rotationList.Count==0)return items.Where(x=>x.Supported).ToList();
  return rotationList.Select(f=>items.FirstOrDefault(x=>String.Equals(x.File,f,StringComparison.OrdinalIgnoreCase))).Where(x=>x!=null&&x.Supported&&File.Exists(x.File)).ToList();
 }
 public void ResetRotationClock(){nextRotation=rotationOn?DateTime.UtcNow.AddMinutes(rotationMinutes):DateTime.MaxValue;}
 public void SetRotation(bool on){
  rotationOn=on;SaveSetting("rotation",on?"on":"off");ResetRotationClock();RaiseRotationChanged();
  if(!on){Say("轮播已关闭。");return;}
  int count=RotationCandidates().Count;
  Say(count<2?"轮播已打开，但可用素材少于 2 个，暂时不会切换。":"轮播已打开：每 "+RotationLabel(rotationMinutes)+"从"+(rotationList.Count==0?"整个素材库":"轮播列表")+"（"+count+" 项）换一张"+(Current==null?"；先设一张壁纸，之后开始轮播。":"。"));
 }
 public void CycleRotationInterval(){int i=Array.IndexOf(RotationChoices,rotationMinutes);rotationMinutes=RotationChoices[(i+1)%RotationChoices.Length];SaveSetting("rotationMinutes",rotationMinutes.ToString());ResetRotationClock();RaiseRotationChanged();Say("轮播间隔："+RotationLabel(rotationMinutes)+"，从现在开始计时。");}
 public void ToggleRotationOrder(){rotationRandom=!rotationRandom;SaveSetting("rotationOrder",rotationRandom?"random":"sequential");RaiseRotationChanged();Say("轮播顺序："+(rotationRandom?"随机":"按素材库顺序")+"。");}
 // rotation.set: null leaves a value unchanged. The caller has validated the values.
 public void ConfigureRotation(int? minutes,string order,bool? on){
  if(minutes.HasValue){rotationMinutes=minutes.Value;SaveSetting("rotationMinutes",rotationMinutes.ToString());}
  if(order!=null){rotationRandom=order=="random";SaveSetting("rotationOrder",order);}
  if(on.HasValue)SetRotation(on.Value);else{ResetRotationClock();RaiseRotationChanged();}
 }
 void RotationTick(){
  if(!rotationOn||TestMode||exiting||DateTime.UtcNow<nextRotation)return;
  // Wait while the wallpaper cannot be seen; switch as soon as it can.
  if(transitioning||swapping||layout.Count==0||players.Count==0||manualPause||locked||coveredNow)return;
  RotateNow(false);
 }
 // Uniform desktop: every display moves to the same next item. Mixed displays: each advances on its own.
 public void RotateNow(bool manual){
  var candidates=RotationCandidates();ResetRotationClock();
  if(candidates.Count<2){if(manual)Say("可轮播的素材少于 2 个。");return;}
  if(transitioning||swapping||layout.Count==0)return;
  var screens=Forms.Screen.AllScreens;var plan=new Dictionary<string,Item>();
  bool uniform=layout.Values.Select(x=>ItemKey(x)).Distinct().Count()<=1;
  if(uniform){var next=NextAfter(Current,candidates);foreach(var s in screens)plan[s.DeviceName]=next;}
  else foreach(var s in screens){Item shown;layout.TryGetValue(s.DeviceName,out shown);plan[s.DeviceName]=NextAfter(shown??Current,candidates);}
  TransitionLog("rotation "+(manual?"manual":"timer"));ApplyRotation(plan);
 }
 Item NextAfter(Item shown,List<Item> candidates){
  if(rotationRandom){var others=candidates.Where(x=>!SameItem(x,shown)).ToList();return others.Count==0?candidates[0]:others[random.Next(others.Count)];}
  int i=candidates.FindIndex(x=>SameItem(x,shown));return candidates[(i+1)%candidates.Count];
 }
 async void ApplyRotation(Dictionary<string,Item> plan){try{await ApplyPlanAsync(false,plan);}catch(Exception e){Say("轮播切换失败："+e.Message);}}
}
}
