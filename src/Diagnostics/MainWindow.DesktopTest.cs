using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace AWall {
// --test-desktop: the three block-host experiments of roadmap step 5 plus the icon show / hide rules.
// Briefly toggles "显示桌面" (Win+D) twice so the desktop is visible for pixel checks, and briefly hides
// the desktop icons. Result: %LOCALAPPDATA%\AWall\desktop-test.json.
public partial class MainWindow {
 [StructLayout(LayoutKind.Sequential)] struct TestPoint {public int X,Y;}
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(TestPoint point);
 [DllImport("user32.dll")] static extern IntPtr ChildWindowFromPointEx(IntPtr parent,TestPoint point,uint flags);
 [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from,IntPtr to,ref TestPoint point,uint count);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
 [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd,int command);
 [DllImport("user32.dll")] static extern bool IsChild(IntPtr parent,IntPtr hwnd);
 [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
 [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
 static string ClassOf(IntPtr hwnd){var s=new System.Text.StringBuilder(128);Native.GetClassName(hwnd,s,128);return s.ToString();}
 // The point shows something other than the block (or a child of it, e.g. WebView2's window).
 static bool Covered(int x,int y,IntPtr block){var hit=HitAt(x,y);return !(hit==block||IsChild(block,hit));}
 // Pixels of the desktop's top-level window (Progman) rendered by PrintWindow(PW_RENDERFULLCONTENT).
 static System.Drawing.Color[] DesktopPixels(System.Drawing.Point[] points){
  var root=GetAncestor(DesktopHost.DefView(),2);Native.Rect r;Native.GetWindowRect(root,out r);
  var result=new System.Drawing.Color[points.Length];
  using(var bitmap=new System.Drawing.Bitmap(Math.Max(1,r.Right-r.Left),Math.Max(1,r.Bottom-r.Top)))using(var g=System.Drawing.Graphics.FromImage(bitmap)){
   IntPtr hdc=g.GetHdc();try{PrintWindow(root,hdc,2);}finally{g.ReleaseHdc(hdc);}
   for(int i=0;i<points.Length;i++){int x=points[i].X-r.Left,y=points[i].Y-r.Top;result[i]=x>=0&&y>=0&&x<bitmap.Width&&y<bitmap.Height?bitmap.GetPixel(x,y):System.Drawing.Color.Empty;}
  }
  return result;
 }

 static System.Drawing.Color ScreenPixel(int x,int y){
  using(var bitmap=new System.Drawing.Bitmap(1,1))using(var g=System.Drawing.Graphics.FromImage(bitmap)){g.CopyFromScreen(x,y,0,0,new System.Drawing.Size(1,1));return bitmap.GetPixel(0,0);}
 }
 static bool Near(System.Drawing.Color c,int r,int g,int b){return Math.Abs(c.R-r)<=24&&Math.Abs(c.G-g)<=24&&Math.Abs(c.B-b)<=24;}
 static string Hex(System.Drawing.Color c){return "#"+c.R.ToString("X2")+c.G.ToString("X2")+c.B.ToString("X2");}
 static void ToggleDesktop(){
  var type=Type.GetTypeFromProgID("Shell.Application");object shell=Activator.CreateInstance(type);
  try{type.InvokeMember("ToggleDesktop",BindingFlags.InvokeMethod,null,shell,null);}finally{Marshal.ReleaseComObject(shell);}
 }
 // The window at a screen point, as its top-most ancestor below the desktop (GA_ROOT would give Progman).
 static IntPtr HitAt(int x,int y){return WindowFromPoint(new TestPoint(){X=x,Y=y});}
 static IntPtr ChildOfDefViewAt(int x,int y){
  var view=DesktopHost.DefView();var p=new TestPoint(){X=x,Y=y};MapWindowPoints(IntPtr.Zero,view,ref p,1);
  return ChildWindowFromPointEx(view,p,0x1|0x4);// CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT
 }
 static string RafPage(string background,bool transparentBody){
  return "<!doctype html><html><head><meta charset='utf-8'><style>html,body{margin:0;height:100%;overflow:hidden;background:"+(transparentBody?"transparent":background)+"}"
   +"#bar{height:40px;background:rgb(0,200,0)}#n{position:absolute;right:6px;bottom:4px;font:12px Segoe UI;color:#fff}</style></head><body>"
   +(transparentBody?"<div id='bar'></div>":"")+"<div id='n'>0</div><script>window.frames_=0;function f(){window.frames_++;if(window.frames_%30==0)document.getElementById('n').textContent=window.frames_;requestAnimationFrame(f);}requestAnimationFrame(f);</script></body></html>";
 }
 static async Task<long> Frames(BlockWindow w){
  string s=await w.View.CoreWebView2.ExecuteScriptAsync("window.frames_");long n;return long.TryParse(s,out n)?n:-1;
 }

 async void TestDesktop(){
  var result=new Dictionary<string,object>();var steps=new List<object>();result["steps"]=steps;bool ok=true;
  var opened=new List<BlockWindow>();bool desktopShown=false;
  Action<string,bool,object> step=delegate(string name,bool pass,object detail){steps.Add(new Dictionary<string,object>{{"name",name},{"pass",pass},{"detail",detail}});if(!pass)ok=false;};
  string profile=Path.Combine(data,"WebView2","desktop-test");
  try{
   wallpaper.StopTimers();
   var screen=Forms.Screen.PrimaryScreen;var area=screen.WorkingArea;
   var rect=new System.Drawing.Rectangle(area.Left+area.Width/2-240,area.Top+area.Height/2-150,480,300);
   int cx=rect.Left+rect.Width/2,cy=rect.Top+rect.Height/2+40;
   result["screen"]=screen.DeviceName+" "+screen.Bounds.Width+"x"+screen.Bounds.Height;
   result["defView"]=DesktopHost.DefView().ToInt64();result["iconList"]=DesktopHost.IconList().ToInt64();

   // ---- Experiment 1: native WPF block under DefView (fences are this kind).
   var nativeContent=new Border(){Background=new SolidColorBrush(Color.FromRgb(255,0,200)),Child=new TextBlock(){Text="AWall 实验 1",Foreground=Brushes.White,Margin=new Thickness(8)}};
   var native=new BlockWindow(nativeContent);opened.Add(native);
   var watch=Stopwatch.StartNew();native.Mount(rect);long mountMs=watch.ElapsedMilliseconds;
   await Task.Delay(600);
   step("native.parent-is-defview",DesktopHost.IsAboveIcons(native.Handle),new{mountMs=mountMs,visible=DesktopHost.IsVisible(native.Handle)});
   var childHit=ChildOfDefViewAt(cx,cy);
   step("native.above-icon-list",childHit==native.Handle,new{hit=childHit.ToInt64(),block=native.Handle.ToInt64(),iconList=DesktopHost.IconList().ToInt64()});
   var perfNative=await PerfProbe.Measure(3);result["perfNativeOnly"]=perfNative;

   // ---- Is the desktop visible at the test point? Screen pixels and hit tests only mean something then.
   // A full-screen app (game, presentation) is never disturbed: no Win+D, screen checks are skipped.
   int notify=0;SHQueryUserNotificationState(out notify);result["notificationState"]=notify;
   bool fullscreen=notify==2||notify==3||notify==4;
   bool onScreen=!Covered(cx,cy,native.Handle);
   if(!onScreen&&!fullscreen){ToggleDesktop();desktopShown=true;await Task.Delay(1500);onScreen=!Covered(cx,cy,native.Handle);}
   result["screenChecks"]=onScreen?(desktopShown?"on (after Win+D)":"on"):"skipped, covered by "+ClassOf(HitAt(cx,cy))+(fullscreen?" (full screen)":"");
   if(onScreen){
    var nativePixel=ScreenPixel(cx,cy);
    step("screen.native-visible",Near(nativePixel,255,0,200),Hex(nativePixel));
    if(desktopShown)step("win+d.block-stays-on-desktop",true,null);
   }
   // PrintWindow of the desktop's top-level window renders DefView's children even when they are covered.
   var printed=DesktopPixels(new[]{new System.Drawing.Point(cx,cy)});
   step("print.native-visible",Near(printed[0],255,0,200),Hex(printed[0]));

   // ---- Experiment 3: transparent web block over the native block.
   var webA=BlockWindow.Web(true);opened.Add(webA);
   await webA.LoadAsync(RafPage("transparent",true),profile);
   webA.Mount(rect);await Task.Delay(1000);
   var probe=new[]{new System.Drawing.Point(cx,rect.Top+20),new System.Drawing.Point(cx,cy)};
   // Pass = the page rendered (green bar). Whether the hole shows the magenta block below is the finding.
   var a=onScreen?new[]{ScreenPixel(probe[0].X,probe[0].Y),ScreenPixel(probe[1].X,probe[1].Y)}:DesktopPixels(probe);
   step("transparent.A.window-background",Near(a[0],0,200,0),new{source=onScreen?"screen":"print",bar=Hex(a[0]),hole=Hex(a[1])});
   result["transparentA"]=Near(a[1],255,0,200)?"works":"opaque ("+Hex(a[1])+")";

   // Variant B: colour key on the child window; the WPF background is the key colour behind the transparent page.
   var key=Color.FromRgb(1,2,3);webA.Background=new SolidColorBrush(key);int keyError;bool keySet=webA.ColorKey(key,out keyError);await Task.Delay(1000);
   var b=onScreen?new[]{ScreenPixel(probe[0].X,probe[0].Y),ScreenPixel(probe[1].X,probe[1].Y)}:DesktopPixels(probe);
   step("transparent.B.color-key",Near(b[0],0,200,0),new{source=onScreen?"screen":"print",keySet=keySet,keyError=keyError,bar=Hex(b[0]),hole=Hex(b[1])});
   result["transparentB"]=!keySet?"SetLayeredWindowAttributes failed ("+keyError+")":Near(b[1],255,0,200)?"works":"opaque ("+Hex(b[1])+")";
   webA.Close();opened.Remove(webA);await Task.Delay(400);

   if(onScreen){
    var nativeHit=HitAt(cx,cy);
    step("screen.native-gets-clicks",nativeHit==native.Handle||IsChild(native.Handle,nativeHit),new{hit=nativeHit.ToInt64(),cls=ClassOf(nativeHit)});
   }
   if(desktopShown){ToggleDesktop();desktopShown=false;await Task.Delay(1200);}
   native.Close();opened.Remove(native);

   // ---- Experiment 2: WebView2 block taken out of DefView ("揭开") and put back.
   var web=BlockWindow.Web(false);opened.Add(web);
   await web.LoadAsync(RafPage("rgb(0,90,255)",false),profile);
   web.Mount(rect);await Task.Delay(800);
   long f1=await Frames(web);
   watch.Restart();web.Reveal();long revealMs=watch.ElapsedMilliseconds;
   // Sample the centre every ~15 ms for 400 ms: anything not blue counts as a flicker sample.
   int samples=0,bad=0;var sampleWatch=Stopwatch.StartNew();
   while(sampleWatch.ElapsedMilliseconds<400){await Task.Delay(15);samples++;if(!Near(ScreenPixel(cx,cy),0,90,255))bad++;}
   var revealPixel=ScreenPixel(cx,cy);var cornerPixel=ScreenPixel(rect.Right-12,rect.Top+12);
   long f2=await Frames(web);
   step("reveal.topmost-visible",Near(revealPixel,0,90,255)&&Near(cornerPixel,0,90,255),new{revealMs=revealMs,center=Hex(revealPixel),corner=Hex(cornerPixel),samples=samples,nonBlueSamples=bad});
   step("reveal.page-keeps-rendering",f2>f1,new{before=f1,after=f2});
   watch.Restart();web.Unreveal();long backMs=watch.ElapsedMilliseconds;await Task.Delay(500);
   long f3=await Frames(web);
   step("unreveal.back-in-defview",DesktopHost.IsAboveIcons(web.Handle)&&f3>f2,new{backMs=backMs,frames=f3});
   // Second round trip: the reparent must be repeatable.
   web.Reveal();await Task.Delay(300);var again=ScreenPixel(cx,cy);web.Unreveal();await Task.Delay(300);long f4=await Frames(web);
   step("reveal.repeatable",Near(again,0,90,255)&&DesktopHost.IsAboveIcons(web.Handle)&&f4>f3,new{center=Hex(again),frames=f4});
   var perfWeb=await PerfProbe.Measure(3);result["perfWebBlock"]=perfWeb;
   web.Close();opened.Remove(web);

   // ---- Icon show / hide and crash recovery.
   bool before=DesktopIcons.Visible;result["iconsVisibleBefore"]=before;
   if(before){
    DesktopIcons.SetVisible(false);await Task.Delay(300);bool hidden=!DesktopIcons.Visible&&DesktopIcons.HiddenByAWall;
    DesktopIcons.Restore();await Task.Delay(300);bool shown=DesktopIcons.Visible&&!DesktopIcons.HiddenByAWall;
    step("icons.hide-and-restore",hidden&&shown,new{hidden=hidden,shown=shown});
    // Simulated crash: AWall hid the icons and died; the next start must show them.
    DesktopIcons.SetVisible(false);bool recovered=DesktopIcons.RecoverAfterCrash();await Task.Delay(300);
    step("icons.recover-after-crash",recovered&&DesktopIcons.Visible&&!DesktopIcons.HiddenByAWall,new{recovered=recovered});
    // Explorer's own "显示桌面图标 = off" must not be undone by Restore.
    ShowWindow(DesktopHost.IconList(),0);DesktopIcons.Restore();bool respected=!DesktopIcons.Visible;ShowWindow(DesktopHost.IconList(),5);
    step("icons.restore-leaves-user-setting",respected,null);
   }else step("icons.skipped",true,"桌面图标本来就是隐藏的，跳过");
  }catch(Exception e){ok=false;result["error"]=e.ToString();}
  finally{
   if(desktopShown)try{ToggleDesktop();}catch{}
   foreach(var w in opened.ToArray())try{w.Close();}catch{}
   DesktopIcons.Restore();
  }
  result["success"]=ok;
  File.WriteAllText(Path.Combine(data,"desktop-test.json"),new JavaScriptSerializer().Serialize(result));
  await Task.Delay(500);Exit();
 }
}
}
