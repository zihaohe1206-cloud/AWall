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
// Tray icon and menu. Every item runs a command (MainWindow.Commands.cs).
public partial class MainWindow {
 void BuildTray(bool test){
  tray=new Forms.NotifyIcon(){Text="AWall",Icon=Theme.TrayIcon(),Visible=!test};
  trayMenu=new Forms.ContextMenuStrip(){ShowImageMargin=false,Font=new System.Drawing.Font("Microsoft YaHei UI",9f),Padding=new Forms.Padding(2,4,2,4)};
  trayNow=new Forms.ToolStripLabel("未播放"){Padding=new Forms.Padding(4,4,4,4)};
  var open=new Forms.ToolStripMenuItem("打开 AWall",null,delegate{Run("app.show");}){Font=new System.Drawing.Font("Microsoft YaHei UI",9f,System.Drawing.FontStyle.Bold)};
  trayPause=new Forms.ToolStripMenuItem("暂停壁纸",null,delegate{Run("wallpaper.toggle-pause");});
  trayNext=new Forms.ToolStripMenuItem("下一张壁纸",null,delegate{Run("wallpaper.next");});
  trayStop=new Forms.ToolStripMenuItem("停止壁纸",null,delegate{Run("wallpaper.stop");});
  var trayIcons=new Forms.ToolStripMenuItem("隐藏桌面图标",null,delegate{Run("desktop.icons");});
  var quit=new Forms.ToolStripMenuItem("退出",null,delegate{Run("app.exit");});
  foreach(var item in new Forms.ToolStripMenuItem[]{open,trayPause,trayNext,trayStop,trayIcons,quit})item.Padding=new Forms.Padding(4,4,12,4);
  trayMenu.Items.AddRange(new Forms.ToolStripItem[]{trayNow,new Forms.ToolStripSeparator(),open,trayPause,trayNext,trayStop,trayIcons,new Forms.ToolStripSeparator(),quit});
  trayMenu.Opening+=delegate{bool active=current!=null||wallpaper.HasPlayers,paused=wallpaper.Paused;trayNow.Text=current==null?"未播放":(paused?"已暂停：":"正在播放：")+Shorten(NowTitle(),26);trayPause.Text=paused?"继续壁纸":"暂停壁纸";trayPause.Enabled=current!=null;trayNext.Enabled=current!=null&&wallpaper.RotationCandidates().Count>1;trayStop.Enabled=active;trayIcons.Text=DesktopIcons.HiddenByAWall?"显示桌面图标":"隐藏桌面图标";};
  ApplyMenuTheme(); tray.ContextMenuStrip=trayMenu;
  tray.MouseClick+=delegate(object s,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)ShowMain();};
 }
 void ApplyMenuTheme(){
  if(trayMenu==null)return;
  trayMenu.Renderer=new Forms.ToolStripProfessionalRenderer(new MenuColors(darkNow)){RoundedEdges=false};
  var text=darkNow?System.Drawing.Color.FromArgb(232,236,245):System.Drawing.Color.FromArgb(26,32,48);
  foreach(Forms.ToolStripItem item in trayMenu.Items)item.ForeColor=text;
  trayNow.ForeColor=darkNow?System.Drawing.Color.FromArgb(151,161,183):System.Drawing.Color.FromArgb(90,100,120);
 }
}
}
