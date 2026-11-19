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
// Rotation glue: the timer, list and settings live in WallpaperService.Rotation.
public partial class MainWindow {
 void ToggleRotationItem(){var item=library.SelectedItem as Item;if(item==null)return;wallpaper.ToggleRotationItem(item);ShowDetail(item);}
}
}
