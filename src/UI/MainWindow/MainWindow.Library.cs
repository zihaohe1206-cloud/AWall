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
// Library glue: import dialogs, Wallpaper Engine import, scan. File logic is in Core/Library.
public partial class MainWindow {
 void Import(){var dialog=new Microsoft.Win32.OpenFileDialog(){Filter="壁纸文件|*.mp4;*.pkg;*.html;*.htm|MP4 视频|*.mp4|PKG 场景|*.pkg|HTML 网页|*.html;*.htm",Multiselect=true};if(dialog.ShowDialog()==true){foreach(string picked in dialog.FileNames){string file=Item.TypeOf(picked)=="web"?LibraryScanner.CopyIntoLibrary(root,picked):picked;var item=items.FirstOrDefault(x=>String.Equals(x.File,file,StringComparison.OrdinalIgnoreCase));if(item==null){item=new Item(){Title=Path.GetFileNameWithoutExtension(file),Type=Item.TypeOf(file),File=file,Preview=Item.TypeOf(file)=="web"?LibraryScanner.SidecarPreview(file):Path.Combine(Path.GetDirectoryName(file),"preview.jpg")};items.Add(item);}Bind();library.SelectedItem=item;}SetStatus("已导入 "+dialog.FileNames.Length+" 个 MP4 / PKG / HTML 文件。");}}
 // Import from Wallpaper Engine (UI + copy logic in WeImport.cs): locate the workshop folder (last used,
 // Steam config, common folders, then a shallow drive search), list compatible items minus duplicates,
 // confirm, then copy into wallpapers\mp4 / wallpapers\pkg.
 string WeAssetsDir {get{return AppPaths.WeAssets;}}
 string PickWorkshop(){
  using(var d=new Forms.FolderBrowserDialog(){Description="没有自动找到 Wallpaper Engine 素材目录。请选择 Steam 库目录，或 steamapps\\workshop\\content\\431960 文件夹。"}){
   if(d.ShowDialog()!=Forms.DialogResult.OK)return null;
   string dir=d.SelectedPath;
   foreach(var tail in new[]{"steamapps\\workshop\\content\\431960","workshop\\content\\431960","content\\431960","431960"}){var p=Path.Combine(dir,tail);if(Directory.Exists(p))return p;}
   return dir;
  }
 }
 async void ImportFromWe(){
  Action<string> report=s=>Dispatcher.BeginInvoke(new Action(()=>SetStatus(s)));
  try{
   string history=LoadSetting("weWorkshop");
   SetStatus("正在查找 Wallpaper Engine 素材目录…");
   var found=await Task.Run(()=>WeLocator.Find(history,report));
   string workshop=found.FirstOrDefault();
   while(true){
    if(workshop==null){workshop=PickWorkshop();if(workshop==null){SetStatus("已取消导入。");return;}}
    string w=workshop,r=root;
    SetStatus("正在读取 "+w+" …");
    var scan=await Task.Run(()=>{int unsupported;var list=WeLibrary.Scan(w,out unsupported);WeLibrary.MarkDuplicates(list,r,report);return Tuple.Create(list,unsupported);});
    string assetsSource=WeLocator.AssetsFor(w);bool offerAssets=assetsSource!=null&&!Directory.Exists(WeAssetsDir);
    SetStatus("找到 "+scan.Item1.Count+" 个可用素材，请确认要导入的内容。");
    var dialog=new WeImportWindow(this,darkNow,w,scan.Item1,scan.Item2,offerAssets);
    bool? ok=dialog.ShowDialog();
    if(dialog.ChangeFolder){workshop=null;continue;}
    if(ok!=true){SetStatus("已取消导入。");return;}
    SaveSetting("weWorkshop",w);
    var chosen=dialog.Chosen;bool assets=offerAssets&&dialog.CopyAssets.IsChecked==true;
    if(chosen.Count==0&&!assets){SetStatus("没有选择要导入的素材。");return;}
    int count=await Task.Run(()=>{if(assets)WeLibrary.CopyTree(assetsSource,WeAssetsDir,report);return WeLibrary.Import(chosen,r,report);});
    Scan();
    SetStatus("已导入 "+count+" 个素材"+(assets?"，并复制了共享资源（重启 AWall 后 PKG 才会用上）":"")+"。");
    return;
   }
  }catch(Exception e){SetStatus("导入失败："+e.Message);}
 }
 void Scan(){
  items=LibraryScanner.Scan(root);Bind();if(items.Count>0)library.SelectedIndex=0;
  SetStatus("已扫描 "+items.Count+" 个素材，其中 "+items.Count(x=>x.Type=="video")+" 个视频、"+items.Count(x=>x.Type=="scene")+" 个 PKG 场景、"+items.Count(x=>x.Type=="web")+" 个网页。目录："+root);
  wallpaper.ScheduleStandby();
 }
}
}
