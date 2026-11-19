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
}
