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
 public string Badge {get {return Type=="video"?"MP4":Type=="scene"?"PKG":"WEB";}}
 // Resource-key prefix for the badge colours (VideoBack / VideoFore ...), so badges follow the theme.
 public string BadgeKey {get {return Type=="video"?"Video":Type=="scene"?"Scene":"Web";}}
 ImageSource thumb; bool thumbLoaded;
 // Loaded lazily by the virtualized library list and decoded small to stay light.
 public ImageSource Thumb {get {if(!thumbLoaded){thumbLoaded=true;thumb=LoadImage(Preview,160);}return thumb;}}
 public static BitmapImage LoadImage(string path,int width){
  try{if(String.IsNullOrEmpty(path)||!System.IO.File.Exists(path))return null;
   using(var stream=System.IO.File.OpenRead(path)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;if(width>0)image.DecodePixelWidth=width;image.StreamSource=stream;image.EndInit();image.Freeze();return image;}
  }catch{return null;}
 }
 public bool Supported {get {return Type=="video"||Type=="scene"||Type=="web";}}
 // Library type from a file extension: .pkg scene, .html/.htm web page, otherwise video.
 public static string TypeOf(string file){string ext=System.IO.Path.GetExtension(file).ToLowerInvariant();return ext==".pkg"?"scene":ext==".html"||ext==".htm"?"web":"video";}
 // Drop the cached thumbnail after a preview image has been (re)generated.
 public void ResetThumb(){thumb=null;thumbLoaded=false;}
 public override string ToString() { return (Type == "video" ? "▶ MP4  " : Type=="scene" ? "▧ PKG  " : "◇ WEB  ") + Title; }
}
public static class Theme {
 // Status colours that read well on both palettes.
 public static readonly SolidColorBrush Ok=Hex("#22C38E"),Warn=Hex("#F5B014"),Idle=Hex("#8A93A6"),Accent=Hex("#7C6CFF");
 static readonly string[] Keys={"Bg","Surface","Line","Text","Muted","Faint","PreviewBack","Hover","Selected","SelectedLine","ButtonBack","ButtonLine","InputBack","TrackOff","KnobOff","ScrollThumb","ScrollThumbHover","IconFaint","Overlay","Accent","DangerFore","DangerBack","DangerLine","VideoBack","VideoFore","SceneBack","SceneFore","WebBack","WebFore"};
 const string DarkColors="#0E131E #151B29 #252E43 #E8ECF5 #97A1B7 #667089 #0A0E17 #1B2234 #242D46 #323D5C #1F273A #2C364E #0F1421 #2A3349 #AEB7CB #323C55 #4A5676 #343E57 #FFFFFF #8B7CFF #FF9EAA #2A1D29 #4A2A3C #123A40 #5EEAD4 #2B2558 #B3A8FF #252C3C #8C97AD";
 const string LightColors="#F3F5FA #FFFFFF #E2E6EF #1A2030 #5A6478 #8A93A6 #EEF1F6 #F1F3F8 #EDEAFF #D6D0FF #F4F6FA #DDE2EC #F6F7FB #D3D9E4 #FFFFFF #CBD2DF #AEB7C8 #C3CAD8 #000000 #6D55F5 #C2334A #FDECEF #F5C6CF #DDF7F2 #0F8B7A #ECE8FF #5B45D6 #EEF0F4 #6B7385";
 public static ResourceDictionary Palette(bool dark){var palette=new ResourceDictionary();var colors=(dark?DarkColors:LightColors).Split(' ');for(int i=0;i<Keys.Length;i++)palette[Keys[i]]=Hex(colors[i]);return palette;}
 // Windows "app mode" setting; AppsUseLightTheme=0 means dark. Missing value = light (Windows default).
 public static bool SystemPrefersDark(){
  try{using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")){object value=key==null?null:key.GetValue("AppsUseLightTheme");return value is int&&(int)value==0;}}catch{return false;}
 }
 public static SolidColorBrush Hex(string hex){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Freeze();return brush;}
 static string AssetPath(string name){return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name);}
 public static ResourceDictionary Load(out string error){
  error=null;try{return (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Xaml);}catch(Exception e){error=e.Message;return new ResourceDictionary();}
 }
 // Window / taskbar icon: WPF picks the best frame from the multi-size ICO.
 public static ImageSource AppIcon(){
  try{string path=AssetPath("AWall.ico");if(File.Exists(path))return BitmapFrame.Create(new Uri(path),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);}catch{}
  return null;
 }
 // Smallest ICO frame that is at least `size` pixels, for the in-window logo.
 public static ImageSource Logo(int size){
  try{string path=AssetPath("AWall.ico");if(!File.Exists(path))return null;
   var decoder=new IconBitmapDecoder(new Uri(path),BitmapCreateOptions.None,BitmapCacheOption.OnLoad);BitmapFrame best=null;
   foreach(var frame in decoder.Frames){bool big=frame.PixelWidth>=size;bool bestBig=best!=null&&best.PixelWidth>=size;
    if(best==null||(big&&(!bestBig||frame.PixelWidth<best.PixelWidth))||(!big&&!bestBig&&frame.PixelWidth>best.PixelWidth))best=frame;}
   return best;
  }catch{return null;}
 }
 // Notification-area icon at the exact small-icon size for the current DPI
 // (16 / 20 / 24 / 32 px) instead of the generic system application icon.
 public static System.Drawing.Icon TrayIcon(){
  try{string path=AssetPath("AWall.ico");if(File.Exists(path))return new System.Drawing.Icon(path,Forms.SystemInformation.SmallIconSize);}catch{}
  try{var embedded=System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetEntryAssembly().Location);if(embedded!=null)return embedded;}catch{}
  return System.Drawing.SystemIcons.Application;
 }
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h,int attribute,ref int value,int size);
 // Title bar matching the palette: immersive dark mode (Win10 1809+ / Win11) and,
 // on Win11, caption, text and border colours.
 public static void TitleBar(IntPtr handle,bool dark){
  if(handle==IntPtr.Zero)return;
  try{int on=dark?1:0;if(DwmSetWindowAttribute(handle,20,ref on,4)!=0)DwmSetWindowAttribute(handle,19,ref on,4);
   int caption=dark?0x001E130E:0x00FAF5F3;DwmSetWindowAttribute(handle,35,ref caption,4);
   int text=dark?0x00F5ECE8:0x0030201A;DwmSetWindowAttribute(handle,36,ref text,4);
   int border=dark?0x00432E25:0x00EFE6E2;DwmSetWindowAttribute(handle,34,ref border,4);}catch{}
 }
 const string Xaml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <FontFamily x:Key='IconFont'>Segoe Fluent Icons, Segoe MDL2 Assets</FontFamily>
 <Style x:Key='BaseButton' TargetType='Button'>
  <Setter Property='Foreground' Value='{DynamicResource Text}'/>
  <Setter Property='Background' Value='{DynamicResource ButtonBack}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource ButtonLine}'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='14,8,16,8'/>
  <Setter Property='Margin' Value='0,0,8,8'/>
  <Setter Property='MinHeight' Value='36'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='Button'>
     <Grid SnapsToDevicePixels='True'>
      <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>
      <Border x:Name='hl' CornerRadius='8' Background='{DynamicResource Overlay}' Opacity='0'/>
      <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
     </Grid>
     <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.07'/></Trigger>
      <Trigger Property='IsPressed' Value='True'><Setter TargetName='hl' Property='Opacity' Value='0.14'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
 <Style x:Key='SoftButton' TargetType='Button' BasedOn='{StaticResource BaseButton}'/>
 <Style x:Key='IconButton' TargetType='Button' BasedOn='{StaticResource BaseButton}'>
  <Setter Property='Padding' Value='10,8'/>
  <Setter Property='Margin' Value='0,0,10,0'/>
  <Setter Property='MinWidth' Value='38'/>
 </Style>
 <Style x:Key='PrimaryButton' TargetType='Button' BasedOn='{StaticResource BaseButton}'>
  <Setter Property='Foreground' Value='#FFFFFF'/>
  <Setter Property='BorderThickness' Value='0'/>
  <Setter Property='FontWeight' Value='SemiBold'/>
  <Setter Property='Background'>
   <Setter.Value><LinearGradientBrush StartPoint='0,0' EndPoint='1,1'><GradientStop Color='#7C61FF' Offset='0'/><GradientStop Color='#3C8CF0' Offset='1'/></LinearGradientBrush></Setter.Value>
  </Setter>
 </Style>
 <Style x:Key='DangerButton' TargetType='Button' BasedOn='{StaticResource BaseButton}'>
  <Setter Property='Foreground' Value='{DynamicResource DangerFore}'/>
  <Setter Property='Background' Value='{DynamicResource DangerBack}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource DangerLine}'/>
 </Style>
 <Style x:Key='SlimScrollBar' TargetType='ScrollBar'>
  <Setter Property='Width' Value='8'/>
  <Setter Property='MinWidth' Value='8'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='ScrollBar'>
     <Track x:Name='PART_Track' IsDirectionReversed='True' Orientation='Vertical'>
      <Track.Thumb>
       <Thumb>
        <Thumb.Template>
         <ControlTemplate TargetType='Thumb'>
          <Border x:Name='t' CornerRadius='3' Background='{DynamicResource ScrollThumb}' Margin='2,0'/>
          <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='{DynamicResource ScrollThumbHover}'/></Trigger></ControlTemplate.Triggers>
         </ControlTemplate>
        </Thumb.Template>
       </Thumb>
      </Track.Thumb>
     </Track>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
 <Style x:Key='Library' TargetType='ListBox'>
  <Setter Property='Background' Value='Transparent'/>
  <Setter Property='BorderThickness' Value='0'/>
  <Setter Property='Foreground' Value='{DynamicResource Text}'/>
  <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
  <Setter Property='VirtualizingPanel.IsVirtualizing' Value='True'/>
  <Setter Property='VirtualizingPanel.VirtualizationMode' Value='Recycling'/>
  <Setter Property='VirtualizingPanel.ScrollUnit' Value='Pixel'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='ListBox'>
     <ScrollViewer Focusable='False' Padding='0'><ItemsPresenter/></ScrollViewer>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
  <Style.Resources>
   <Style TargetType='ScrollBar' BasedOn='{StaticResource SlimScrollBar}'/>
   <Style TargetType='ListBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Margin' Value='0,0,4,4'/>
    <Setter Property='Padding' Value='8'/>
    <Setter Property='Cursor' Value='Hand'/>
    <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
    <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
    <Setter Property='Template'>
     <Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
       <Grid>
        <Border x:Name='bd' CornerRadius='8' Background='Transparent' BorderBrush='Transparent' BorderThickness='1' Padding='{TemplateBinding Padding}'>
         <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}'/>
        </Border>
        <Border x:Name='bar' Width='3' CornerRadius='1.5' Background='{DynamicResource Accent}' HorizontalAlignment='Left' Margin='0,14' Visibility='Collapsed'/>
       </Grid>
       <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource Hover}'/></Trigger>
        <Trigger Property='IsSelected' Value='True'>
         <Setter TargetName='bd' Property='Background' Value='{DynamicResource Selected}'/>
         <Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource SelectedLine}'/>
         <Setter TargetName='bar' Property='Visibility' Value='Visible'/>
        </Trigger>
       </ControlTemplate.Triggers>
      </ControlTemplate>
     </Setter.Value>
    </Setter>
   </Style>
  </Style.Resources>
 </Style>
 <DataTemplate x:Key='ItemTemplate'>
  <Grid>
   <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions>
   <Border Width='76' Height='44' CornerRadius='6' Background='{DynamicResource PreviewBack}'>
    <Grid>
     <TextBlock Text='&#xEB9F;' FontFamily='{StaticResource IconFont}' FontSize='16' Foreground='{DynamicResource IconFaint}' HorizontalAlignment='Center' VerticalAlignment='Center'/>
     <Border CornerRadius='6'><Border.Background><ImageBrush ImageSource='{Binding Thumb}' Stretch='UniformToFill'/></Border.Background></Border>
    </Grid>
   </Border>
   <StackPanel Grid.Column='1' Margin='12,0,4,0' VerticalAlignment='Center' ToolTip='{Binding Title}'>
    <TextBlock Text='{Binding Title}' TextTrimming='CharacterEllipsis' FontSize='13'/>
    <Border x:Name='badge' Margin='0,6,0,0' HorizontalAlignment='Left' CornerRadius='4' Padding='6,0,6,1' Background='{DynamicResource WebBack}'>
     <TextBlock x:Name='badgeText' Text='{Binding Badge}' FontSize='10.5' FontWeight='SemiBold' Foreground='{DynamicResource WebFore}'/>
    </Border>
   </StackPanel>
  </Grid>
  <DataTemplate.Triggers>
   <DataTrigger Binding='{Binding Badge}' Value='MP4'><Setter TargetName='badge' Property='Background' Value='{DynamicResource VideoBack}'/><Setter TargetName='badgeText' Property='Foreground' Value='{DynamicResource VideoFore}'/></DataTrigger>
   <DataTrigger Binding='{Binding Badge}' Value='PKG'><Setter TargetName='badge' Property='Background' Value='{DynamicResource SceneBack}'/><Setter TargetName='badgeText' Property='Foreground' Value='{DynamicResource SceneFore}'/></DataTrigger>
  </DataTemplate.Triggers>
 </DataTemplate>
 <Style x:Key='SearchBox' TargetType='TextBox'>
  <Setter Property='Foreground' Value='{DynamicResource Text}'/>
  <Setter Property='CaretBrush' Value='{DynamicResource Text}'/>
  <Setter Property='SelectionBrush' Value='{DynamicResource Accent}'/>
  <Setter Property='Background' Value='{DynamicResource InputBack}'/>
  <Setter Property='BorderBrush' Value='{DynamicResource Line}'/>
  <Setter Property='Height' Value='34'/>
  <Setter Property='VerticalContentAlignment' Value='Center'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='TextBox'>
     <Border x:Name='bd' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
      <Grid>
       <Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions>
       <TextBlock Text='&#xE721;' FontFamily='{StaticResource IconFont}' FontSize='13' Foreground='{DynamicResource Faint}' Margin='11,0,0,0' VerticalAlignment='Center'/>
       <TextBlock x:Name='hint' Grid.Column='1' Text='搜索素材名称' Foreground='{DynamicResource Faint}' Margin='10,0,0,0' VerticalAlignment='Center' IsHitTestVisible='False' Visibility='Collapsed'/>
       <ScrollViewer x:Name='PART_ContentHost' Grid.Column='1' Margin='6,0,8,0' VerticalAlignment='Center'/>
      </Grid>
     </Border>
     <ControlTemplate.Triggers>
      <Trigger Property='Text' Value=''><Setter TargetName='hint' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
 <Style x:Key='Switch' TargetType='CheckBox'>
  <Setter Property='Foreground' Value='{DynamicResource Text}'/>
  <Setter Property='Cursor' Value='Hand'/>
  <Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'>
   <Setter.Value>
    <ControlTemplate TargetType='CheckBox'>
     <StackPanel Orientation='Horizontal' Background='Transparent'>
      <Grid Width='40' Height='22' VerticalAlignment='Center'>
       <Border x:Name='track' CornerRadius='11' Background='{DynamicResource TrackOff}'/>
       <Ellipse x:Name='knob' Width='16' Height='16' Fill='{DynamicResource KnobOff}' HorizontalAlignment='Left' Margin='3,0,0,0'/>
      </Grid>
      <ContentPresenter Margin='10,0,0,0' VerticalAlignment='Center'/>
     </StackPanel>
     <ControlTemplate.Triggers>
      <Trigger Property='IsChecked' Value='True'>
       <Setter TargetName='track' Property='Background' Value='#7C61FF'/>
       <Setter TargetName='knob' Property='HorizontalAlignment' Value='Right'/>
       <Setter TargetName='knob' Property='Margin' Value='0,0,3,0'/>
       <Setter TargetName='knob' Property='Fill' Value='#FFFFFF'/>
      </Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
</ResourceDictionary>";
}
// Tray context-menu palette matching the current window theme.
public sealed class MenuColors : Forms.ProfessionalColorTable {
 readonly System.Drawing.Color back,border,selected,pressed;
 public MenuColors(bool dark){
  back=dark?C(22,28,42):C(255,255,255);border=dark?C(44,54,78):C(222,226,236);
  selected=dark?C(38,46,70):C(237,234,255);pressed=dark?C(46,56,84):C(224,219,255);
 }
 static System.Drawing.Color C(int r,int g,int b){return System.Drawing.Color.FromArgb(r,g,b);}
 public override System.Drawing.Color ToolStripDropDownBackground {get {return back;}}
 public override System.Drawing.Color MenuBorder {get {return border;}}
 public override System.Drawing.Color MenuItemBorder {get {return selected;}}
 public override System.Drawing.Color MenuItemSelected {get {return selected;}}
 public override System.Drawing.Color MenuItemSelectedGradientBegin {get {return selected;}}
 public override System.Drawing.Color MenuItemSelectedGradientEnd {get {return selected;}}
 public override System.Drawing.Color MenuItemPressedGradientBegin {get {return pressed;}}
 public override System.Drawing.Color MenuItemPressedGradientEnd {get {return pressed;}}
 public override System.Drawing.Color ImageMarginGradientBegin {get {return back;}}
 public override System.Drawing.Color ImageMarginGradientMiddle {get {return back;}}
 public override System.Drawing.Color ImageMarginGradientEnd {get {return back;}}
 public override System.Drawing.Color SeparatorDark {get {return border;}}
 public override System.Drawing.Color SeparatorLight {get {return back;}}
}
public static class Native {
 public delegate bool EnumProc(IntPtr h, IntPtr p);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c,string n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p,IntPtr a,string c,string n);
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f,IntPtr p);
 [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll",SetLastError=true)] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int w,int height,uint flags);
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetLong(IntPtr h,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] public static extern IntPtr SetLong(IntPtr h,int index,IntPtr value);
 [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h,uint m,IntPtr w,IntPtr l,uint f,uint timeout,out IntPtr result);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Rect r);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h,StringBuilder s,int n);
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
 [StructLayout(LayoutKind.Sequential)] public struct WindowPos { public IntPtr Window,After; public int X,Y,Width,Height; public uint Flags; }
 public static IntPtr DesktopParent() {return DesktopHost.Parent();}

}
public class Player : Window {
 public IntPtr Handle, ParentHandle; public Forms.Screen Monitor;
 // The wallpaper this player shows (null while a PKG standby waits for Assign).
 public Item Item;
 bool presented,closed,hostFailed;string hostError="";
 public ScenePlayer Scene;
 public bool Ready {get {return !hostFailed&&Scene.Ready;}}
 public bool Failed {get {return hostFailed||Scene.Failed;}}
 public string LastError {get {return hostFailed?hostError:Scene.LastError;}}
 public Action<string> Report;
 public Player(Item item,Forms.Screen monitor) {
  Monitor=monitor;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
  ShowActivated=false;Background=Brushes.Black;Width=monitor.Bounds.Width;Height=monitor.Bounds.Height;
  // A renderable staging HWND; attach only after the backend produces a frame.
  Left=-32000;Top=-32000;WindowStartupLocation=WindowStartupLocation.Manual;
  Item=item;Scene=new ScenePlayer(item,monitor.DeviceName);Content=Scene.UsesMpv?null:Scene.View;
  Scene.Report+=ForwardReport;
  SourceInitialized+=delegate {Handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(Handle).AddHook(WindowMessage);
   Native.SetLong(Handle,-20,new IntPtr(Native.GetLong(Handle,-20).ToInt64()|0x08000080L));};
  Loaded+=async delegate {try{await Scene.Initialize(Handle);}catch(Exception e){HostFailure(e.Message);}};
  Closed+=delegate {closed=true;Report=null;Scene.Dispose();};
 }
 public bool IsClosed {get {return closed;}}
 // Hand a pre-started standby PKG player its scene (see MainWindow.TakeStandby).
 public void Assign(Item item){if(closed)throw new ObjectDisposedException("Player");Scene.Assign(item);Item=item;}
 void HostFailure(string message){if(closed)return;hostFailed=true;hostError=message;if(Report!=null)Report(message);}
 void ForwardReport(string message){if(!closed&&Report!=null)Report(message);}
 public void Pause(bool value){Scene.Pause(value);}
 public Task<string> Stats(){return Scene.Stats();}
 public async Task SeekNearEnd(){await Scene.SeekNearEnd();}
 IntPtr WindowMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(message==0x0046&&!presented){var pos=(Native.WindowPos)Marshal.PtrToStructure(lParam,typeof(Native.WindowPos));
   pos.After=new IntPtr(1);pos.Flags&=~0x0004u;
   if(ParentHandle==IntPtr.Zero){pos.X=-32000;pos.Y=-32000;pos.Flags&=~0x0002u;}
   Marshal.StructureToPtr(pos,lParam,false);}
  return IntPtr.Zero;
 }
 public void Attach(){if(closed)throw new ObjectDisposedException("Player");DesktopHost.Attach(Handle,Monitor,!presented);ParentHandle=DesktopHost.Parent();}
 public void Present(){if(!Ready)throw new InvalidOperationException("播放器尚未就绪。");presented=true;Attach();DesktopHost.Raise(Handle);}
 public async Task<string> Snapshot(string path) {
  return await Scene.Snapshot(path);
 }
}
// A native desktop child containing only a frozen bitmap. Alpha applies to the
// entire HWND, so it also works above WebView2's separate browser child windows.
public sealed class TransitionCover : Window {
 public IntPtr Handle,ParentHandle;readonly Forms.Screen monitor;
 public TransitionCover(Forms.Screen screen,BitmapSource frame){
  monitor=screen;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;AllowsTransparency=true;Background=Brushes.Transparent;
  Width=screen.Bounds.Width;Height=screen.Bounds.Height;Content=new Image(){Source=frame,Stretch=Stretch.Fill};
  SourceInitialized+=delegate{Handle=new WindowInteropHelper(this).Handle;Attach();};Loaded+=delegate{Attach();};
 }
 public void Attach(){DesktopHost.Attach(Handle,monitor,false);ParentHandle=DesktopHost.Parent();SetAlpha(255);}
 public void Raise(){DesktopHost.Raise(Handle);}
 public void SetAlpha(byte alpha){Opacity=alpha/255.0;}
}
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
public class MainWindow : Window {
 ListBox library=new ListBox(); TextBlock status=new TextBlock(); TextBox search=new TextBox();
 const double PreviewAspect=76.0/44.0; ImageBrush previewBrush=new ImageBrush(){Stretch=Stretch.UniformToFill}; Border previewImage=new Border(); TextBlock previewHint=new TextBlock();
 TextBlock detailTitle=new TextBlock(),detailMeta=new TextBlock(),detailFile=new TextBlock(),badgeText=new TextBlock(),nowText=new TextBlock(),countText=new TextBlock(); Border badge=new Border();
 System.Windows.Shapes.Ellipse nowDot=new System.Windows.Shapes.Ellipse(); TextBlock pauseGlyph,pauseLabel; Forms.ToolStripLabel trayNow; Forms.ToolStripMenuItem trayPause,trayStop; Forms.ContextMenuStrip trayMenu; string lastNow;
 string themeMode="system"; bool darkNow; IntPtr windowHandle; TextBlock themeGlyph; Button themeButton;
 Button propsButton,applyButton,stopButton; PropertiesWindow propsWindow;
 CheckBox pauseCovered=new CheckBox(){Content="全屏应用时暂停",IsChecked=true};
 List<Player> players=new List<Player>(); List<Item> items=new List<Item>();
 List<Player> previousPlayers=new List<Player>(); bool transitioning; DispatcherTimer transitionTimer;
 List<TransitionCover> covers=new List<TransitionCover>();DispatcherTimer fadeTimer;int transitionVersion;bool fading,previousManualPause;
 Forms.NotifyIcon tray; bool exiting,manualPause,locked; string topology=ScreenTopology(); DispatcherTimer timer; DispatcherTimer pointerTimer; bool testMode;
 string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall");
 // Wallpaper root: <project>\library\wallpapers; shared assets stay in library\we-assets.
 string root=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","library","wallpapers"));
 Dictionary<string,Item> recoveryPlan;bool recoveryBusy;int recoveryAttempts;DateTime nextRecovery;
 Stopwatch transitionClock;double preparationMs;
 // Wallpaper per display, keyed by Screen.DeviceName. While a transition runs this is the target;
 // previousLayout is what CancelTransition restores. Displays whose wallpaper does not change keep
 // their running player (kept) instead of being rebuilt.
 Dictionary<string,Item> layout=new Dictionary<string,Item>(),previousLayout=new Dictionary<string,Item>();
 HashSet<Player> kept=new HashSet<Player>(),announced=new HashSet<Player>(); Action finishTransition;
 // The primary display's wallpaper: now-playing text, lock-screen snapshot, auto preview.
 Item current {get {return PrimaryItem(layout);}}
 // Target of "设为动态桌面": null = every display, otherwise a Screen.DeviceName.
 string applyTarget; WrapPanel targetPanel; TextBlock applyLabel;
 CheckBox autostartSwitch=new CheckBox(){Content="开机自动启动"},rotationSwitch=new CheckBox(){Content="轮播"};
 Button rotationItemButton,rotationIntervalButton,rotationOrderButton,videoBackendButton; TextBlock settingsHint; Forms.ToolStripMenuItem trayNext;
 bool started,autostartLaunch,coveredNow;
 bool rotationOn,rotationRandom; int rotationMinutes=15; DateTime nextRotation=DateTime.MaxValue; List<string> rotationList=new List<string>(); readonly Random random=new Random();
 void TransitionLog(string phase){try{File.AppendAllText(Path.Combine(data,"transitions.log"),DateTime.Now.ToString("s")+" version="+transitionVersion+" phase="+phase+" elapsedMs="+(transitionClock==null?0:transitionClock.ElapsedMilliseconds)+Environment.NewLine);}catch{}}
 public MainWindow(bool test,bool autostart=false) {
  testMode=test; autostartLaunch=autostart&&!test; Directory.CreateDirectory(data); LoadLibraryRoot(); LoadVideoBackend(); LoadRotation(); Title="AWall · 轻量动态壁纸"; Width=1080; Height=760; MinWidth=880; MinHeight=640; WindowStartupLocation=WindowStartupLocation.CenterScreen;
  string themeError;var styles=Theme.Load(out themeError);themeMode=LoadThemeMode();darkNow=ResolveDark();
  var resources=new ResourceDictionary();resources.MergedDictionaries.Add(Theme.Palette(darkNow));resources.MergedDictionaries.Add(styles);Resources=resources;
  SetResourceReference(BackgroundProperty,"Bg"); SetResourceReference(ForegroundProperty,"Text"); FontFamily=new FontFamily("Microsoft YaHei UI, Segoe UI"); FontSize=13; UseLayoutRounding=true; SnapsToDevicePixels=true;
  TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
  var appIcon=Theme.AppIcon(); if(appIcon!=null)Icon=appIcon;
  var outer=new Grid(){Margin=new Thickness(24,18,24,16)}; Content=outer;
  outer.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto}); outer.RowDefinitions.Add(new RowDefinition()); outer.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto});
  // Header: logo, title and a "now playing" pill.
  var header=new Grid(){Margin=new Thickness(0,0,0,18)}; outer.Children.Add(header);
  header.ColumnDefinitions.Add(new ColumnDefinition(){Width=GridLength.Auto}); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition(){Width=GridLength.Auto});
  var logo=new Image(){Width=42,Height=42,Source=Theme.Logo(96),Margin=new Thickness(0,0,14,0),VerticalAlignment=VerticalAlignment.Center}; RenderOptions.SetBitmapScalingMode(logo,BitmapScalingMode.HighQuality); header.Children.Add(logo);
  var titles=new StackPanel(){VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(titles,1); header.Children.Add(titles);
  titles.Children.Add(new TextBlock(){Text="AWall",FontSize=24,FontWeight=FontWeights.Bold});
  titles.Children.Add(Fg(new TextBlock(){Text="轻量动态桌面 · 静态锁屏",Margin=new Thickness(0,1,0,0)},"Muted"));
  var pill=new Border(){CornerRadius=new CornerRadius(17),BorderThickness=new Thickness(1),Padding=new Thickness(13,7,15,8),VerticalAlignment=VerticalAlignment.Center};
  SurfaceColors(pill);
  var headerRight=new StackPanel(){Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center}; Grid.SetColumn(headerRight,2); header.Children.Add(headerRight);
  themeButton=AddButton(headerRight,"\uE770",null,CycleTheme,"IconButton"); themeGlyph=(TextBlock)((StackPanel)themeButton.Content).Children[0]; themeGlyph.Margin=new Thickness(0);
  headerRight.Children.Add(pill); UpdateThemeButton(); var pillRow=new StackPanel(){Orientation=Orientation.Horizontal}; pill.Child=pillRow;
  nowDot.Width=8; nowDot.Height=8; nowDot.Fill=Theme.Idle; nowDot.VerticalAlignment=VerticalAlignment.Center; nowDot.Margin=new Thickness(0,1,9,0); pillRow.Children.Add(nowDot);
  nowText.Text="未播放"; nowText.MaxWidth=360; nowText.TextTrimming=TextTrimming.CharacterEllipsis; pillRow.Children.Add(nowText);
  var body=new Grid(); Grid.SetRow(body,1); outer.Children.Add(body);
  body.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(340)}); body.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(16)}); body.ColumnDefinitions.Add(new ColumnDefinition());
  // Library card: header with count, search, thumbnail list.
  var left=Card(14); body.Children.Add(left); var leftDock=new DockPanel(); left.Child=leftDock;
  var libraryHead=new DockPanel(){Margin=new Thickness(2,0,2,10)}; DockPanel.SetDock(libraryHead,Dock.Top); leftDock.Children.Add(libraryHead);
  Fg(countText,"Faint"); countText.VerticalAlignment=VerticalAlignment.Center; DockPanel.SetDock(countText,Dock.Right); libraryHead.Children.Add(countText);
  libraryHead.Children.Add(new TextBlock(){Text="素材库",FontSize=15,FontWeight=FontWeights.SemiBold});
  search.Style=TryFindResource("SearchBox") as Style; search.Margin=new Thickness(0,0,0,10); search.TextChanged+=delegate{ApplyFilter();}; DockPanel.SetDock(search,Dock.Top); leftDock.Children.Add(search);
  library.Style=TryFindResource("Library") as Style; library.ItemTemplate=TryFindResource("ItemTemplate") as DataTemplate; leftDock.Children.Add(library);
  // Detail card: preview, info, grouped actions, settings.
  var rightCard=Card(18); Grid.SetColumn(rightCard,2); body.Children.Add(rightCard); var right=new Grid(); rightCard.Child=right;
  right.RowDefinitions.Add(new RowDefinition(){MinHeight=120}); for(int i=0;i<4;i++)right.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto});
  var previewFrame=new Border(){CornerRadius=new CornerRadius(10),BorderThickness=new Thickness(1)}; previewFrame.SetResourceReference(Border.BackgroundProperty,"PreviewBack"); previewFrame.SetResourceReference(Border.BorderBrushProperty,"Line");
  // Same framing as the library thumbnails (76:44, fill + centre crop), scaled to the space available.
  var previewHost=new Grid(); right.Children.Add(previewHost); previewFrame.HorizontalAlignment=HorizontalAlignment.Center; previewFrame.VerticalAlignment=VerticalAlignment.Center; previewHost.Children.Add(previewFrame);
  previewHost.SizeChanged+=delegate{double w=previewHost.ActualWidth,h=previewHost.ActualHeight;if(w<=0||h<=0)return;double fw=Math.Min(w,h*PreviewAspect);previewFrame.Width=Math.Floor(fw);previewFrame.Height=Math.Floor(fw/PreviewAspect);};
  var previewGrid=new Grid(); previewFrame.Child=previewGrid;
  previewHint.Text="暂无预览"; Fg(previewHint,"Faint"); previewHint.HorizontalAlignment=HorizontalAlignment.Center; previewHint.VerticalAlignment=VerticalAlignment.Center; previewGrid.Children.Add(previewHint);
  previewImage.CornerRadius=new CornerRadius(9); previewImage.Background=previewBrush; previewImage.Visibility=Visibility.Collapsed; previewGrid.Children.Add(previewImage);
  var info=new StackPanel(){Margin=new Thickness(2,16,2,0)}; Grid.SetRow(info,1); right.Children.Add(info);
  detailTitle.Text="未选择素材"; detailTitle.FontSize=19; detailTitle.FontWeight=FontWeights.SemiBold; detailTitle.TextTrimming=TextTrimming.CharacterEllipsis; info.Children.Add(detailTitle);
  var metaRow=new StackPanel(){Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,0)}; info.Children.Add(metaRow);
  badge.CornerRadius=new CornerRadius(4); badge.Padding=new Thickness(7,1,7,2); badge.Margin=new Thickness(0,0,10,0); badge.VerticalAlignment=VerticalAlignment.Center; badge.Visibility=Visibility.Collapsed; badge.Child=badgeText; metaRow.Children.Add(badge);
  badgeText.FontSize=11; badgeText.FontWeight=FontWeights.SemiBold;
  Fg(detailMeta,"Muted"); detailMeta.VerticalAlignment=VerticalAlignment.Center; metaRow.Children.Add(detailMeta);
  Fg(detailFile,"Faint"); detailFile.FontSize=12; detailFile.Margin=new Thickness(0,6,0,0); detailFile.TextTrimming=TextTrimming.CharacterEllipsis; info.Children.Add(detailFile);
  var actions=Section(right,2,"壁纸");
  // Multi-display target row ("应用到"); only shown with more than one display.
  targetPanel=new WrapPanel(){Margin=new Thickness(0,0,0,4)}; ((StackPanel)actions.Parent).Children.Insert(1,targetPanel);
  applyButton=AddButton(actions,"\uE768","设为动态桌面",Apply,"PrimaryButton"); applyLabel=(TextBlock)((StackPanel)applyButton.Content).Children[1];
  var pauseContent=(StackPanel)AddButton(actions,"\uE769","暂停",TogglePause,"SoftButton").Content; pauseGlyph=(TextBlock)pauseContent.Children[0]; pauseLabel=(TextBlock)pauseContent.Children[1];
  AddButton(actions,"\uE72E","同步锁屏快照",SyncLock,"SoftButton");
  propsButton=AddButton(actions,"\uE713","调整参数",OpenProperties,"SoftButton");propsButton.Visibility=Visibility.Collapsed;
  rotationItemButton=AddButton(actions,"\uE8EE","加入轮播",ToggleRotationItem,"SoftButton");
  stopButton=AddButton(actions,"\uE71A","停止壁纸",Stop,"DangerButton");
  var manage=Section(right,3,"素材");
  AddButton(manage,"\uE710","导入本地文件",Import,"SoftButton"); AddButton(manage,"\uE72C","重新扫描",Scan,"SoftButton"); AddButton(manage,"\uE896","从 Wallpaper Engine 导入",ImportFromWe,"SoftButton");
  var settings=new StackPanel(){Margin=new Thickness(2,12,2,0)}; Grid.SetRow(settings,4); right.Children.Add(settings);
  var switches=new WrapPanel(); settings.Children.Add(switches);
  foreach(var sw in new[]{pauseCovered,autostartSwitch,rotationSwitch}){sw.Style=TryFindResource("Switch") as Style; sw.Margin=new Thickness(0,0,22,8); switches.Children.Add(sw);}
  pauseCovered.Click+=delegate{SaveLayout();};
  autostartSwitch.IsChecked=AutostartEnabled(); autostartSwitch.Click+=delegate{SetAutostart(autostartSwitch.IsChecked==true);};
  rotationSwitch.IsChecked=rotationOn; rotationSwitch.Click+=delegate{SetRotation(rotationSwitch.IsChecked==true);};
  var options=new WrapPanel(); settings.Children.Add(options);
  rotationIntervalButton=SmallButton(options,"\uE916",CycleRotationInterval); rotationOrderButton=SmallButton(options,"\uE8B1",ToggleRotationOrder); videoBackendButton=SmallButton(options,"\uE714",CycleVideoBackend);
  settingsHint=Fg(new TextBlock(){TextWrapping=TextWrapping.Wrap,FontSize=12,Margin=new Thickness(0,2,0,0)},"Faint"); settings.Children.Add(settingsHint);
  BuildTargets(); UpdateSettingsUi();
  // Status line.
  var statusBar=new DockPanel(){Margin=new Thickness(4,14,4,0)}; Grid.SetRow(statusBar,2); outer.Children.Add(statusBar);
  var statusIcon=Fg(new TextBlock(){Text="\uE946",FontSize=12,Margin=new Thickness(0,1,8,0),VerticalAlignment=VerticalAlignment.Center},"Faint"); statusIcon.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont"); statusBar.Children.Add(statusIcon);
  Fg(status,"Muted"); status.FontSize=12; status.TextTrimming=TextTrimming.CharacterEllipsis; status.VerticalAlignment=VerticalAlignment.Center; statusBar.Children.Add(status);
  library.SelectionChanged+=delegate{ShowDetail(library.SelectedItem as Item);UpdateActionButtons();};
  BuildTray(test); Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e){if(!exiting){e.Cancel=true;Hide();}};
  SourceInitialized+=delegate{var handle=new WindowInteropHelper(this).Handle;HwndSource.FromHwnd(handle).AddHook(ShellMessage);windowHandle=handle;Theme.TitleBar(handle,darkNow);};
  // A second launch (desktop icon, Start menu) signals this event instead of showing a message box.
  if(!test&&ShowSignal!=null)System.Threading.ThreadPool.RegisterWaitForSingleObject(ShowSignal,delegate{Dispatcher.BeginInvoke(new Action(ShowMain));},null,-1,false);
  SystemEvents.SessionSwitch+=SessionChanged; SystemEvents.DisplaySettingsChanged+=DisplayChanged; SystemEvents.UserPreferenceChanged+=PreferenceChanged;
  timer=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(2)}; timer.Tick+=delegate{Tick();RotationTick();};timer.Start();
  pointerTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(50)};pointerTimer.Tick+=delegate{foreach(var p in previousPlayers.Concat(players)){if(p.Scene!=null){var cursor=Forms.Cursor.Position;var b=p.Monitor.Bounds;if(b.Contains(cursor))p.Scene.Pointer((double)(cursor.X-b.Left)/b.Width,(double)(cursor.Y-b.Top)/b.Height);}}};pointerTimer.Start();
  if(themeError!=null)SetStatus("界面样式加载失败，已使用默认样式："+themeError);
  Scan(); Loaded+=delegate{Startup();};
  UpdateNowPlaying();
 }
 // Runs once: from Loaded, or directly for a hidden --autostart launch (the window is first shown
 // later from the tray, and its Loaded must not restore a second time).
 void Startup(){
  if(started)return;started=true;
  if(testMode){Hide();var a=Environment.GetCommandLineArgs();int at=Array.IndexOf(a,"--test-file");if(at>=0&&at+1<a.Length){string f=Path.GetFullPath(a[at+1]);items=new List<Item>{new Item(){Title=Path.GetFileName(f),Type=Item.TypeOf(f),File=f,Preview=""}};Bind();library.SelectedIndex=0;}if(a.Contains("--test-transitions"))TestTransitions();else if(a.Contains("--test-library"))TestLibrary();else RunTest();return;}
  RepairAutostart();ResetRotationClock();
  if(autostartLaunch)RestoreAfterLogin();else RestoreLayout();
 }
 // --autostart: create the window handle (tray, shell hooks) without showing the window.
 public void StartHidden(){new WindowInteropHelper(this).EnsureHandle();Dispatcher.BeginInvoke(new Action(Startup));}
 // Started by the Run key at sign-in: give Explorer a moment to create the desktop windows,
 // then restore; one retry if nothing could be shown yet.
 async void RestoreAfterLogin(){
  try{
   await Task.Delay(3000);await RestoreLayoutAsync();await WaitForTransition();
   if(players.Count==0&&File.Exists(LayoutPath)){TransitionLog("autostart-restore-retry");await Task.Delay(10000);await RestoreLayoutAsync();}
  }catch(Exception e){SetStatus("恢复上次壁纸失败："+e.Message);}
 }
 Border Card(double padding){return SurfaceColors(new Border(){BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(padding)});}
 static Border SurfaceColors(Border border){border.SetResourceReference(Border.BackgroundProperty,"Surface");border.SetResourceReference(Border.BorderBrushProperty,"Line");return border;}
 // Theme-aware foreground: TextBlock and Control share TextElement.ForegroundProperty.
 static T Fg<T>(T element,string key) where T:FrameworkElement{element.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,key);return element;}
 WrapPanel Section(Grid parent,int row,string label){
  var box=new StackPanel(){Margin=new Thickness(2,14,2,0)};Grid.SetRow(box,row);parent.Children.Add(box);
  box.Children.Add(Fg(new TextBlock(){Text=label,FontSize=12,Margin=new Thickness(0,0,0,8)},"Faint"));
  var wrap=new WrapPanel();box.Children.Add(wrap);return wrap;
 }
 Button AddButton(Panel p,string glyph,string text,Action a,string style) {
  var icon=new TextBlock(){Text=glyph,FontSize=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};icon.SetResourceReference(TextBlock.FontFamilyProperty,"IconFont");
  var content=new StackPanel(){Orientation=Orientation.Horizontal};content.Children.Add(icon);if(text!=null)content.Children.Add(new TextBlock(){Text=text,VerticalAlignment=VerticalAlignment.Center});
  var b=new Button(){Content=content,Style=TryFindResource(style) as Style};b.Click+=delegate{try{a();}catch(Exception e){SetStatus(e.Message);}};p.Children.Add(b);return b;
 }
 // ---- Now playing across displays
 string NowTitle(){var distinct=layout.Values.Where(x=>x!=null).GroupBy(x=>ItemKey(x)).Select(g=>g.First()).ToList();return distinct.Count==0?"":distinct.Count==1?distinct[0].Title:distinct.Count+" 个壁纸（多屏）";}
 string NowDetail(){
  if(layout.Count==0)return null;var screens=OrderedScreens();if(screens.Length<2)return current==null?null:current.Title;
  var lines=new List<string>();
  for(int i=0;i<screens.Length;i++){Item shown;lines.Add("显示器 "+(i+1)+(screens[i].Primary?"（主）":"")+"："+(layout.TryGetValue(screens[i].DeviceName,out shown)&&shown!=null?shown.Title:"未播放"));}
  return String.Join("\n",lines.ToArray());
 }
 // ---- "应用到" row: all displays, or one of them (numbered left to right)
 void BuildTargets(){
  if(targetPanel==null)return;targetPanel.Children.Clear();
  var screens=OrderedScreens();
  if(applyTarget!=null&&!screens.Any(s=>s.DeviceName==applyTarget))applyTarget=null;
  if(screens.Length<2){applyTarget=null;targetPanel.Visibility=Visibility.Collapsed;UpdateTargetButtons();return;}
  targetPanel.Visibility=Visibility.Visible;
  targetPanel.Children.Add(Fg(new TextBlock(){Text="应用到",FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,10,8)},"Muted"));
  AddTarget("全部显示器",null);
  for(int i=0;i<screens.Length;i++)AddTarget("显示器 "+(i+1)+(screens[i].Primary?" · 主":""),screens[i].DeviceName);
  UpdateTargetButtons();
 }
 void AddTarget(string text,string device){
  var b=new Button(){Content=text,Style=TryFindResource("SoftButton") as Style,Padding=new Thickness(12,4,12,5),MinHeight=30,FontSize=12,Tag=device};
  b.Click+=delegate{applyTarget=device;UpdateActionButtons();};
  targetPanel.Children.Add(b);
 }
 void UpdateTargetButtons(){
  var screens=OrderedScreens();
  if(targetPanel!=null)foreach(var b in targetPanel.Children.OfType<Button>()){
   string device=b.Tag as string;
   if(device==applyTarget){b.SetResourceReference(Button.BorderBrushProperty,"Accent");b.SetResourceReference(Button.ForegroundProperty,"Accent");}
   else{b.SetResourceReference(Button.BorderBrushProperty,"ButtonLine");b.SetResourceReference(Button.ForegroundProperty,"Text");}
   var screen=screens.FirstOrDefault(x=>x.DeviceName==device);Item shown;
   b.ToolTip=device==null?"所有显示器播放同一张壁纸":screen==null?null:screen.Bounds.Width+"×"+screen.Bounds.Height+" · 当前："+(layout.TryGetValue(device,out shown)&&shown!=null?shown.Title:"未播放");
  }
  if(applyLabel!=null){int index=applyTarget==null?-1:Array.FindIndex(screens,x=>x.DeviceName==applyTarget);applyLabel.Text=index<0?"设为动态桌面":"设到显示器 "+(index+1);}
 }
 Button SmallButton(Panel p,string glyph,Action a){var b=AddButton(p,glyph," ",a,"SoftButton");b.Padding=new Thickness(10,4,12,5);b.MinHeight=30;b.FontSize=12;return b;}
 static void SetButtonText(Button b,string text){if(b==null)return;var content=b.Content as StackPanel;if(content!=null&&content.Children.Count>1)((TextBlock)content.Children[1]).Text=text;}
 // ---- MP4 backend (settings.ini videoBackend=builtin|mpv; --video-backend=... overrides for tests)
 void LoadVideoBackend(){
  string value=LoadSetting("videoBackend");
  var arg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--video-backend="));if(arg!=null)value=arg.Substring(16);
  ScenePlayer.VideoBackend=value=="mpv"?"mpv":"builtin";
 }
 // What new MP4 players actually use: mpv only when chosen and its files are present.
 static bool MpvActive {get {return ScenePlayer.VideoBackend=="mpv"&&ScenePlayer.MpvAvailable;}}
 void CycleVideoBackend(){
  if(ScenePlayer.VideoBackend!="mpv"&&!ScenePlayer.MpvAvailable){SetStatus("没有找到 "+ScenePlayer.MpvPath+"。需要 mpv 时用 build.ps1 -WithMpv 构建；内置播放器不需要它。");return;}
  ScenePlayer.VideoBackend=ScenePlayer.VideoBackend=="mpv"?"builtin":"mpv";SaveSetting("videoBackend",ScenePlayer.VideoBackend);UpdateSettingsUi();
  var selected=library.SelectedItem as Item;if(selected!=null)ShowDetail(selected);
  if(!transitioning&&!swapping&&players.Any(p=>p.Item!=null&&p.Item.Type=="video")){SetStatus("正在用"+(MpvActive?" mpv ":"内置播放器")+"重新加载 MP4…");ReloadVideos();}
  else SetStatus("MP4 播放器已切换为"+(MpvActive?" mpv":"内置播放器（WebView2）")+"，下次播放 MP4 时生效。");
 }
 async void ReloadVideos(){try{await ApplyPlanAsync(true,new Dictionary<string,Item>(layout));}catch(Exception e){SetStatus(e.Message);}}
 void UpdateSettingsUi(){
  SetButtonText(rotationIntervalButton,"每 "+RotationLabel(rotationMinutes)+"换一张");
  SetButtonText(rotationOrderButton,rotationRandom?"随机顺序":"按顺序");
  SetButtonText(videoBackendButton,"MP4："+(ScenePlayer.VideoBackend=="mpv"?(ScenePlayer.MpvAvailable?"mpv":"mpv（未找到，用内置）"):"内置播放器"));
  if(rotationIntervalButton!=null)rotationIntervalButton.IsEnabled=rotationOn;
  if(rotationOrderButton!=null)rotationOrderButton.IsEnabled=rotationOn;
  if(videoBackendButton!=null)videoBackendButton.ToolTip="内置播放器使用 Windows 自带的 WebView2，不需要额外文件；mpv 需要用 build.ps1 -WithMpv 构建（约 120 MB）。";
  if(settingsHint!=null)settingsHint.Text=(rotationOn?"轮播"+(rotationList.Count==0?"整个素材库":"列表中的 "+rotationList.Count+" 项")+"，暂停、锁屏或被全屏窗口遮挡时不切换。":"把素材“加入轮播”后打开“轮播”；列表为空时轮播整个素材库。")+"关闭窗口后驻留托盘。";
 }
 // ---- Rotation (settings.ini rotation / rotationMinutes / rotationOrder; list in rotation.json)
 static readonly int[] RotationChoices={5,15,30,60,180};
 static string RotationLabel(int minutes){return minutes%60==0?(minutes/60)+" 小时":minutes+" 分钟";}
 string RotationPath {get{return Path.Combine(data,"rotation.json");}}
 void LoadRotation(){
  rotationOn=LoadSetting("rotation")=="on";rotationRandom=LoadSetting("rotationOrder")=="random";
  int minutes;if(int.TryParse(LoadSetting("rotationMinutes"),out minutes)&&minutes>0)rotationMinutes=minutes;
  try{if(File.Exists(RotationPath))rotationList=new JavaScriptSerializer().Deserialize<List<string>>(File.ReadAllText(RotationPath))??new List<string>();}catch{rotationList=new List<string>();}
 }
 void SaveRotationList(){try{File.WriteAllText(RotationPath,new JavaScriptSerializer().Serialize(rotationList));}catch(Exception e){SetStatus("保存轮播列表失败："+e.Message);}}
 bool InRotation(Item item){return item!=null&&rotationList.Any(x=>String.Equals(x,item.File,StringComparison.OrdinalIgnoreCase));}
 void ToggleRotationItem(){
  var item=library.SelectedItem as Item;if(item==null)return;
  if(InRotation(item)){rotationList.RemoveAll(x=>String.Equals(x,item.File,StringComparison.OrdinalIgnoreCase));SetStatus("已移出轮播："+item.Title+"（列表剩 "+rotationList.Count+" 项"+(rotationList.Count==0?"，将轮播整个素材库":"")+"）");}
  else{rotationList.Add(item.File);SetStatus("已加入轮播："+item.Title+"（列表共 "+rotationList.Count+" 项）"+(rotationOn?"":"。打开下方“轮播”开关后生效。"));}
  SaveRotationList();ShowDetail(item);UpdateSettingsUi();
 }
 // The rotation list, or the whole library when the list is empty.
 List<Item> RotationCandidates(){
  if(rotationList.Count==0)return items.Where(x=>x.Supported).ToList();
  return rotationList.Select(f=>items.FirstOrDefault(x=>String.Equals(x.File,f,StringComparison.OrdinalIgnoreCase))).Where(x=>x!=null&&x.Supported&&System.IO.File.Exists(x.File)).ToList();
 }
 void ResetRotationClock(){nextRotation=rotationOn?DateTime.UtcNow.AddMinutes(rotationMinutes):DateTime.MaxValue;}
 void SetRotation(bool on){
  rotationOn=on;SaveSetting("rotation",on?"on":"off");ResetRotationClock();UpdateSettingsUi();
  if(!on){SetStatus("轮播已关闭。");return;}
  int count=RotationCandidates().Count;
  SetStatus(count<2?"轮播已打开，但可用素材少于 2 个，暂时不会切换。":"轮播已打开：每 "+RotationLabel(rotationMinutes)+"从"+(rotationList.Count==0?"整个素材库":"轮播列表")+"（"+count+" 项）换一张"+(current==null?"；先设一张壁纸，之后开始轮播。":"。"));
 }
 void CycleRotationInterval(){int i=Array.IndexOf(RotationChoices,rotationMinutes);rotationMinutes=RotationChoices[(i+1)%RotationChoices.Length];SaveSetting("rotationMinutes",rotationMinutes.ToString());ResetRotationClock();UpdateSettingsUi();SetStatus("轮播间隔："+RotationLabel(rotationMinutes)+"，从现在开始计时。");}
 void ToggleRotationOrder(){rotationRandom=!rotationRandom;SaveSetting("rotationOrder",rotationRandom?"random":"sequential");UpdateSettingsUi();SetStatus("轮播顺序："+(rotationRandom?"随机":"按素材库顺序")+"。");}
 void RotationTick(){
  if(!rotationOn||testMode||exiting||DateTime.UtcNow<nextRotation)return;
  // Wait while the wallpaper cannot be seen; switch as soon as it can.
  if(transitioning||swapping||layout.Count==0||players.Count==0||manualPause||locked||coveredNow)return;
  RotateNow(false);
 }
 // Uniform desktop: every display moves to the same next item. Mixed displays: each advances on its own.
 void RotateNow(bool manual){
  var candidates=RotationCandidates();ResetRotationClock();
  if(candidates.Count<2){if(manual)SetStatus("可轮播的素材少于 2 个。");return;}
  if(transitioning||swapping||layout.Count==0)return;
  var screens=Forms.Screen.AllScreens;var plan=new Dictionary<string,Item>();
  bool uniform=layout.Values.Select(x=>ItemKey(x)).Distinct().Count()<=1;
  if(uniform){var next=NextAfter(current,candidates);foreach(var s in screens)plan[s.DeviceName]=next;}
  else foreach(var s in screens){Item shown;layout.TryGetValue(s.DeviceName,out shown);plan[s.DeviceName]=NextAfter(shown??current,candidates);}
  TransitionLog("rotation "+(manual?"manual":"timer"));ApplyRotation(plan);
 }
 Item NextAfter(Item shown,List<Item> candidates){
  if(rotationRandom){var others=candidates.Where(x=>!SameItem(x,shown)).ToList();return others.Count==0?candidates[0]:others[random.Next(others.Count)];}
  int i=candidates.FindIndex(x=>SameItem(x,shown));return candidates[(i+1)%candidates.Count];
 }
 async void ApplyRotation(Dictionary<string,Item> plan){try{await ApplyPlanAsync(false,plan);}catch(Exception e){SetStatus("轮播切换失败："+e.Message);}}
 // ---- Start with Windows (HKCU Run, value "AWall" = "<exe>" --autostart)
 const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
 static string ExePath {get {return System.Reflection.Assembly.GetEntryAssembly().Location;}}
 static string AutostartValue(){try{using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){return key==null?null:key.GetValue("AWall") as string;}}catch{return null;}}
 static bool AutostartEnabled(){return !String.IsNullOrEmpty(AutostartValue());}
 void SetAutostart(bool on){
  try{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(on)key.SetValue("AWall","\""+ExePath+"\" --autostart");else key.DeleteValue("AWall",false);}
   SetStatus(on?"已开启开机自动启动：登录后只在托盘运行，并恢复上次的壁纸。":"已关闭开机自动启动。");
  }catch(Exception e){SetStatus("设置开机自动启动失败："+e.Message);}
  autostartSwitch.IsChecked=AutostartEnabled();
 }
 // The Run entry names one AWall.exe; if that file is gone (folder moved), point it at this one.
 void RepairAutostart(){
  string value=AutostartValue();if(String.IsNullOrEmpty(value))return;
  string target=value.StartsWith("\"")&&value.IndexOf('"',1)>0?value.Substring(1,value.IndexOf('"',1)-1):value.Split(' ')[0];
  if(String.Equals(target,ExePath,StringComparison.OrdinalIgnoreCase)||File.Exists(target))return;
  try{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){key.SetValue("AWall","\""+ExePath+"\" --autostart");}TransitionLog("autostart-repaired "+target+" -> "+ExePath);}catch(Exception e){TransitionLog("autostart-repair-failed "+e.Message);}
 }
 void BuildTray(bool test){
  tray=new Forms.NotifyIcon(){Text="AWall",Icon=Theme.TrayIcon(),Visible=!test};
  trayMenu=new Forms.ContextMenuStrip(){ShowImageMargin=false,Font=new System.Drawing.Font("Microsoft YaHei UI",9f),Padding=new Forms.Padding(2,4,2,4)};
  trayNow=new Forms.ToolStripLabel("未播放"){Padding=new Forms.Padding(4,4,4,4)};
  var open=new Forms.ToolStripMenuItem("打开 AWall",null,delegate{ShowMain();}){Font=new System.Drawing.Font("Microsoft YaHei UI",9f,System.Drawing.FontStyle.Bold)};
  trayPause=new Forms.ToolStripMenuItem("暂停壁纸",null,delegate{TogglePause();});
  trayNext=new Forms.ToolStripMenuItem("下一张壁纸",null,delegate{RotateNow(true);});
  trayStop=new Forms.ToolStripMenuItem("停止壁纸",null,delegate{Stop();});
  var quit=new Forms.ToolStripMenuItem("退出",null,delegate{Exit();});
  foreach(var item in new Forms.ToolStripMenuItem[]{open,trayPause,trayNext,trayStop,quit})item.Padding=new Forms.Padding(4,4,12,4);
  trayMenu.Items.AddRange(new Forms.ToolStripItem[]{trayNow,new Forms.ToolStripSeparator(),open,trayPause,trayNext,trayStop,new Forms.ToolStripSeparator(),quit});
  trayMenu.Opening+=delegate{bool active=current!=null||players.Count>0;trayNow.Text=current==null?"未播放":(manualPause?"已暂停：":"正在播放：")+Shorten(NowTitle(),26);trayPause.Text=manualPause?"继续壁纸":"暂停壁纸";trayPause.Enabled=current!=null;trayNext.Enabled=current!=null&&RotationCandidates().Count>1;trayStop.Enabled=active;};
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
 // Theme: follow Windows (default) -> light -> dark, remembered in settings.ini.
 string SettingsPath {get{return Path.Combine(data,"settings.ini");}}
 void LoadLibraryRoot(){
  try{if(File.Exists(SettingsPath))foreach(var line in File.ReadAllLines(SettingsPath))if(line.StartsWith("libraryRoot=")){string value=line.Substring(12).Trim();if(!Path.IsPathRooted(value))continue;value=Path.GetFullPath(value).TrimEnd('\\');
   // Older builds saved library\wallpaper-engine; the typed layout lives one level up.
   if(Path.GetFileName(value).Equals("wallpaper-engine",StringComparison.OrdinalIgnoreCase)&&!Directory.Exists(Path.Combine(value,"pkg")))value=Path.GetDirectoryName(value);
   if(Path.GetFileName(value).Equals("library",StringComparison.OrdinalIgnoreCase)&&Directory.Exists(Path.Combine(value,"wallpapers")))value=Path.Combine(value,"wallpapers");
   if(Directory.Exists(value))root=value;}}catch(Exception e){TransitionLog("load-library-failed: "+e.Message);}
 }
 void SaveSettings(){
  try{var lines=File.Exists(SettingsPath)?File.ReadAllLines(SettingsPath).Where(x=>!x.StartsWith("theme=")&&!x.StartsWith("libraryRoot=")).ToList():new List<string>();lines.Add("theme="+themeMode);lines.Add("libraryRoot="+root);File.WriteAllLines(SettingsPath,lines);}catch(Exception e){SetStatus("保存设置失败："+e.Message);}
 }
 string LoadThemeMode(){
  try{if(File.Exists(SettingsPath))foreach(var line in File.ReadAllLines(SettingsPath)){if(line.StartsWith("theme=")){string v=line.Substring(6).Trim();if(v=="light"||v=="dark"||v=="system")return v;}}}catch{}
  return "system";
 }
 bool ResolveDark(){return themeMode=="dark"||(themeMode=="system"&&Theme.SystemPrefersDark());}
 void CycleTheme(){
  themeMode=themeMode=="system"?"light":themeMode=="light"?"dark":"system";
  SaveSettings();
  ApplyTheme();
 }
 void ApplyTheme(){
  darkNow=ResolveDark();Resources.MergedDictionaries[0]=Theme.Palette(darkNow);
  Theme.TitleBar(windowHandle,darkNow);ApplyMenuTheme();UpdateThemeButton();
 }
 void UpdateThemeButton(){
  if(themeGlyph==null)return;
  themeGlyph.Text=themeMode=="light"?"\uE706":themeMode=="dark"?"\uE708":"\uE770";
  themeButton.ToolTip="主题："+(themeMode=="light"?"浅色":themeMode=="dark"?"深色":"跟随系统")+"（点击切换）";
 }
 void PreferenceChanged(object s,UserPreferenceChangedEventArgs e){
  if(e.Category!=UserPreferenceCategory.General&&e.Category!=UserPreferenceCategory.Color)return;
  Dispatcher.BeginInvoke(new Action(delegate{if(themeMode=="system"&&ResolveDark()!=darkNow)ApplyTheme();}));
 }
 static string Shorten(string text,int max){text=text??"";return text.Length>max?text.Substring(0,max-1)+"…":text;}
 void ShowMain(){if(exiting)return;if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();Topmost=true;Topmost=false;Focus();}
 void TogglePause(){manualPause=!manualPause;Tick();SaveLayout();}
 void ShowDetail(Item item){
  if(item==null)return;
  detailTitle.Text=String.IsNullOrEmpty(item.Title)?"未命名素材":item.Title;detailTitle.ToolTip=item.Title;
  badge.Visibility=Visibility.Visible;badge.SetResourceReference(Border.BackgroundProperty,item.BadgeKey+"Back");badgeText.SetResourceReference(TextBlock.ForegroundProperty,item.BadgeKey+"Fore");badgeText.Text=item.Badge;
  detailMeta.Text=item.Type=="video"?(MpvActive?"视频 · mpv 硬件解码":"视频 · 内置播放器（WebView2 硬件解码）"):item.Type=="scene"?"PKG 场景 · WebGL 兼容渲染（非官方引擎）":"网页 · WebView2 离线渲染（首次播放后自动生成预览图）";
  detailFile.Text=Path.GetFileName(item.File??"");detailFile.ToolTip=item.File;
  propsButton.Visibility=item.Type=="web"&&WebProperties.Parse(item).Count>0?Visibility.Visible:Visibility.Collapsed;
  SetButtonText(rotationItemButton,InRotation(item)?"移出轮播":"加入轮播");
  var image=Item.LoadImage(item.Preview,1280);previewBrush.ImageSource=image;
  previewImage.Visibility=image==null?Visibility.Collapsed:Visibility.Visible;previewHint.Visibility=image==null?Visibility.Visible:Visibility.Collapsed;
 }
 // HTML wallpapers arrive without a preview: save the primary monitor's frame as <name>.jpg
 // after the first successful play. Only for files inside the library, never the user's own folders.
 async void AutoPreview(){
  var item=current;if(testMode||item==null||item.Type!="web"||players.Count==0)return;
  if(!String.IsNullOrEmpty(item.Preview)&&System.IO.File.Exists(item.Preview))return;
  if(!Path.GetFullPath(item.File).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))return;
  try{
   await Task.Delay(1500);// let intro animations settle
   if(current!=item||transitioning)return;
   var p=players.FirstOrDefault(x=>x.Monitor.Primary)??players[0];if(!p.Ready||p.Failed||!SameItem(p.Item,item))return;
   string target=Path.ChangeExtension(item.File,".jpg");
   await p.Snapshot(target);item.Preview=target;item.ResetThumb();
   var selected=library.SelectedItem;Bind();library.SelectedItem=selected;if(selected==item)ShowDetail(item);
  }catch(Exception e){TransitionLog("auto-preview failed: "+e.Message);}
 }
 // Parameters declared inside an HTML wallpaper. Changes apply live when that wallpaper is playing
 // and are saved for the next time it plays.
 void OpenProperties(){
  var item=library.SelectedItem as Item;if(item==null||item.Type!="web")return;
  string error;var props=WebProperties.Parse(item,out error);
  if(error!=null){SetStatus(error);return;}
  if(props.Count==0){SetStatus("这个网页没有声明可调参数。");return;}
  if(propsWindow!=null){propsWindow.Close();propsWindow=null;}
  propsWindow=new PropertiesWindow(this,item,props,delegate(string name,object value){
   foreach(var p in players)if(p.Scene!=null&&SameItem(p.Item,item))p.Scene.SetProperty(name,value);
  },darkNow);
  propsWindow.Closed+=delegate(object s,EventArgs e){if(propsWindow==s)propsWindow=null;};
  propsWindow.Show();
 }
 void Bind(){library.ItemsSource=null;library.ItemsSource=items;ApplyFilter();}
 void ApplyFilter(){
  var view=CollectionViewSource.GetDefaultView(items);if(view==null)return;string q=(search.Text??"").Trim();
  if(q.Length==0)view.Filter=null;else view.Filter=delegate(object o){var it=o as Item;return it!=null&&(it.Title??"").IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0;};
  countText.Text=q.Length==0?items.Count+" 项":items.Count(x=>view.Filter(x))+" / "+items.Count;
 }
 // "设为动态桌面" is disabled while the selected item is already the active
 // wallpaper (playing, paused or loading); "停止壁纸" only when nothing is active.
 bool IsActive(Item item){
  if(item==null||layout.Count==0)return false;
  var screens=Forms.Screen.AllScreens;
  var targets=applyTarget!=null&&screens.Any(s=>s.DeviceName==applyTarget)?new[]{applyTarget}:screens.Select(s=>s.DeviceName).ToArray();
  foreach(var device in targets){Item shown;if(!layout.TryGetValue(device,out shown)||!SameItem(shown,item))return false;}
  return true;
 }
 void UpdateActionButtons(){
  bool active=current!=null||players.Count>0||previousPlayers.Count>0||transitioning||swapping;
  if(applyButton!=null)applyButton.IsEnabled=library.SelectedItem is Item&&!IsActive(library.SelectedItem as Item);
  if(stopButton!=null)stopButton.IsEnabled=active;
  UpdateTargetButtons();
 }
 void UpdateNowPlaying(){
  UpdateActionButtons();
  string text=current==null?"未播放":((transitioning||swapping)?"正在加载 · ":manualPause?"已暂停 · ":"正在播放 · ")+NowTitle();
  string detail=NowDetail();
  string key=text+"|"+manualPause+"|"+detail;if(key==lastNow)return;lastNow=key;
  nowText.Text=text;nowText.ToolTip=detail;
  nowDot.Fill=current==null?Theme.Idle:(transitioning||swapping)?Theme.Accent:manualPause?Theme.Warn:Theme.Ok;
  if(pauseGlyph!=null){pauseGlyph.Text=manualPause?"\uE768":"\uE769";pauseLabel.Text=manualPause?"继续":"暂停";}
  if(tray!=null)tray.Text=Shorten("AWall · "+text,63);
 }
 void SetStatus(string s){status.Text=s;status.ToolTip=s;UpdateNowPlaying();System.IO.File.AppendAllText(Path.Combine(data,"AWall.log"),DateTime.Now.ToString("s")+" "+s+Environment.NewLine);}
 // AI-generated pages usually sit in Downloads; keep a copy in library\html so the library only depends on its own folder.
 string CopyIntoLibrary(string file){
  string dir=Path.Combine(root,"html");Directory.CreateDirectory(dir);
  if(String.Equals(Path.GetFullPath(Path.GetDirectoryName(file)),dir,StringComparison.OrdinalIgnoreCase))return Path.GetFullPath(file);
  string name=Path.GetFileNameWithoutExtension(file),target=Path.Combine(dir,name+".html");
  for(int n=2;System.IO.File.Exists(target);n++){if(System.IO.File.ReadAllBytes(target).SequenceEqual(System.IO.File.ReadAllBytes(file)))return target;target=Path.Combine(dir,name+" ("+n+").html");}
  System.IO.File.Copy(file,target);return target;
 }
 void Import(){var dialog=new Microsoft.Win32.OpenFileDialog(){Filter="壁纸文件|*.mp4;*.pkg;*.html;*.htm|MP4 视频|*.mp4|PKG 场景|*.pkg|HTML 网页|*.html;*.htm",Multiselect=true};if(dialog.ShowDialog()==true){foreach(string picked in dialog.FileNames){string file=Item.TypeOf(picked)=="web"?CopyIntoLibrary(picked):picked;var item=items.FirstOrDefault(x=>String.Equals(x.File,file,StringComparison.OrdinalIgnoreCase));if(item==null){item=new Item(){Title=Path.GetFileNameWithoutExtension(file),Type=Item.TypeOf(file),File=file,Preview=Item.TypeOf(file)=="web"?SidecarPreview(file):Path.Combine(Path.GetDirectoryName(file),"preview.jpg")};items.Add(item);}Bind();library.SelectedItem=item;}SetStatus("已导入 "+dialog.FileNames.Length+" 个 MP4 / PKG / HTML 文件。");}}
 // Import from Wallpaper Engine (UI + copy logic in WeImport.cs): locate the workshop folder (last used,
 // Steam config, common folders, then a shallow drive search), list compatible items minus duplicates,
 // confirm, then copy into wallpapers\mp4 / wallpapers\pkg.
 string WeAssetsDir {get{return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","library","we-assets"));}}
 string LoadSetting(string key){try{if(File.Exists(SettingsPath))foreach(var line in File.ReadAllLines(SettingsPath))if(line.StartsWith(key+"="))return line.Substring(key.Length+1).Trim();}catch{}return null;}
 void SaveSetting(string key,string value){try{var lines=File.Exists(SettingsPath)?File.ReadAllLines(SettingsPath).Where(x=>!x.StartsWith(key+"=")).ToList():new List<string>();lines.Add(key+"="+value);File.WriteAllLines(SettingsPath,lines);}catch(Exception e){TransitionLog("save-setting-failed: "+e.Message);}}
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
 // Typed library: root\pkg\<name>.pkg (+ <name>.json project, <name>.jpg|gif|png preview), root\mp4\<name>.mp4
 // and root\html\<name>.html (single-file web wallpaper; <name>.jpg preview is generated on first play).
 // Falls back to the old per-folder project.json scan only when neither typed folder exists yet.
 static readonly string[] PreviewExts={".jpg",".gif",".png",".jpeg"};
 static string SidecarPreview(string file){foreach(var ext in PreviewExts){var p=Path.ChangeExtension(file,ext);if(System.IO.File.Exists(p))return p;}return "";}
 void ScanTyped(string folder,string pattern,string type){
  if(!Directory.Exists(folder))return;
  foreach(var file in Directory.GetFiles(folder,pattern,SearchOption.TopDirectoryOnly))items.Add(new Item(){Title=Path.GetFileNameWithoutExtension(file),Type=type,File=Path.GetFullPath(file),Preview=SidecarPreview(file)});
 }
 void Scan(){items.Clear();
  string pkgDir=Path.Combine(root,"pkg"),mp4Dir=Path.Combine(root,"mp4"),htmlDir=Path.Combine(root,"html");
  if(Directory.Exists(pkgDir)||Directory.Exists(mp4Dir)||Directory.Exists(htmlDir)){ScanTyped(pkgDir,"*.pkg","scene");ScanTyped(mp4Dir,"*.mp4","video");ScanTyped(htmlDir,"*.html","web");}
  else if(Directory.Exists(root))foreach(var file in Directory.GetFiles(root,"project.json",SearchOption.AllDirectories)){try {var j=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(System.IO.File.ReadAllText(file));var dir=Path.GetDirectoryName(file);string type=j.ContainsKey("type")?Convert.ToString(j["type"]).ToLowerInvariant():"scene";string entry=j.ContainsKey("file")?Convert.ToString(j["file"]):"";var item=new Item(){Title=j.ContainsKey("title")?Convert.ToString(j["title"]):Path.GetFileName(dir),Type=type,File=Path.GetFullPath(Path.Combine(dir,entry)),Preview=Path.Combine(dir,j.ContainsKey("preview")?Convert.ToString(j["preview"]):"preview.jpg")};if(!item.File.StartsWith(dir+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))continue;if(type=="scene"&&!System.IO.File.Exists(item.File)&&System.IO.File.Exists(Path.Combine(dir,"scene.pkg")))item.File=Path.Combine(dir,"scene.pkg");items.Add(item);}catch{}}
  items=items.OrderBy(x=>x.Type=="video"?0:x.Type=="scene"?1:2).ThenBy(x=>x.Title).ToList();Bind();if(items.Count>0)library.SelectedIndex=0;
  SetStatus("已扫描 "+items.Count+" 个素材，其中 "+items.Count(x=>x.Type=="video")+" 个视频、"+items.Count(x=>x.Type=="scene")+" 个 PKG 场景、"+items.Count(x=>x.Type=="web")+" 个网页。目录："+root);
  ScheduleStandby();
 }
 async void Apply(){recoveryPlan=null;recoveryAttempts=0;ResetRotationClock();try{await ApplyAsync();}catch(Exception e){SetStatus(e.Message);}}
 static bool SameItem(Item a,Item b){return a!=null&&b!=null&&a.Type==b.Type&&String.Equals(a.File,b.File,StringComparison.OrdinalIgnoreCase);}
 static string ItemKey(Item item){return item==null?"":item.Type+"|"+(item.File??"").ToLowerInvariant();}
 // Displays numbered left to right, as in the "应用到" row.
 static Forms.Screen[] OrderedScreens(){return Forms.Screen.AllScreens.OrderBy(x=>x.Bounds.Left).ThenBy(x=>x.Bounds.Top).ToArray();}
 static Item PrimaryItem(Dictionary<string,Item> plan){
  if(plan==null||plan.Count==0)return null;Item item;
  foreach(var s in Forms.Screen.AllScreens.OrderByDescending(x=>x.Primary).ThenBy(x=>x.Bounds.Left))if(plan.TryGetValue(s.DeviceName,out item)&&item!=null)return item;
  return plan.Values.FirstOrDefault(x=>x!=null);
 }
 // Every current display gets an item: its own entry, otherwise the plan's primary item.
 static Dictionary<string,Item> CompletePlan(Dictionary<string,Item> plan,Forms.Screen[] screens){
  var result=new Dictionary<string,Item>();var fallback=PrimaryItem(plan);if(fallback==null)return result;
  foreach(var s in screens){Item item;result[s.DeviceName]=plan.TryGetValue(s.DeviceName,out item)&&item!=null?item:fallback;}
  return result;
 }
 static Dictionary<string,Item> Uniform(Item item){var plan=new Dictionary<string,Item>();foreach(var s in Forms.Screen.AllScreens)plan[s.DeviceName]=item;return plan;}
 static Dictionary<string,Item> LayoutOf(IEnumerable<Player> list){var result=new Dictionary<string,Item>();foreach(var p in list)if(p.Item!=null&&!p.IsClosed)result[p.Monitor.DeviceName]=p.Item;return result;}
 // The chosen display gets `item`; the others keep what they show now (or get `item` if empty).
 Dictionary<string,Item> PlanFor(Item item,string target){
  var screens=Forms.Screen.AllScreens;if(target!=null&&!screens.Any(s=>s.DeviceName==target))target=null;
  var plan=new Dictionary<string,Item>();
  foreach(var s in screens){Item keep;plan[s.DeviceName]=target!=null&&s.DeviceName!=target&&layout.TryGetValue(s.DeviceName,out keep)&&keep!=null?keep:item;}
  return plan;
 }
 bool Healthy(Player p,Forms.Screen[] screens){return !p.IsClosed&&p.Ready&&!p.Failed&&screens.Any(s=>s.DeviceName==p.Monitor.DeviceName&&s.Bounds==p.Monitor.Bounds);}
 // True when every display already plays exactly this plan.
 bool PlayingPlan(Dictionary<string,Item> plan){
  var screens=Forms.Screen.AllScreens;
  return players.Count==screens.Length&&players.All(p=>Healthy(p,screens)&&plan.ContainsKey(p.Monitor.DeviceName)&&SameItem(p.Item,plan[p.Monitor.DeviceName]));
 }
 static string PlanTitle(Dictionary<string,Item> plan){var distinct=plan.Values.GroupBy(x=>ItemKey(x)).Select(g=>g.First()).ToList();return distinct.Count==1?distinct[0].Title:distinct.Count+" 个壁纸";}
 async Task ApplyAsync(bool force=false,Item requested=null){var item=requested??library.SelectedItem as Item;if(item==null)return;await ApplyPlanAsync(force,PlanFor(item,applyTarget));}
 // Shows plan[display] on every display. Displays whose wallpaper does not change keep their running
 // player (unless forced); the others get new players that stay off-screen until all are ready, behind
 // a cover of the old frame that then fades out. Any failure restores the previous wallpapers.
 async Task ApplyPlanAsync(bool force,Dictionary<string,Item> requestedPlan){
  var screens=Forms.Screen.AllScreens;var plan=CompletePlan(requestedPlan,screens);if(plan.Count==0)return;
  foreach(var check in plan.Values.Distinct()){if(!check.Supported){SetStatus("不支持的素材类型："+check.Type);return;}if(!System.IO.File.Exists(check.File))throw new Exception("素材入口文件不存在："+check.Title);}
  if(!force&&!transitioning&&PlayingPlan(plan)){SetStatus("当前壁纸已在播放。");return;}
  // PKG -> PKG: swap the scene inside the already running page instead of building a new
  // WebView2. While a swap runs, remember only the latest requested plan and apply it after.
  if(!force&&swapping){if(plan.Values.All(x=>x.Type=="scene")){pendingPlan=plan;SetStatus("正在切换中，稍后加载："+PlanTitle(plan));return;}pendingPlan=null;}
  if(!force&&!transitioning&&!swapping&&players.Count==screens.Length&&players.All(p=>Healthy(p,screens))){
   var changed=players.Where(p=>plan.ContainsKey(p.Monitor.DeviceName)&&!SameItem(p.Item,plan[p.Monitor.DeviceName])).ToList();
   if(changed.Count>0&&changed.All(p=>p.Scene.CanSwap&&plan[p.Monitor.DeviceName].Type=="scene")){await SwapAsync(changed,plan);return;}
  }
  CancelTransition();
  transitionClock=Stopwatch.StartNew();preparationMs=0;TransitionLog("begin");
  int version=++transitionVersion;previousManualPause=manualPause;
  var keep=new HashSet<Player>();
  if(!force&&!swapping)foreach(var p in players)if(Healthy(p,screens)&&Native.IsWindow(p.Handle)&&plan.ContainsKey(p.Monitor.DeviceName)&&SameItem(p.Item,plan[p.Monitor.DeviceName]))keep.Add(p);
  var fresh=screens.Where(s=>!keep.Any(p=>p.Monitor.DeviceName==s.DeviceName)).ToArray();
  if(keep.Count>0)TransitionLog("kept "+keep.Count+" display(s)");
  previousPlayers=players.Where(p=>!keep.Contains(p)).ToList();previousLayout=layout;
  players=new List<Player>(keep);layout=plan;kept=keep;announced=new HashSet<Player>();manualPause=false;transitioning=true;
  var batch=players;bool coversReady=false;
  Action finish=delegate{if(!coversReady||version!=transitionVersion||players!=batch||!transitioning||fading||batch.Count!=screens.Length||!batch.All(x=>x.Ready))return;try{TransitionLog("all-content-ready");foreach(var ready in batch)if(!keep.Contains(ready))ready.Present();TransitionLog("attached");foreach(var cover in covers)cover.Raise();StopTransitionTimer();BeginFade(batch,version);}catch(Exception e){CancelTransition();SetStatus("桌面挂载失败，已恢复原壁纸："+e.Message);}};
  finishTransition=finish;
  transitionTimer=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(60)};
  transitionTimer.Tick+=delegate{if(transitioning&&players==batch){CancelTransition();SetStatus("壁纸加载超时，已保留原壁纸。");}};transitionTimer.Start();
  try{
   // Start the old-frame capture, then immediately start the replacement
   // backends in their off-screen staging windows. They are never presented
   // before coversReady, so the visual barrier is unchanged, but WebView2
   // browser startup / mpv startup now overlaps capture + cover setup.
   TransitionLog("capture-start");var oldBatch=previousPlayers.ToArray();
   var capture=Task.WhenAll(fresh.Select(screen=>CaptureBackdrop(screen,oldBatch)).ToArray());
   TransitionLog("backends-start");
   foreach(var screen in fresh){var wanted=plan[screen.DeviceName];var p=TakeStandby(wanted,screen)??new Player(wanted,screen);Hook(p);batch.Add(p);p.Show();}
   // With a pre-started standby the new scene is often ready before the old
   // frame capture finishes (mpv/WebView capture can take ~1s). Do not let the
   // cover gate the switch: once all new content has been ready for 100ms and
   // the capture is still pending, present with a direct cut instead of a fade.
   long readyAt=-1;
   while(!capture.IsCompleted){
    if(version!=transitionVersion)return;
    if(batch.Count==screens.Length&&batch.All(x=>x.Ready)){if(readyAt<0)readyAt=transitionClock.ElapsedMilliseconds;else if(transitionClock.ElapsedMilliseconds-readyAt>=100)break;}
    await Task.WhenAny(capture,Task.Delay(25));
   }
   if(version!=transitionVersion)return;
   if(capture.IsCompleted&&!capture.IsFaulted&&!capture.IsCanceled){
    var frames=capture.Result;TransitionLog("capture-done");
    for(int i=0;i<fresh.Length;i++){var cover=new TransitionCover(fresh[i],frames[i]);covers.Add(cover);cover.Show();}
    await Dispatcher.InvokeAsync(delegate{},DispatcherPriority.Render);await Task.Delay(50);if(version!=transitionVersion)return;
   }else{TransitionLog("covers-skipped capture-pending");capture.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);}
   foreach(var old in oldBatch)old.Pause(true);
   preparationMs=transitionClock.Elapsed.TotalMilliseconds;TransitionLog("covers-ready");
   coversReady=true;finish();
   var shown=fresh.Length>0?plan[fresh[0].DeviceName]:PrimaryItem(plan);
   SetStatus((shown.Type=="scene"?"正在加载 PKG：":shown.Type=="web"?"正在加载网页：":"正在加载 MP4：")+shown.Title+(screens.Length>1&&fresh.Length<screens.Length?"（"+fresh.Length+" 块显示器）":"")+"。锁屏图片需点击“同步锁屏快照”。");}catch{if(version!=transitionVersion)return;CancelTransition();throw;}
 }
 void Hook(Player p){p.Report=delegate(string message){PlayerReport(p,message);};}
 // Reports from any live player (kept or new). A failure during a transition rolls back; a failure
 // afterwards stops the wallpaper.
 void PlayerReport(Player p,string message){
  if(!players.Contains(p))return;
  if(p.Ready&&announced.Add(p))TransitionLog("backend-ready "+p.Monitor.DeviceName);
  SetStatus(message);
  Dispatcher.BeginInvoke(new Action(delegate{
   if(!players.Contains(p))return;
   var failed=players.FirstOrDefault(x=>x.Failed);
   if(failed!=null){string error=failed.LastError;if(transitioning){CancelTransition();SetStatus("壁纸加载失败，已保留原壁纸："+error);}else{Stop();SetStatus("壁纸播放器已退出："+error);}return;}
   var next=finishTransition;if(transitioning&&next!=null)next();
  }),DispatcherPriority.ContextIdle);
 }
 async Task<BitmapSource> CaptureBackdrop(Forms.Screen screen,Player[] old){
  var p=old.FirstOrDefault(x=>x.Monitor.DeviceName==screen.DeviceName&&x.Ready&&!x.Failed&&Native.IsWindow(x.Handle));
  if(p!=null)try{return await p.Scene.CaptureFrame();}catch(Exception e){TransitionLog("capture-fallback: "+e.Message);}
  return StartupBackdrop.Capture(screen);
 }
 static string ScreenTopology(){return String.Join(";",Forms.Screen.AllScreens.Select(x=>x.DeviceName+":"+x.Bounds+":"+x.Primary));}
 void BeginFade(List<Player> batch,int version){
  TransitionLog("fade-start");fading=true;var elapsed=Stopwatch.StartNew();fadeTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(15)};
  fadeTimer.Tick+=delegate{
   if(version!=transitionVersion||players!=batch)return;
   try{
    if(batch.Any(x=>x.Failed)){CancelTransition();SetStatus("新壁纸播放失败，已恢复原壁纸。");return;}
    double progress=Math.Min(1,elapsed.Elapsed.TotalMilliseconds/350.0);
    foreach(var cover in covers)cover.SetAlpha((byte)Math.Round(255*(1-progress)));
    if(progress<1&&covers.Count>0)return;
    CloseCovers();transitioning=false;StopFade();var old=previousPlayers;previousPlayers=new List<Player>();previousLayout=new Dictionary<string,Item>();kept=new HashSet<Player>();finishTransition=null;
    foreach(var retired in old)retired.Close();Tick();recoveryPlan=null;recoveryAttempts=0;SaveLayout();TransitionLog("complete");ScheduleStandby();SetStatus("壁纸切换完成。");AutoPreview();
   }catch(Exception e){CancelTransition();SetStatus("壁纸切换失败，已恢复原壁纸："+e.Message);}
  };fadeTimer.Start();
 }
 // PKG startup cost is dominated by WebView2: a new browser process (~1s) plus
 // loading index.html and the 1.1MB engine script (~1s). Keep one pre-started
 // player per monitor parked off-screen at "boot" so the next PKG switch only
 // pays for mounting the scene. It uses its own profile slot, so it never
 // shares a browser process with the live wallpaper. Costs one idle WebView2.
 Dictionary<string,Player> standbys=new Dictionary<string,Player>();
 Player TakeStandby(Item item,Forms.Screen screen){
  if(item.Type!="scene")return null;Player p;
  if(!standbys.TryGetValue(screen.DeviceName,out p))return null;standbys.Remove(screen.DeviceName);
  if(p.IsClosed||p.Failed||!p.Scene.Standby||p.Monitor.Bounds!=screen.Bounds){try{p.Close();}catch{}return null;}
  try{p.Assign(item);TransitionLog("standby-used "+screen.DeviceName);return p;}catch{try{p.Close();}catch{}return null;}
 }
 bool swapping; Dictionary<string,Item> pendingPlan;
 async Task SwapAsync(List<Player> targets,Dictionary<string,Item> plan){
  swapping=true;int version=++transitionVersion;var batch=players;
  transitionClock=Stopwatch.StartNew();preparationMs=0;TransitionLog("swap-begin "+targets.Count+" display(s)");
  SetStatus("正在加载 PKG："+plan[targets[0].Monitor.DeviceName].Title+"（同页切换）。");
  bool[] results;
  try{results=await Task.WhenAll(targets.Select(p=>p.Scene.Swap(plan[p.Monitor.DeviceName])).ToArray());}
  finally{swapping=false;}
  var next=pendingPlan;pendingPlan=null;
  if(version!=transitionVersion||players!=batch)return;
  for(int i=0;i<targets.Count;i++)if(results[i])targets[i].Item=plan[targets[i].Monitor.DeviceName];
  layout=LayoutOf(players);
  if(results.All(x=>x)){manualPause=false;Tick();SaveLayout();TransitionLog("swap-complete");SetStatus("壁纸切换完成。");}
  else if(results.Any(x=>x)){TransitionLog("swap-partial rebuild");await ApplyPlanAsync(true,plan);return;}
  else{string error=String.Join("；",targets.Select(p=>p.Scene.LastSwapError).Where(x=>!String.IsNullOrEmpty(x)).Distinct().ToArray());TransitionLog("swap-failed "+error);SetStatus("壁纸加载失败，已保留原壁纸："+error);}
  if(next!=null&&!PlayingPlan(next))await ApplyPlanAsync(false,next);
 }
 void ScheduleStandby(){
  if(testMode||exiting||!items.Any(x=>x.Type=="scene"))return;
  // Let the wallpaper that just appeared settle before starting another browser.
  var delay=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(500)};delay.Tick+=delegate{delay.Stop();PrepareStandby();};delay.Start();
 }
 void PrepareStandby(){
  if(testMode||exiting||transitioning)return;
  var screens=Forms.Screen.AllScreens;
  foreach(var key in standbys.Keys.ToArray()){var old=standbys[key];if(!old.IsClosed&&!old.Failed&&screens.Any(s=>s.DeviceName==key&&s.Bounds==old.Monitor.Bounds))continue;standbys.Remove(key);try{old.Close();}catch{}}
  foreach(var screen in screens){if(standbys.ContainsKey(screen.DeviceName))continue;
   try{var p=new Player(null,screen);standbys[screen.DeviceName]=p;p.Show();TransitionLog("standby-start "+screen.DeviceName);}catch(Exception e){TransitionLog("standby-failed "+e.Message);}}
 }
 void CloseStandbys(){foreach(var p in standbys.Values.ToArray()){try{p.Close();}catch{}}standbys.Clear();}
 void StopFade(){if(fadeTimer!=null){fadeTimer.Stop();fadeTimer=null;}fading=false;}
 void CloseCovers(){var old=covers;covers=new List<TransitionCover>();foreach(var cover in old)cover.Close();}
 void StopTransitionTimer(){if(transitionTimer!=null){transitionTimer.Stop();transitionTimer=null;}}
 void CancelTransition(){
  if(!transitioning)return;TransitionLog("rollback");++transitionVersion;transitioning=false;finishTransition=null;StopTransitionTimer();StopFade();
  var pending=players;var restore=previousPlayers;var keep=kept;players=restore;manualPause=previousManualPause;
  previousPlayers=new List<Player>();previousLayout=new Dictionary<string,Item>();kept=new HashSet<Player>();
  try{foreach(var p in restore.ToArray()){try{if(!p.Ready||p.Failed||!Native.IsWindow(p.Handle))throw new InvalidOperationException("旧播放器已失效");p.Present();}catch(Exception e){TransitionLog("rollback-unavailable: "+e.Message);players.Remove(p);if(!p.IsClosed)p.Close();}}}
  finally{foreach(var p in pending)if(!keep.Contains(p)&&!p.IsClosed)p.Close();CloseCovers();}
  // Kept players never stopped showing their wallpaper; they simply stay.
  foreach(var p in keep){if(p.IsClosed||p.Failed){if(!p.IsClosed)p.Close();}else players.Add(p);}
  layout=LayoutOf(players);
  Tick();
 }
 void Stop(){if(current==null&&players.Count==0&&previousPlayers.Count==0&&!transitioning&&!swapping){UpdateActionButtons();return;}StopPlayback(true);}
 void StopPlayback(bool clearLayout){recoveryPlan=null;recoveryAttempts=0;++transitionVersion;transitioning=false;finishTransition=null;StopTransitionTimer();StopFade();CloseCovers();var all=previousPlayers.Concat(players).Distinct().ToArray();players=new List<Player>();previousPlayers=new List<Player>();layout=new Dictionary<string,Item>();previousLayout=new Dictionary<string,Item>();kept=new HashSet<Player>();foreach(var p in all)p.Close();if(clearLayout)try{File.Delete(LayoutPath);}catch(Exception e){SetStatus("清除壁纸布局失败："+e.Message);}SetStatus("动态壁纸已停止。");}
 void Tick(){try{bool covered=false;IntPtr fg=Native.GetForegroundWindow();var cls=new StringBuilder(256);Native.GetClassName(fg,cls,256);Native.Rect r; if(pauseCovered.IsChecked==true&&Native.GetWindowRect(fg,out r)&&!Native.IsIconic(fg)&&cls.ToString()!="Progman"&&cls.ToString()!="WorkerW")foreach(var s in Forms.Screen.AllScreens){var b=s.Bounds;if(r.Left<=b.Left&&r.Top<=b.Top&&r.Right>=b.Right&&r.Bottom>=b.Bottom)covered=true;}
  if(!transitioning&&(players.Any(p=>!Native.IsWindow(p.Handle))||(recoveryPlan!=null&&players.Count==0&&recoveryAttempts<5&&DateTime.UtcNow>=nextRecovery))){RebuildDesktop();return;}
  foreach(var p in previousPlayers.Concat(players).Where(p=>Native.IsWindow(p.Handle))){if(p.ParentHandle!=IntPtr.Zero&&!DesktopHost.IsHealthy(p.ParentHandle))p.Attach();if(p.Ready){p.Pause((transitioning&&covers.Count>0&&previousPlayers.Contains(p))||manualPause||locked||covered);}}
  foreach(var cover in covers){if(!DesktopHost.IsHealthy(cover.ParentHandle))cover.Attach();cover.Raise();}
  coveredNow=covered;
  UpdateNowPlaying();
 }catch(Exception e){SetStatus("桌面恢复失败："+e.Message);}}
 readonly uint taskbarCreated=RegisterWindowMessage("TaskbarCreated");
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
 IntPtr ShellMessage(IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled){
  if(taskbarCreated!=0&&(uint)message==taskbarCreated)Dispatcher.BeginInvoke(new Action(delegate{recoveryAttempts=0;nextRecovery=DateTime.MinValue;RebuildDesktop();}));return IntPtr.Zero;
 }
 async void RebuildDesktop(){await RebuildDesktopAsync();}
 async Task RebuildDesktopAsync(){
  if(exiting||recoveryBusy)return;var plan=layout.Count>0?new Dictionary<string,Item>(layout):recoveryPlan;if(plan==null||plan.Count==0)return;
  recoveryPlan=plan;recoveryBusy=true;bool keepPause=manualPause;int version=transitionVersion;
  try{DesktopHost.Invalidate();var pending=ApplyPlanAsync(true,plan);version=transitionVersion;await pending;
   if(version==transitionVersion&&layout.Count>0){manualPause=keepPause;Tick();}
  }catch(Exception e){
   if(version==transitionVersion||current==null){++recoveryAttempts;nextRecovery=DateTime.UtcNow.AddSeconds(Math.Min(10,recoveryAttempts*2));SetStatus("等待 Windows 桌面恢复："+e.Message);}
  }finally{recoveryBusy=false;}
 }

 void SessionChanged(object s,SessionSwitchEventArgs e){Dispatcher.BeginInvoke(new Action(delegate{locked=e.Reason==SessionSwitchReason.SessionLock;if(e.Reason==SessionSwitchReason.SessionUnlock)locked=false;Tick();}));}
 void DisplayChanged(object s,EventArgs e){Dispatcher.BeginInvoke(new Action(delegate{string next=ScreenTopology();if(next!=topology){topology=next;BuildTargets();if(current!=null){var item=current;library.SelectedItem=item;RebuildDesktop();}}}));}
 string LayoutPath {get{return Path.Combine(data,testMode?"wallpaper-layout-test.json":"wallpaper-layout.json");}}
 public sealed class SavedScreen {public string Device,Title,Type,File,Preview;}
 // Title..Preview is the primary display's wallpaper (also what older builds read); Screens is per display.
 public sealed class SavedLayout {public string Title,Type,File,Preview;public bool ManualPause;public bool PauseCovered=true;public List<SavedScreen> Screens;}
 void SaveLayout(){
  if(transitioning||current==null||players.Count==0||players.Any(x=>!x.Ready||x.Failed))return;
  try{var primary=current;
   var value=new SavedLayout(){Title=primary.Title,Type=primary.Type,File=primary.File,Preview=primary.Preview,ManualPause=manualPause,PauseCovered=pauseCovered.IsChecked==true,
    Screens=layout.Where(x=>x.Value!=null).Select(x=>new SavedScreen(){Device=x.Key,Title=x.Value.Title,Type=x.Value.Type,File=x.Value.File,Preview=x.Value.Preview}).ToList()};
   string temp=LayoutPath+".tmp";File.WriteAllText(temp,new JavaScriptSerializer().Serialize(value));
   if(File.Exists(LayoutPath))File.Replace(temp,LayoutPath,null);else File.Move(temp,LayoutPath);
  }catch(Exception e){TransitionLog("save-layout-failed: "+e.Message);}
 }
 // A saved entry as a library item. Layouts saved before the library was reorganised point at
 // files that no longer exist; re-find them by title. Null when nothing usable is left.
 Item FindSaved(string title,string type,string file,string preview){
  if(String.IsNullOrEmpty(file)||(type!="video"&&type!="scene"&&type!="web"))return null;
  if(!File.Exists(file)){var moved=items.FirstOrDefault(x=>x.Type==type&&String.Equals(x.Title,title,StringComparison.OrdinalIgnoreCase));if(moved==null)return null;file=moved.File;preview=moved.Preview;}
  var item=items.FirstOrDefault(x=>String.Equals(x.File,file,StringComparison.OrdinalIgnoreCase)&&x.Type==type);
  if(item==null){item=new Item(){Title=title,Type=type,File=file,Preview=preview};items.Add(item);Bind();}
  return item;
 }
 async void RestoreLayout(){try{await RestoreLayoutAsync();}catch(Exception e){SetStatus("恢复上次壁纸失败："+e.Message);}}
 async Task RestoreLayoutAsync(){
  if(!File.Exists(LayoutPath))return;
  var saved=new JavaScriptSerializer().Deserialize<SavedLayout>(File.ReadAllText(LayoutPath));
  var primary=saved==null?null:FindSaved(saved.Title,saved.Type,saved.File,saved.Preview);
  if(primary==null)throw new IOException("上次壁纸文件不可用。");
  var plan=new Dictionary<string,Item>();
  if(saved.Screens!=null)foreach(var entry in saved.Screens){if(entry==null||String.IsNullOrEmpty(entry.Device))continue;var item=FindSaved(entry.Title,entry.Type,entry.File,entry.Preview);if(item!=null)plan[entry.Device]=item;}
  // Displays that were not saved (new, or renamed by Windows) get the primary wallpaper.
  foreach(var screen in Forms.Screen.AllScreens)if(!plan.ContainsKey(screen.DeviceName))plan[screen.DeviceName]=primary;
  library.SelectedItem=primary;pauseCovered.IsChecked=saved.PauseCovered;await ApplyPlanAsync(true,plan);manualPause=saved.ManualPause;Tick();
 }
 async void SyncLock(){if(players.Count==0){SetStatus("请先加载 MP4 或 PKG，再同步锁屏快照。");return;}try{var p=players.FirstOrDefault(x=>x.Monitor.Primary)??players[0];string image=await p.Snapshot(Path.Combine(data,"lock-"+Guid.NewGuid().ToString("N")+".jpg"));SetStatus("正在保存锁屏快照…");var start=new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Set-LockImage.ps1")+"\" -ImagePath \""+image+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};int code=await System.Threading.Tasks.Task.Run(delegate{using(var proc=Process.Start(start)){string output=proc.StandardOutput.ReadToEnd();string error=proc.StandardError.ReadToEnd();proc.WaitForExit();System.IO.File.WriteAllText(Path.Combine(data,"lock-result.txt"),output+error);return proc.ExitCode;}});SetStatus(code==0?"锁屏静态快照已同步。Win+L 后显示图片，不播放视频。":"锁屏设置失败，详情见 "+Path.Combine(data,"lock-result.txt"));}catch(Exception e){SetStatus(e.Message);}}
 void RunTest(){var check=new DispatcherTimer(){Interval=TimeSpan.FromSeconds(8)};Apply();check.Tick+=async delegate{check.Stop();try{var p=players[0];await p.Snapshot(Path.Combine(data,"test-frame.jpg"));var perf=await PerfProbe.Measure(3);System.IO.File.WriteAllText(Path.Combine(data,"selftest.txt"),"items="+items.Count+"; videos="+items.Count(x=>x.Type=="video")+"; parent="+p.ParentHandle+"; media="+await p.Stats()+"; snapshot=ok; videoBackend="+(MpvActive?"mpv":"builtin")+"; footprint="+new JavaScriptSerializer().Serialize(PerfProbe.Footprint())+"; perf="+new JavaScriptSerializer().Serialize(perf));}catch(Exception e){System.IO.File.WriteAllText(Path.Combine(data,"selftest.txt"),e.ToString());}Exit();};check.Start();}
 async void TestLibrary(){
  timer.Stop();pointerTimer.Stop();pauseCovered.IsChecked=false;
  var results=new List<object>();string folder=Path.Combine(data,"compatibility");Directory.CreateDirectory(folder);
  // Resource use while each wallpaper plays (AWall + all child processes), plus an idle baseline.
  int perfSeconds=5;var perfArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--perf-seconds="));if(perfArg!=null&&(!int.TryParse(perfArg.Substring(15),out perfSeconds)||perfSeconds<1))perfSeconds=5;
  var footprint=PerfProbe.Footprint();await Task.Delay(1500);var idle=await PerfProbe.Measure(3);
  var perfItems=new List<KeyValuePair<string,Dictionary<string,object>>>();
  var onlyArg=Environment.GetCommandLineArgs().FirstOrDefault(x=>x.StartsWith("--only="));string only=onlyArg==null?null:onlyArg.Substring(7);foreach(var item in items.Where(x=>x.Supported&&(only==null||(only.Split(',').Contains(Path.GetFileName(Path.GetDirectoryName(x.File)))||only.Split(',').Contains(Path.GetFileNameWithoutExtension(x.File))))).ToArray()){
   library.SelectedItem=item;string id=Path.GetFileNameWithoutExtension(item.File);
   try{await ApplyAsync();var player=players.First();var started=DateTime.UtcNow;
    while(!player.Ready&&!player.Failed&&(DateTime.UtcNow-started).TotalSeconds<50)await Task.Delay(250);
    if(!player.Ready)throw new Exception(player.LastError);
    await Task.Delay(1500);if(player.Failed)throw new Exception(player.LastError);
    var perf=await PerfProbe.Measure(perfSeconds);if(player.Failed)throw new Exception(player.LastError);
    string frame=Path.Combine(folder,id+".jpg");await player.Snapshot(frame);
    int lo=255,hi=0;using(var bitmap=new System.Drawing.Bitmap(frame)){for(int y=0;y<bitmap.Height;y+=Math.Max(1,bitmap.Height/48))for(int x=0;x<bitmap.Width;x+=Math.Max(1,bitmap.Width/64)){var pixel=bitmap.GetPixel(x,y);lo=Math.Min(lo,Math.Min(pixel.R,Math.Min(pixel.G,pixel.B)));hi=Math.Max(hi,Math.Max(pixel.R,Math.Max(pixel.G,pixel.B)));}}if(hi-lo<8)throw new Exception("截图近似纯色，需人工排查");
    await Task.Delay(500);string second=Path.Combine(folder,id+"-next.jpg");await player.Snapshot(second);
    bool frameChanged;using(var sha=System.Security.Cryptography.SHA256.Create()){frameChanged=!sha.ComputeHash(System.IO.File.ReadAllBytes(frame)).SequenceEqual(sha.ComputeHash(System.IO.File.ReadAllBytes(second)));}
    string pauseStats="";{player.Pause(true);await Task.Delay(300);pauseStats=await player.Stats();if(!pauseStats.Contains("\"paused\":true"))throw new Exception("播放暂停未生效");player.Pause(false);await Task.Delay(300);}
    if(item.Type=="video"){
     player.Pause(true);await Task.Delay(250);
     var json=new JavaScriptSerializer();
     var before=json.Deserialize<Dictionary<string,object>>(await player.Stats());await Task.Delay(450);
     var after=json.Deserialize<Dictionary<string,object>>(await player.Stats());
     if(!Convert.ToBoolean(after["mediaPaused"])||Math.Abs(Convert.ToDouble(after["time"])-Convert.ToDouble(before["time"]))>0.1)throw new Exception("MP4 暂停后仍在播放");
     player.Pause(false);
     await player.SeekNearEnd();
     await Task.Delay(1200);
     var loop=json.Deserialize<Dictionary<string,object>>(await player.Stats());
     if(Convert.ToBoolean(loop["ended"])||Convert.ToDouble(loop["time"])>=Convert.ToDouble(loop["duration"])-0.2)throw new Exception("MP4 循环播放未生效");
    }
    string stats=await player.Stats();
    results.Add(new{id=id,title=item.Title,type=item.Type,success=true,warnings=player.Scene!=null?player.Scene.Warnings:0,frameChanged=frameChanged,pixelRange=hi-lo,pause=pauseStats,stats=stats,perf=perf});
    perfItems.Add(new KeyValuePair<string,Dictionary<string,object>>(item.Type,perf));
   }catch(Exception ex){results.Add(new{id=id,title=item.Title,type=item.Type,success=false,error=ex.ToString()});}
   // Long enough for the previous wallpaper's processes to exit before the next measurement.
   Stop();System.IO.File.WriteAllText(Path.Combine(folder,"results.json"),new JavaScriptSerializer().Serialize(results));await Task.Delay(1500);
  }
  try{string text;var summary=PerfProbe.Summarize(MpvActive?"mpv":"builtin",Forms.Screen.AllScreens.Length,footprint,idle,perfItems,out text);
   System.IO.File.WriteAllText(Path.Combine(folder,"perf-summary.json"),new JavaScriptSerializer().Serialize(summary));
   System.IO.File.WriteAllText(Path.Combine(folder,"perf-summary.txt"),text,Encoding.UTF8);
  }catch(Exception e){System.IO.File.WriteAllText(Path.Combine(folder,"perf-summary.txt"),"性能汇总失败："+e,Encoding.UTF8);}
  Exit();
 }
 async Task WaitForTransition(){var started=Stopwatch.StartNew();while(transitioning&&started.Elapsed.TotalSeconds<65)await Task.Delay(25);if(transitioning)throw new Exception("切换未结束");}
 async void TestTransitions(){
  var results=new List<string>();timer.Stop();pointerTimer.Stop();pauseCovered.IsChecked=false;
  string resultPath=Path.Combine(data,"transition-test.json");
  try{
   var video=items.First(x=>x.Type=="video");var scene=items.First(x=>x.Type=="scene"&&System.IO.File.Exists(x.File));
   library.SelectedItem=video;await ApplyAsync();await WaitForTransition();
   if(current!=video||players.Count==0||!players.All(x=>x.Ready)||covers.Count!=0)throw new Exception("首次播放失败");if(players.Any(x=>Native.GetParent(x.Handle)!=DesktopHost.Parent()))throw new Exception("首帧就绪后未正确挂桌面");results.Add("first-play/checked-parent: pass");
   var stable=players.ToArray();await ApplyAsync();if(!players.SequenceEqual(stable))throw new Exception("相同素材重复启动");results.Add("same-wallpaper-noop: pass");
   var original=players.ToArray();library.SelectedItem=scene;await ApplyAsync();
   if(players.Any(x=>!x.Ready&&x.ParentHandle!=IntPtr.Zero))throw new Exception("未就绪播放器提前挂入桌面");
   if(covers.Count!=original.Length||previousPlayers.Count!=original.Length)throw new Exception("快照遮罩未创建");
   var oldCovers=covers.ToArray();await Task.Delay(100);
   if(transitioning){foreach(var old in original){string stats=await old.Stats();if(!stats.Contains("\"paused\":true"))throw new Exception("旧播放器未暂停");}}
   await WaitForTransition();if(current!=scene||!players.All(x=>x.Ready)||covers.Count!=0||previousPlayers.Count!=0||oldCovers.Any(x=>Native.IsWindow(x.Handle)))throw new Exception("场景切换或遮罩清理失败");results.Add("video-to-scene/freeze/fade/cleanup: pass");
   library.SelectedItem=video;await ApplyAsync();await WaitForTransition();if(current!=video||!players.All(x=>x.Ready))throw new Exception("视频切换失败");results.Add("scene-to-video: pass");
   library.SelectedItem=scene;await RebuildDesktopAsync();await WaitForTransition();if(current!=video||!players.All(x=>x.Ready))throw new Exception("恢复错误使用了预览素材");results.Add("host-rebuild-keeps-playing-item: pass");
   manualPause=true;Tick();SaveLayout();StopPlayback(false);await RestoreLayoutAsync();await WaitForTransition();if(current==null||current.File!=video.File||!players.All(x=>x.Ready))throw new Exception("启动布局恢复失败");await Task.Delay(200);if(!manualPause||!(await players[0].Stats()).Contains("\"paused\":true"))throw new Exception("启动恢复未保留暂停设置");manualPause=false;Tick();SaveLayout();results.Add("saved-layout-restore/pause: pass");
   // Simulate loss of one desktop-owned HWND without restarting the user's shell.
   players[0].Close();library.SelectedItem=scene;await RebuildDesktopAsync();await WaitForTransition();if(current.File!=video.File||!players.All(x=>x.Ready))throw new Exception("桌面窗口销毁后未重建");results.Add("destroyed-window-rebuild: pass");
   StopPlayback(false);recoveryPlan=Uniform(video);await RebuildDesktopAsync();await WaitForTransition();if(current!=video||!players.All(x=>x.Ready))throw new Exception("桌面恢复未使用保留目标");results.Add("deferred-shell-recovery-target: pass");
   library.SelectedItem=video;

   original=players.ToArray();var broken=new Item(){Title="invalid transition test",Type="video",File=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","test-artifacts","invalid.mp4"),Preview=""};items.Add(broken);Bind();library.SelectedItem=broken;
   await ApplyAsync();await WaitForTransition();if(current!=video||!players.SequenceEqual(original)||covers.Count!=0||!players.All(x=>x.Ready))throw new Exception("失败后没有恢复旧播放器");results.Add("failed-load-rollback: pass");
   library.SelectedItem=scene;var stale=ApplyAsync();library.SelectedItem=video;var latest=ApplyAsync();await Task.WhenAll(stale,latest);await WaitForTransition();if(current!=video||covers.Count!=0||previousPlayers.Count!=0)throw new Exception("连续切换遗留状态");results.Add("rapid-switch: pass");
   library.SelectedItem=scene;var stopping=ApplyAsync();Stop();await stopping;await Task.Delay(250);if(players.Count!=0||covers.Count!=0||previousPlayers.Count!=0||transitioning)throw new Exception("停止后异步请求重新创建窗口");results.Add("stop-during-capture: pass");
   System.IO.File.WriteAllText(resultPath,new JavaScriptSerializer().Serialize(new{success=true,checks=results}));
  }catch(Exception e){System.IO.File.WriteAllText(resultPath,new JavaScriptSerializer().Serialize(new{success=false,checks=results,error=e.ToString()}));}
  Exit();
 }
 void Exit(){exiting=true;timer.Stop();pointerTimer.Stop();SystemEvents.SessionSwitch-=SessionChanged;SystemEvents.UserPreferenceChanged-=PreferenceChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;SaveLayout();StopPlayback(false);CloseStandbys();tray.Dispose();Application.Current.Shutdown();}
 public static System.Threading.EventWaitHandle ShowSignal;
 [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
 [STAThread]public static void Main(string[] args){
  Native.SetProcessDPIAware();bool test=args.Contains("--self-test")||args.Contains("--test-library")||args.Contains("--test-transitions");bool autostart=args.Contains("--autostart");
  // The show event is created before the mutex, so a second launch can always tell
  // whether the running instance listens for it (builds before this change did not).
  bool signalCreated;
  using(ShowSignal=new System.Threading.EventWaitHandle(false,System.Threading.EventResetMode.AutoReset,"Local\\AWall.Desktop.Show",out signalCreated)){
   bool owned;using(var mutex=new System.Threading.Mutex(true,"Local\\AWall.Desktop",out owned)){
    if(!owned){
     if(test||autostart)return;
     if(signalCreated){MessageBox.Show("AWall 已在运行（旧版本），请从托盘打开，或退出后重新启动。","AWall");return;}
     AllowSetForegroundWindow(-1);ShowSignal.Set();return;
    }
    var app=new Application(){ShutdownMode=ShutdownMode.OnExplicitShutdown};app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){System.IO.File.AppendAllText(Path.Combine(Path.GetTempPath(),"AWall-error.log"),e.Exception.ToString());MessageBox.Show(e.Exception.Message,"AWall");e.Handled=true;};
    var window=new MainWindow(test,autostart);
    // Sign-in launch: tray only, the window opens from the tray icon.
    if(autostart&&!test){window.StartHidden();app.Run();}else app.Run(window);
   }
  }
 }
}
}

