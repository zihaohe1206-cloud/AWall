using System;
using System.IO;

namespace AWall {
// Where AWall keeps things. BaseDir is the build output (bin\ or bin-next\); the project folder
// is one level up, and everything the app depends on lives inside it (library\...).
// Per-user data, logs and settings live in %LOCALAPPDATA%\AWall.
public static class AppPaths {
 public static string BaseDir {get {return AppDomain.CurrentDomain.BaseDirectory;}}
 public static string Data {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall");}}
 public static string LibraryDir {get {return Path.GetFullPath(Path.Combine(BaseDir,"..","library"));}}
 // Typed wallpaper library: wallpapers\pkg, wallpapers\mp4, wallpapers\html.
 public static string DefaultWallpaperRoot {get {return Path.Combine(LibraryDir,"wallpapers");}}
 // Local copy of Wallpaper Engine's shared assets; PKG scenes read only from here.
 public static string WeAssets {get {return Path.Combine(LibraryDir,"we-assets");}}
 public static string InBase(string name){return Path.Combine(BaseDir,name);}
}
}
