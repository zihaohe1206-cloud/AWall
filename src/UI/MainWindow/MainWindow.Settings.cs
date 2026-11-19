using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace AWall {
// Settings: MP4 backend, theme, autostart, settings.ini access (AppSettings).
public partial class MainWindow {
 // ---- MP4 backend (settings.ini videoBackend=builtin|mpv; --video-backend=... overrides for tests)
 void LoadVideoBackend(){
  string value=LoadSetting("videoBackend");
  var arg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--video-backend="));if(arg!=null)value=arg.Substring(16);
  ScenePlayer.VideoBackend=value=="mpv"?"mpv":"builtin";
 }
 // What new MP4 players actually use: mpv only when chosen and its files are present.
 static bool MpvActive {get {return ScenePlayer.VideoBackend=="mpv"&&ScenePlayer.MpvAvailable;}}
 void CycleVideoBackend(){
  if(ScenePlayer.VideoBackend!="mpv"&&!ScenePlayer.MpvAvailable){SetStatus("没有找到 "+ScenePlayer.MpvPath+"。需要 mpv 时用 build.ps1 -WithMpv 构建；内置播放器不需要它。");return;}
  ScenePlayer.VideoBackend=ScenePlayer.VideoBackend=="mpv"?"builtin":"mpv";SaveSetting("videoBackend",ScenePlayer.VideoBackend);UpdateSettingsUi();
  var selected=library.SelectedItem as Item;if(selected!=null)ShowDetail(selected);
  if(!wallpaper.Loading&&wallpaper.HasVideo){SetStatus("正在用"+(MpvActive?" mpv ":"内置播放器")+"重新加载 MP4…");ReloadVideos();}
  else SetStatus("MP4 播放器已切换为"+(MpvActive?" mpv":"内置播放器（WebView2）")+"，下次播放 MP4 时生效。");
 }
 async void ReloadVideos(){try{await wallpaper.ReloadAsync();}catch(Exception e){SetStatus(e.Message);}}
 void UpdateSettingsUi(){
  bool rotationOn=wallpaper.RotationOn;int listCount=wallpaper.RotationListCount;
  SetButtonText(rotationIntervalButton,"每 "+WallpaperService.RotationLabel(wallpaper.RotationMinutes)+"换一张");
  SetButtonText(rotationOrderButton,wallpaper.RotationRandom?"随机顺序":"按顺序");
  SetButtonText(videoBackendButton,"MP4："+(ScenePlayer.VideoBackend=="mpv"?(ScenePlayer.MpvAvailable?"mpv":"mpv（未找到，用内置）"):"内置播放器"));
  if(rotationIntervalButton!=null)rotationIntervalButton.IsEnabled=rotationOn;
  if(rotationOrderButton!=null)rotationOrderButton.IsEnabled=rotationOn;
  if(videoBackendButton!=null)videoBackendButton.ToolTip="内置播放器使用 Windows 自带的 WebView2，不需要额外文件；mpv 需要用 build.ps1 -WithMpv 构建（约 120 MB）。";
  if(settingsHint!=null)settingsHint.Text=(rotationOn?"轮播"+(listCount==0?"整个素材库":"列表中的 "+listCount+" 项")+"，暂停、锁屏或被全屏窗口遮挡时不切换。":"把素材“加入轮播”后打开“轮播”；列表为空时轮播整个素材库。")+"关闭窗口后驻留托盘。";
 }
 // ---- Start with Windows (registry details in Platform/Autostart.cs)
 void SetAutostart(bool on){
  try{Autostart.Set(on);
   SetStatus(on?"已开启开机自动启动：登录后只在托盘运行，并恢复上次的壁纸。":"已关闭开机自动启动。");
  }catch(Exception e){SetStatus("设置开机自动启动失败："+e.Message);}
  autostartSwitch.IsChecked=Autostart.Enabled;
 }
 void RepairAutostart(){try{string old=Autostart.Repair();if(old!=null)TransitionLog("autostart-repaired "+old+" -> "+Autostart.ExePath);}catch(Exception e){TransitionLog("autostart-repair-failed "+e.Message);}}
 // ---- settings.ini (AppSettings) and the library folder
 void LoadLibraryRoot(){
  try{string value=AppSettings.Get("libraryRoot");if(String.IsNullOrEmpty(value)||!Path.IsPathRooted(value))return;value=Path.GetFullPath(value).TrimEnd('\\');
   // Older builds saved library\wallpaper-engine; the typed layout lives one level up.
   if(Path.GetFileName(value).Equals("wallpaper-engine",StringComparison.OrdinalIgnoreCase)&&!Directory.Exists(Path.Combine(value,"pkg")))value=Path.GetDirectoryName(value);
   if(Path.GetFileName(value).Equals("library",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(Path.Combine(value,"wallpapers")))value=Path.Combine(value,"wallpapers");
   if(Directory.Exists(value))root=value;}catch(Exception e){TransitionLog("load-library-failed: "+e.Message);}
 }
 void SaveSettings(){try{AppSettings.Set("theme",themeMode);AppSettings.Set("libraryRoot",root);}catch(Exception e){SetStatus("保存设置失败："+e.Message);}}
 string LoadThemeMode(){string v=AppSettings.Get("theme");return v=="light"||v=="dark"||v=="system"?v:"system";}
 string LoadSetting(string key){return AppSettings.Get(key);}
 void SaveSetting(string key,string value){try{AppSettings.Set(key,value);}catch(Exception e){TransitionLog("save-setting-failed: "+e.Message);}}
 // ---- Theme: follow Windows (default) -> light -> dark, remembered in settings.ini.
 bool ResolveDark(){return themeMode=="dark"||(themeMode=="system"&&Theme.SystemPrefersDark());}
 void CycleTheme(){
  themeMode=themeMode=="system"?"light":themeMode=="light"?"dark":"system";
  SaveSettings();
  ApplyTheme();
 }
 void ApplyTheme(){
  darkNow=ResolveDark();Resources.MergedDictionaries[0]=Theme.Palette(darkNow);
  Theme.TitleBar(windowHandle,darkNow);ApplyMenuTheme();UpdateThemeButton();
 }
 void UpdateThemeButton(){
  if(themeGlyph==null)return;
  themeGlyph.Text=themeMode=="light"?"\uE706":themeMode=="dark"?"\uE708":"\uE770";
  themeButton.ToolTip="主题："+(themeMode=="light"?"浅色":themeMode=="dark"?"深色":"跟随系统")+"（点击切换）";
 }
 void PreferenceChanged(object s,UserPreferenceChangedEventArgs e){
  if(e.Category!=UserPreferenceCategory.General&&e.Category!=UserPreferenceCategory.Color)return;
  Dispatcher.BeginInvoke(new Action(delegate{if(themeMode=="system"&&ResolveDark()!=darkNow)ApplyTheme();}));
 }
}
}
