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
// Saved-layout glue: the file is written and restored by WallpaperService.Layout.
public partial class MainWindow {
 async void RestoreLayout(){try{await wallpaper.RestoreLayoutAsync();}catch(Exception e){SetStatus("恢复上次壁纸失败："+e.Message);}}
 async void RestoreAfterLogin(){try{await wallpaper.RestoreAfterLoginAsync();}catch(Exception e){SetStatus("恢复上次壁纸失败："+e.Message);}}
}
}
