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
public class Item {
 public string Title {get;set;} public string Type {get;set;} public string File {get;set;} public string Preview {get;set;}
 public string Badge {get {return Type=="video"?"MP4":Type=="scene"?"PKG":Type=="image"?"IMG":"WEB";}}
 // Resource-key prefix for the badge colours (VideoBack / VideoFore ...), so badges follow the theme.
 public string BadgeKey {get {return Type=="video"?"Video":Type=="scene"?"Scene":Type=="image"?"Image":"Web";}}
 ImageSource thumb; bool thumbLoaded;
 // Loaded lazily by the virtualized library list and decoded small to stay light.
 public ImageSource Thumb {get {if(!thumbLoaded){thumbLoaded=true;thumb=LoadImage(Preview,160);}return thumb;}}
 public static BitmapImage LoadImage(string path,int width){
  try{if(String.IsNullOrEmpty(path)||!System.IO.File.Exists(path))return null;
   using(var stream=System.IO.File.OpenRead(path)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;if(width>0)image.DecodePixelWidth=width;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}
  }catch{return null;}
 }
 public bool Supported {get {return Type=="video"||Type=="scene"||Type=="web"||Type=="image";}}
 // Still images Windows can show as its own wallpaper (the static presentation route).
 public static readonly string[] ImageExtensions={".jpg",".jpeg",".png",".bmp"};
 public static bool IsImageFile(string file){return Array.IndexOf(ImageExtensions,System.IO.Path.GetExtension(file??"").ToLowerInvariant())>=0;}
 // Library type from a file extension: .pkg scene, .html/.htm web page, still image, otherwise video.
 public static string TypeOf(string file){string ext=System.IO.Path.GetExtension(file).ToLowerInvariant();return ext==".pkg"?"scene":ext==".html"||ext==".htm"?"web":IsImageFile(file)?"image":"video";}
 // Drop the cached thumbnail after a preview image has been (re)generated.
 public void ResetThumb(){thumb=null;thumbLoaded=false;}
 public override string ToString() { return (Type == "video" ? "▶ MP4  " : Type=="scene" ? "▧ PKG  " : Type=="image" ? "▣ IMG  " : "◇ WEB  ") + Title; }
}
}
