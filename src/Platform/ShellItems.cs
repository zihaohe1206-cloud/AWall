using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AWall {
// One thing shown on the desktop: a file / folder from the user or public Desktop folder, or a shell
// item such as 回收站 / 此电脑 (Path is then "::{GUID}").
public sealed class DesktopEntry {
 public string Path,Name,Extension;
 public bool IsFolder,IsVirtual;
 public DateTime Created,Modified;
 // Identity used for free-fence assignments and selection.
 public string Key {get {return Path.ToLowerInvariant();}}
 public override string ToString(){return Name;}
}

// Shell access for desktop blocks: list desktop items, names and icons as Explorer shows them, open,
// rename / move / copy / recycle with undo, and the native context menu. Written for C# 5.
public static class ShellItems {
 // ---- Listing
 public static string UserDesktop {get {return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);}}
 public static string PublicDesktop {get {return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);}}
 public static bool IsOnDesktop(string path){
  if(String.IsNullOrEmpty(path))return false;
  if(path.StartsWith("::"))return true;
  string dir=System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
  return Same(dir,UserDesktop)||Same(dir,PublicDesktop);
 }
 static bool Same(string a,string b){return !String.IsNullOrEmpty(a)&&!String.IsNullOrEmpty(b)&&String.Equals(a.TrimEnd('\\'),b.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase);}
 // Shell icons Explorer can put on the desktop (Settings > 桌面图标设置), in Explorer's order.
 static readonly string[][] VirtualIcons={
  new[]{"{20D04FE0-3AEA-1069-A2D8-08002B30309D}","此电脑"},new[]{"{59031a47-3f72-44a7-89c5-5595fe6b30ee}","用户文件"},
  new[]{"{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}","网络"},new[]{"{645FF040-5081-101B-9F08-00AA002F954E}","回收站"},
  new[]{"{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}","控制面板"}};
 public static List<DesktopEntry> ListDesktop(){
  var list=new List<DesktopEntry>();
  try{
   using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel")){
    foreach(var v in VirtualIcons){
     object value=key==null?null:key.GetValue(v[0]);
     // Missing value: only 回收站 is shown by default.
     bool shown=value==null?v[0]=="{645FF040-5081-101B-9F08-00AA002F954E}":Convert.ToInt32(value)==0;
     if(shown)list.Add(new DesktopEntry(){Path="::"+v[0],Name=DisplayName("::"+v[0])??v[1],Extension="",IsVirtual=true});
    }
   }
  }catch{}
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var dir in new[]{UserDesktop,PublicDesktop})foreach(var e in ListFolder(dir))if(seen.Add(e.Name+"|"+e.IsFolder))list.Add(e);
  return list;
 }
 public static List<DesktopEntry> ListFolder(string dir){
  var list=new List<DesktopEntry>();
  if(String.IsNullOrEmpty(dir)||!Directory.Exists(dir))return list;
  try{
   foreach(var info in new DirectoryInfo(dir).EnumerateFileSystemInfos()){
    if((info.Attributes&(FileAttributes.Hidden|FileAttributes.System))!=0)continue;
    bool folder=(info.Attributes&FileAttributes.Directory)!=0;
    list.Add(new DesktopEntry(){Path=info.FullName,Name=DisplayName(info.FullName)??info.Name,Extension=folder?"":info.Extension.ToLowerInvariant(),IsFolder=folder,Created=info.CreationTime,Modified=info.LastWriteTime});
   }
  }catch{}
  return list.OrderBy(x=>x.IsFolder?0:1).ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
 }

 // ---- Names and icons
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileInfoW {public IntPtr Icon;public int IconIndex;public uint Attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(IntPtr pidl,uint attributes,ref FileInfoW info,uint size,uint flags);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHParseDisplayName(string name,IntPtr bindContext,out IntPtr pidl,uint attributesIn,out uint attributesOut);
 [DllImport("ole32.dll")] static extern void CoTaskMemFree(IntPtr pointer);
 [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
 static IntPtr Pidl(string path){IntPtr pidl;uint attributes;return SHParseDisplayName(path,IntPtr.Zero,out pidl,0,out attributes)==0?pidl:IntPtr.Zero;}
 // The name as Explorer shows it (no .lnk, extension hidden when Explorer hides it).
 public static string DisplayName(string path){
  IntPtr pidl=Pidl(path);if(pidl==IntPtr.Zero)return null;
  try{var info=new FileInfoW();return SHGetFileInfo(pidl,0,ref info,(uint)Marshal.SizeOf(typeof(FileInfoW)),0x200|0x8)!=IntPtr.Zero?info.DisplayName:null;}// SHGFI_DISPLAYNAME | SHGFI_PIDL
  finally{CoTaskMemFree(pidl);}
 }
 [StructLayout(LayoutKind.Sequential)] struct NativeSize {public int Width,Height;}
 [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellItemImageFactory {[PreserveSig] int GetImage(NativeSize size,int flags,out IntPtr bitmap);}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHCreateItemFromIDList(IntPtr pidl,[MarshalAs(UnmanagedType.LPStruct)] Guid riid,[MarshalAs(UnmanagedType.Interface)] out object item);
 [StructLayout(LayoutKind.Sequential)] struct NativeBitmap {public int Type,Width,Height,WidthBytes;public ushort Planes,BitsPixel;public IntPtr Bits;}
 [DllImport("gdi32.dll")] static extern int GetObject(IntPtr handle,int size,ref NativeBitmap bitmap);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
 // Icon or thumbnail (pictures) as Explorer draws it, size in pixels. Null when the shell has none.
 public static BitmapSource Icon(string path,int size){
  IntPtr pidl=Pidl(path);if(pidl==IntPtr.Zero)return null;
  try{
   object item;
   if(SHCreateItemFromIDList(pidl,typeof(IShellItemImageFactory).GUID,out item)==0&&item!=null){
    try{
     IntPtr hbitmap;
     if(((IShellItemImageFactory)item).GetImage(new NativeSize(){Width=size,Height=size},0,out hbitmap)==0&&hbitmap!=IntPtr.Zero){
      try{var b=FromDib(hbitmap);if(b!=null)return b;}finally{DeleteObject(hbitmap);}
     }
    }finally{Marshal.ReleaseComObject(item);}
   }
   var info=new FileInfoW();
   if(SHGetFileInfo(pidl,0,ref info,(uint)Marshal.SizeOf(typeof(FileInfoW)),0x100|0x8)!=IntPtr.Zero&&info.Icon!=IntPtr.Zero){// SHGFI_ICON | SHGFI_PIDL (large)
    try{var b=Imaging.CreateBitmapSourceFromHIcon(info.Icon,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());b.Freeze();return b;}finally{DestroyIcon(info.Icon);}
   }
  }catch{}finally{CoTaskMemFree(pidl);}
  return null;
 }
 // 32-bit DIB section with premultiplied alpha, usually bottom-up. CreateBitmapSourceFromHBitmap would drop the alpha.
 static BitmapSource FromDib(IntPtr hbitmap){
  var bm=new NativeBitmap();if(GetObject(hbitmap,Marshal.SizeOf(typeof(NativeBitmap)),ref bm)==0||bm.BitsPixel!=32||bm.Bits==IntPtr.Zero)return null;
  int w=bm.Width,h=Math.Abs(bm.Height),stride=w*4;var pixels=new byte[stride*h];var row=new byte[stride];
  for(int y=0;y<h;y++){Marshal.Copy(new IntPtr(bm.Bits.ToInt64()+(long)y*bm.WidthBytes),row,0,stride);Buffer.BlockCopy(row,0,pixels,(bm.Height>0?h-1-y:y)*stride,stride);}
  bool anyAlpha=false;for(int i=3;i<pixels.Length;i+=4)if(pixels[i]!=0){anyAlpha=true;break;}
  if(!anyAlpha)for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
  var source=BitmapSource.Create(w,h,96,96,PixelFormats.Pbgra32,null,pixels,stride);source.Freeze();return source;
 }

 // ---- Actions
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ShellExecuteInfo {
  public int Size;public uint Mask;public IntPtr Window;public string Verb,File,Parameters,Directory;public int Show;public IntPtr Instance,IdList;public string Class;public IntPtr ClassKey;public uint HotKey;public IntPtr Icon,Process;
 }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool ShellExecuteEx(ref ShellExecuteInfo info);
 // Double-click / Enter: the default verb, exactly as Explorer.
 public static bool Open(string path){
  IntPtr pidl=Pidl(path);if(pidl==IntPtr.Zero)return false;
  // SEE_MASK_INVOKEIDLIST; errors (missing target of a shortcut ...) are shown by the shell itself.
  try{var info=new ShellExecuteInfo(){Size=Marshal.SizeOf(typeof(ShellExecuteInfo)),Mask=0x0000000C,IdList=pidl,Show=1};return ShellExecuteEx(ref info);}
  finally{CoTaskMemFree(pidl);}
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct FileOperation {
  public IntPtr Window;public uint Function;public string From,To;public ushort Flags;[MarshalAs(UnmanagedType.Bool)] public bool Aborted;public IntPtr NameMappings;public string Title;
 }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SHFileOperation(ref FileOperation operation);
 const ushort AllowUndo=0x40,NoConfirmMkdir=0x200,RenameOnCollision=0x8;
 static string Multi(IEnumerable<string> paths){return String.Join("\0",paths.ToArray())+"\0\0";}
 // Explorer-style operations with undo (Ctrl+Z in Explorer) and the usual conflict / progress dialogs.
 public static bool Rename(string path,string newName,out string error){
  error=null;
  if(String.IsNullOrWhiteSpace(newName)||newName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars())>=0){error="文件名不能为空，也不能包含 \\ / : * ? \" < > |";return false;}
  string target=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),newName.Trim());
  if(String.Equals(target,path,StringComparison.Ordinal))return true;
  var op=new FileOperation(){Function=4,From=Multi(new[]{path}),To=target+"\0\0",Flags=AllowUndo};
  int code=SHFileOperation(ref op);if(code!=0||op.Aborted){error=op.Aborted?"已取消。":"重命名失败（"+code+"）。";return false;}
  return true;
 }
 public static bool Recycle(IList<string> paths){
  var files=paths.Where(x=>!x.StartsWith("::")).ToList();if(files.Count==0)return false;
  var op=new FileOperation(){Function=3,From=Multi(files),Flags=AllowUndo};return SHFileOperation(ref op)==0&&!op.Aborted;
 }
 // copy=false moves. Returns the paths the files have after the operation (RenameOnCollision may change names).
 public static bool Transfer(IList<string> paths,string folder,bool copy){
  var files=paths.Where(x=>!x.StartsWith("::")&&(File.Exists(x)||Directory.Exists(x))).ToList();if(files.Count==0)return false;
  var op=new FileOperation(){Function=copy?2u:1u,From=Multi(files),To=folder+"\0\0",Flags=(ushort)(AllowUndo|NoConfirmMkdir|(copy?RenameOnCollision:0))};
  return SHFileOperation(ref op)==0&&!op.Aborted;
 }
 // Explorer's default for a plain drag: move on the same volume, copy across volumes.
 public static bool DefaultIsCopy(IList<string> paths,string folder){
  try{string root=System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(folder));return paths.Any(x=>!x.StartsWith("::")&&!String.Equals(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(x)),root,StringComparison.OrdinalIgnoreCase));}catch{return true;}
 }

 // ---- Native context menu (IContextMenu / IContextMenu3), adapted in spirit from DeskFrame's
 // ShellContextMenu (MIT); written anew for C# 5 with a single parent folder per menu.
 [ComImport,Guid("000214E6-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IShellFolder {
  [PreserveSig] int ParseDisplayName(IntPtr hwnd,IntPtr bindContext,[MarshalAs(UnmanagedType.LPWStr)] string name,IntPtr eaten,out IntPtr pidl,IntPtr attributes);
  [PreserveSig] int EnumObjects(IntPtr hwnd,int flags,out IntPtr enumerator);
  [PreserveSig] int BindToObject(IntPtr pidl,IntPtr bindContext,ref Guid riid,out IntPtr result);
  [PreserveSig] int BindToStorage(IntPtr pidl,IntPtr bindContext,ref Guid riid,out IntPtr result);
  [PreserveSig] int CompareIDs(IntPtr lParam,IntPtr pidl1,IntPtr pidl2);
  [PreserveSig] int CreateViewObject(IntPtr hwnd,ref Guid riid,out IntPtr result);
  [PreserveSig] int GetAttributesOf(uint count,[MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,ref uint attributes);
  [PreserveSig] int GetUIObjectOf(IntPtr hwnd,uint count,[MarshalAs(UnmanagedType.LPArray)] IntPtr[] pidls,ref Guid riid,IntPtr reserved,out IntPtr result);
  [PreserveSig] int GetDisplayNameOf(IntPtr pidl,uint flags,IntPtr name);
  [PreserveSig] int SetNameOf(IntPtr hwnd,IntPtr pidl,[MarshalAs(UnmanagedType.LPWStr)] string name,uint flags,out IntPtr result);
 }
 [ComImport,Guid("000214e4-0000-0000-c000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IContextMenu {
  [PreserveSig] int QueryContextMenu(IntPtr menu,uint index,uint first,uint last,uint flags);
  [PreserveSig] int InvokeCommand(ref InvokeInfo info);
  [PreserveSig] int GetCommandString(UIntPtr command,uint type,IntPtr reserved,[MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,uint size);
 }
 [ComImport,Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
 interface IContextMenu3 {
  [PreserveSig] int QueryContextMenu(IntPtr menu,uint index,uint first,uint last,uint flags);
  [PreserveSig] int InvokeCommand(ref InvokeInfo info);
  [PreserveSig] int GetCommandString(UIntPtr command,uint type,IntPtr reserved,[MarshalAs(UnmanagedType.LPWStr)] StringBuilder name,uint size);
  [PreserveSig] int HandleMenuMsg(uint message,IntPtr wParam,IntPtr lParam);
  [PreserveSig] int HandleMenuMsg2(uint message,IntPtr wParam,IntPtr lParam,out IntPtr result);
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct InvokeInfo {
  public int Size;public uint Mask;public IntPtr Window,Verb;
  [MarshalAs(UnmanagedType.LPStr)] public string Parameters;[MarshalAs(UnmanagedType.LPStr)] public string Directory;
  public int Show;public uint HotKey;public IntPtr Icon;[MarshalAs(UnmanagedType.LPStr)] public string Title;
  public IntPtr VerbW;public string ParametersW,DirectoryW,TitleW;public int X,Y;
 }
 [DllImport("shell32.dll")] static extern int SHBindToParent(IntPtr pidl,[MarshalAs(UnmanagedType.LPStruct)] Guid riid,out IntPtr folder,out IntPtr last);
 [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
 [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
 [DllImport("user32.dll")] static extern int GetMenuItemCount(IntPtr menu);
 [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr menu,uint flags,int x,int y,IntPtr hwnd,IntPtr parameters);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
 const uint First=1,Last=0x7FFF;

 sealed class Menu : IDisposable {
  public IContextMenu Context;public IContextMenu3 Context3;public IntPtr Handle;readonly List<IntPtr> pidls=new List<IntPtr>();object folder;
  public static Menu Create(IntPtr owner,IList<string> paths,bool extended){
   var m=new Menu();
   try{
    // One parent folder per menu (Explorer's own limitation for a selection): keep the paths that share the first one's parent.
    string parent=paths[0].StartsWith("::")?"::":System.IO.Path.GetDirectoryName(paths[0]);
    var same=paths.Where(x=>(x.StartsWith("::")?"::":System.IO.Path.GetDirectoryName(x))==parent).ToList();
    var children=new List<IntPtr>();
    foreach(var p in same){
     IntPtr pidl=Pidl(p);if(pidl==IntPtr.Zero)continue;m.pidls.Add(pidl);
     IntPtr folderPtr,last;if(SHBindToParent(pidl,typeof(IShellFolder).GUID,out folderPtr,out last)!=0)continue;
     if(m.folder==null)m.folder=Marshal.GetObjectForIUnknown(folderPtr);
     Marshal.Release(folderPtr);children.Add(last);
    }
    if(m.folder==null||children.Count==0){m.Dispose();return null;}
    var iid=typeof(IContextMenu).GUID;IntPtr result;
    if(((IShellFolder)m.folder).GetUIObjectOf(owner,(uint)children.Count,children.ToArray(),ref iid,IntPtr.Zero,out result)!=0||result==IntPtr.Zero){m.Dispose();return null;}
    var obj=Marshal.GetObjectForIUnknown(result);Marshal.Release(result);
    m.Context=(IContextMenu)obj;m.Context3=obj as IContextMenu3;
    m.Handle=CreatePopupMenu();
    // CMF_EXPLORE | CMF_CANRENAME, CMF_EXTENDEDVERBS with Shift (like Explorer).
    m.Context.QueryContextMenu(m.Handle,0,First,Last,0x4|0x10|(extended?0x100u:0u));
    return m;
   }catch{m.Dispose();return null;}
  }
  public string Verb(uint command){
   var name=new StringBuilder(256);
   try{return Context.GetCommandString(new UIntPtr(command-First),4,IntPtr.Zero,name,256)==0?name.ToString():"";}catch{return "";}// GCS_VERBW
  }
  public void Invoke(IntPtr owner,uint command,int x,int y){
   var info=new InvokeInfo(){Size=Marshal.SizeOf(typeof(InvokeInfo)),Mask=0x4000|0x20000000,Window=owner,Verb=new IntPtr(command-First),VerbW=new IntPtr(command-First),Show=1,X=x,Y=y};// CMIC_MASK_UNICODE | CMIC_MASK_PTINVOKE
   if((System.Windows.Forms.Control.ModifierKeys&System.Windows.Forms.Keys.Control)!=0)info.Mask|=0x40000000;
   if((System.Windows.Forms.Control.ModifierKeys&System.Windows.Forms.Keys.Shift)!=0)info.Mask|=0x10000000;
   Context.InvokeCommand(ref info);
  }
  public void Dispose(){
   if(Handle!=IntPtr.Zero){DestroyMenu(Handle);Handle=IntPtr.Zero;}
   if(Context!=null){try{Marshal.ReleaseComObject(Context);}catch{}Context=null;Context3=null;}
   if(folder!=null){try{Marshal.ReleaseComObject(folder);}catch{}folder=null;}
   foreach(var p in pidls)CoTaskMemFree(p);pidls.Clear();
  }
 }
 // For tests: how many items the shell menu would have, without showing it.
 public static int MenuItemCount(IList<string> paths){
  using(var m=Menu.Create(IntPtr.Zero,paths,false)){return m==null?-1:GetMenuItemCount(m.Handle);}
 }
 // Shows Explorer's menu for the items at a screen point (pixels). "rename" is handed back to the caller,
 // which edits the name in place (the shell's rename needs an Explorer view). Returns the chosen verb or null.
 public static string ShowMenu(IntPtr owner,IList<string> paths,int x,int y,Action rename){
  if(paths==null||paths.Count==0)return null;
  bool extended=(System.Windows.Forms.Control.ModifierKeys&System.Windows.Forms.Keys.Shift)!=0;
  using(var m=Menu.Create(owner,paths,extended)){
   if(m==null)return null;
   var source=HwndSource.FromHwnd(owner);
   HwndSourceHook hook=delegate(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
    // Owner-drawn and lazily filled submenus (发送到, 打开方式, 新建).
    if(m.Context3!=null&&(message==0x117||message==0x2B||message==0x2C||message==0x120)){IntPtr r;if(m.Context3.HandleMenuMsg2((uint)message,wParam,lParam,out r)==0){handled=true;return r;}}
    return IntPtr.Zero;
   };
   if(source!=null)source.AddHook(hook);
   uint command;
   try{SetForegroundWindow(owner);command=TrackPopupMenuEx(m.Handle,0x100|0x2,x,y,owner,IntPtr.Zero);PostMessage(owner,0,IntPtr.Zero,IntPtr.Zero);}// TPM_RETURNCMD | TPM_RIGHTBUTTON
   finally{if(source!=null)source.RemoveHook(hook);}
   if(command<First)return null;
   string verb=m.Verb(command);
   if(String.Equals(verb,"rename",StringComparison.OrdinalIgnoreCase)){if(rename!=null)rename();return verb;}
   try{m.Invoke(owner,command,x,y);}catch(Exception e){throw new InvalidOperationException("执行菜单命令失败："+e.Message);}
   return verb;
  }
 }
}
}
