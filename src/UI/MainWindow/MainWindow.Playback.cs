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
// Playback glue: the controller lives in Features/Wallpaper/WallpaperService; the window forwards
// button clicks and Explorer's TaskbarCreated message to it and redraws on its events.
public partial class MainWindow {
 readonly WallpaperService wallpaper=new WallpaperService();
 // The primary display's wallpaper.
 Item current {get {return wallpaper.Current;}}
 void HookWallpaper(){
  wallpaper.TestMode=testMode;wallpaper.Library=delegate{return items;};
  wallpaper.Status+=SetStatus;
  wallpaper.Changed+=delegate{if(pauseCovered.IsChecked!=wallpaper.PauseWhenCovered)pauseCovered.IsChecked=wallpaper.PauseWhenCovered;UpdateNowPlaying();};
  wallpaper.RotationChanged+=delegate{rotationSwitch.IsChecked=wallpaper.RotationOn;UpdateSettingsUi();var item=library.SelectedItem as Item;if(item!=null)SetButtonText(rotationItemButton,wallpaper.InRotation(item)?"移出轮播":"加入轮播");};
  wallpaper.DisplaysChanged+=delegate{BuildTargets();};
  wallpaper.LibraryChanged+=delegate{Bind();};
  wallpaper.PreviewSaved+=delegate(Item item){var selected=library.SelectedItem;Bind();library.SelectedItem=selected;if(selected==item)ShowDetail(item);};
  wallpaper.Restoring+=delegate(Item primary){library.SelectedItem=primary;pauseCovered.IsChecked=wallpaper.PauseWhenCovered;};
 }
 void TransitionLog(string phase){wallpaper.TransitionLog(phase);}
 // "设为动态桌面": the selected item on the display(s) chosen in "应用到".
 async void Apply(){try{await wallpaper.ApplyChosenAsync(library.SelectedItem as Item,applyTarget);}catch(Exception e){SetStatus(e.Message);}}
 // Self-tests: apply the selected item without resetting recovery or the rotation clock.
 Task ApplyAsync(){return wallpaper.ApplyItemAsync(library.SelectedItem as Item,applyTarget);}
 readonly uint taskbarCreated=RegisterWindowMessage("TaskbarCreated");
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
 IntPtr ShellMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(taskbarCreated!=0&&(uint)message==taskbarCreated){wallpaper.ShellRestarted();DesktopIcons.ShellRestarted();fences.ShellRestarted();}return IntPtr.Zero;
 }
}
}
