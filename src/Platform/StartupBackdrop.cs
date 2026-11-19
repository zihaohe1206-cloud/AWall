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
public static class StartupBackdrop {
 // Read the static system background; never capture application windows.
 public static BitmapSource Capture(Forms.Screen screen){
  string path="",style="10",tile="0",rgb="0 0 0";
  using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop")){if(key!=null){path=Convert.ToString(key.GetValue("WallPaper",""));style=Convert.ToString(key.GetValue("WallpaperStyle","10"));tile=Convert.ToString(key.GetValue("TileWallpaper","0"));}}
  using(var key=Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors")){if(key!=null)rgb=Convert.ToString(key.GetValue("Background","0 0 0"));}
  var parts=rgb.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);byte r=0,g=0,b=0;
  var color=parts.Length==3&&byte.TryParse(parts[0],out r)&&byte.TryParse(parts[1],out g)&&byte.TryParse(parts[2],out b)?Color.FromRgb(r,g,b):Colors.Black;
  BitmapImage image=null;
  try{if(File.Exists(path)){image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();}}catch{}
  var bounds=screen.Bounds;var visual=new DrawingVisual();
  using(var draw=visual.RenderOpen()){
   draw.DrawRectangle(new SolidColorBrush(color),null,new System.Windows.Rect(0,0,bounds.Width,bounds.Height));
   if(image!=null){
    var area=style=="22"?Forms.SystemInformation.VirtualScreen:bounds;
    double w=image.PixelWidth,h=image.PixelHeight,x=area.Left-bounds.Left,y=area.Top-bounds.Top;
    if(tile=="1"){
     var brush=new ImageBrush(image){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new System.Windows.Rect(0,0,w,h),Stretch=Stretch.Fill};
     draw.DrawRectangle(brush,null,new System.Windows.Rect(0,0,bounds.Width,bounds.Height));
    }else{
     if(style=="2"){w=area.Width;h=area.Height;}
     else if(style=="6"||style=="10"||style=="22"){double scale=style=="6"?Math.Min(area.Width/w,area.Height/h):Math.Max(area.Width/w,area.Height/h);w*=scale;h*=scale;}
     x+=(area.Width-w)/2;y+=(area.Height-h)/2;draw.DrawImage(image,new System.Windows.Rect(x,y,w,h));
    }
   }
  }
  var frame=new RenderTargetBitmap(bounds.Width,bounds.Height,96,96,PixelFormats.Pbgra32);frame.Render(visual);frame.Freeze();return frame;
 }
}
}
