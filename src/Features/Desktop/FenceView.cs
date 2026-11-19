using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AWall {
// One fence on the desktop: a native WPF block (no WebView) hosted by BlockWindow in the AboveIcons layer.
// Items behave like Explorer's: click / Ctrl+click select, double-click or Enter opens, right-click shows
// the shell menu, F2 renames in place, Delete recycles, drag out to any Explorer target, drag in to assign.
public sealed class FenceView {
 public const string DragFormat="AWall.Fence";
 static readonly Brush Back=Frozen(0x1F,0x22,0x29),Line=Frozen(0x34,0x3A,0x46),Hover=Frozen(0x2C,0x31,0x3B),Selected=Frozen(0x33,0x49,0x6E),
  Text=Frozen(0xF0,0xF2,0xF7),Muted=Frozen(0x9A,0xA3,0xB5),DropMark=Frozen(0x5E,0xA8,0xFF);
 static Brush Frozen(byte r,byte g,byte b){var x=new SolidColorBrush(Color.FromRgb(r,g,b));x.Freeze();return x;}
 public const int TitleHeight=30; // DIP

 public readonly FenceConfig Config;
 public BlockWindow Window {get;private set;}
 readonly FenceService service;
 readonly Border root;readonly TextBlock title,count,chevron,hint;readonly WrapPanel wrap;readonly ScrollViewer scroll;readonly Border grip;readonly Grid titleBar;
 readonly Dictionary<string,Border> tiles=new Dictionary<string,Border>();
 readonly HashSet<string> selection=new HashSet<string>();
 List<DesktopEntry> entries=new List<DesktopEntry>();
 string anchorKey;Point dragStart;bool dragArmed;
 // Move / resize with the mouse while the layout is unlocked.
 System.Drawing.Point gestureCursor;System.Drawing.Rectangle gestureBounds;int gesture; // 0 none, 1 move, 2 resize
 public bool Removed;

 public FenceView(FenceService owner,FenceConfig config){
  service=owner;Config=config;
  title=new TextBlock(){Foreground=Text,FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
  count=new TextBlock(){Foreground=Muted,FontSize=12,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,14,0)};
  chevron=new TextBlock(){Foreground=Muted,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=10,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,8,0)};
  var titleDock=new DockPanel(){LastChildFill=true};DockPanel.SetDock(chevron,Dock.Left);DockPanel.SetDock(count,Dock.Right);
  titleDock.Children.Add(chevron);titleDock.Children.Add(count);titleDock.Children.Add(title);
  titleBar=new Grid(){Height=TitleHeight,Background=Brushes.Transparent};titleBar.Children.Add(titleDock);
  wrap=new WrapPanel(){Margin=new Thickness(6,2,6,6)};
  scroll=new ScrollViewer(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=wrap,Focusable=false};
  hint=new TextBlock(){Foreground=Muted,FontSize=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12),IsHitTestVisible=false};
  grip=new Border(){Width=14,Height=14,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Background=Brushes.Transparent,Cursor=Cursors.SizeNWSE,
   Child=new TextBlock(){Text="",FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=9,Foreground=Muted,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
  var body=new Grid();body.Children.Add(scroll);body.Children.Add(hint);body.Children.Add(grip);
  var layout=new Grid();layout.RowDefinitions.Add(new RowDefinition(){Height=GridLength.Auto});layout.RowDefinitions.Add(new RowDefinition());
  Grid.SetRow(body,1);layout.Children.Add(titleBar);layout.Children.Add(body);
  root=new Border(){Background=Back,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Child=layout,Focusable=true,AllowDrop=true};
  FocusManager.SetIsFocusScope(root,true);
  TextOptions.SetTextFormattingMode(root,TextFormattingMode.Display);

  titleBar.MouseLeftButtonDown+=TitleDown;titleBar.MouseMove+=GestureMove;titleBar.MouseLeftButtonUp+=GestureUp;
  grip.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(!service.Store.Unlocked)return;BeginGesture(2);grip.CaptureMouse();e.Handled=true;};
  grip.MouseMove+=GestureMove;grip.MouseLeftButtonUp+=GestureUp;
  root.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){Keyboard.Focus(root);if(e.Source==root||e.OriginalSource==scroll||e.OriginalSource==wrap){ClearSelection();}};
  root.MouseRightButtonUp+=delegate(object s,MouseButtonEventArgs e){if(!e.Handled){e.Handled=true;ShowFenceMenu();}};
  root.PreviewKeyDown+=KeyDown;
  root.DragEnter+=DragOver;root.DragOver+=DragOver;root.DragLeave+=delegate{root.BorderBrush=Line;};root.Drop+=OnDrop;
  CreateWindow();
  Refresh();
 }
 void CreateWindow(){
  Window=new BlockWindow(root);
  Window.Closed+=delegate{if(!Removed)service.WindowLost(this);};
 }
 // After the system destroyed the window (its Explorer parent went away), host the same content again.
 public void Recreate(){if(Window!=null&&!Window.IsClosed)return;try{Window.Content=null;}catch{}CreateWindow();}

 // ---- Placement
 public void Mount(System.Drawing.Rectangle bounds){
  if(Config.Collapsed)bounds.Height=(int)Math.Round(TitleHeight*service.Scale)+2;
  Window.Mount(bounds);Round(bounds);
 }
 [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int left,int top,int right,int bottom,int width,int height);
 [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd,IntPtr region,bool redraw);
 // A child window cannot be transparent (see --test-desktop), so the rounded corners are a window region.
 void Round(System.Drawing.Rectangle b){int r=(int)Math.Round(20*service.Scale);SetWindowRgn(Window.Handle,CreateRoundRectRgn(0,0,b.Width+1,b.Height+1,r,r),true);}

 // ---- Content
 public void Refresh(){
  title.Text=String.IsNullOrEmpty(Config.Title)?"分区":Config.Title;
  chevron.Text=Config.Collapsed?"":"";
  grip.Visibility=service.Store.Unlocked&&!Config.Collapsed?Visibility.Visible:Visibility.Collapsed;
  titleBar.Cursor=service.Store.Unlocked?Cursors.SizeAll:null;
  count.Text=entries.Count==0?"":entries.Count.ToString();
  hint.Text=entries.Count>0?"":Config.Kind=="free"?"把桌面上的文件拖到这里":Config.Kind=="folder"?(Directory.Exists(Config.Folder??"")?"文件夹是空的":"找不到文件夹："+Config.Folder):Config.Kind=="other"?"其他文件都已归入分区":"没有符合规则的文件";
 }
 public IList<DesktopEntry> Entries {get {return entries;}}
 // For tests: tiles whose shell icon has arrived.
 public int IconCount {get {return tiles.Values.Count(t=>((Image)((StackPanel)t.Child).Children[0]).Source!=null);}}
 public void SetEntries(List<DesktopEntry> list){
  entries=list??new List<DesktopEntry>();
  var keep=new HashSet<string>(entries.Select(x=>x.Key));selection.IntersectWith(keep);
  wrap.Children.Clear();var old=new Dictionary<string,Border>(tiles);tiles.Clear();
  foreach(var e in entries){
   Border tile;
   if(!old.TryGetValue(e.Key+"|"+e.Name,out tile))tile=Tile(e);else tile.Tag=e;
   tiles[e.Key+"|"+e.Name]=tile;wrap.Children.Add(tile);Paint(tile);
  }
  Refresh();
 }
 Border Tile(DesktopEntry e){
  var image=new Image(){Width=40,Height=40,Margin=new Thickness(0,2,0,4),HorizontalAlignment=HorizontalAlignment.Center};
  RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
  var label=new TextBlock(){Text=e.Name,Foreground=Text,FontSize=12,TextWrapping=TextWrapping.Wrap,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,MaxHeight=34,LineHeight=16,LineStackingStrategy=LineStackingStrategy.BlockLineHeight};
  var stack=new StackPanel();stack.Children.Add(image);stack.Children.Add(label);
  var tile=new Border(){Width=82,Margin=new Thickness(2),Padding=new Thickness(4,4,4,6),CornerRadius=new CornerRadius(6),Background=Brushes.Transparent,Child=stack,Tag=e,ToolTip=e.Path.StartsWith("::")?e.Name:e.Path};
  ToolTipService.SetInitialShowDelay(tile,900);
  tile.MouseEnter+=delegate{Paint(tile);};tile.MouseLeave+=delegate{Paint(tile);};
  tile.MouseLeftButtonDown+=TileDown;tile.MouseMove+=TileMove;tile.MouseLeftButtonUp+=delegate{dragArmed=false;};
  tile.MouseRightButtonUp+=TileRight;
  service.LoadIcon(e,delegate(BitmapSource icon){image.Source=icon;});
  return tile;
 }
 void Paint(Border tile){var e=(DesktopEntry)tile.Tag;tile.Background=selection.Contains(e.Key)?Selected:tile.IsMouseOver?Hover:Brushes.Transparent;}
 void PaintAll(){foreach(var t in tiles.Values)Paint(t);}
 void ClearSelection(){selection.Clear();PaintAll();}
 List<DesktopEntry> SelectedEntries(){return entries.Where(x=>selection.Contains(x.Key)).ToList();}

 void TileDown(object sender,MouseButtonEventArgs e){
  var tile=(Border)sender;var entry=(DesktopEntry)tile.Tag;Keyboard.Focus(root);e.Handled=true;
  if(e.ClickCount==2){service.Open(new[]{entry});return;}
  bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0,shift=(Keyboard.Modifiers&ModifierKeys.Shift)!=0;
  if(shift&&anchorKey!=null){
   int a=entries.FindIndex(x=>x.Key==anchorKey),b=entries.IndexOf(entry);
   if(!ctrl)selection.Clear();if(a>=0)for(int i=Math.Min(a,b);i<=Math.Max(a,b);i++)selection.Add(entries[i].Key);
  }else if(ctrl){if(!selection.Remove(entry.Key))selection.Add(entry.Key);anchorKey=entry.Key;}
  else{if(!selection.Contains(entry.Key)){selection.Clear();selection.Add(entry.Key);}anchorKey=entry.Key;}
  PaintAll();dragStart=e.GetPosition(root);dragArmed=true;
 }
 void TileMove(object sender,MouseEventArgs e){
  if(!dragArmed||e.LeftButton!=MouseButtonState.Pressed)return;
  var p=e.GetPosition(root);
  if(Math.Abs(p.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
  dragArmed=false;
  var files=SelectedEntries().Where(x=>!x.IsVirtual).Select(x=>x.Path).ToArray();if(files.Length==0)return;
  var data=new DataObject();data.SetData(DataFormats.FileDrop,files);data.SetData(DragFormat,Config.Id);
  try{DragDrop.DoDragDrop((DependencyObject)sender,data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link);}catch(Exception x){service.Say("拖动失败："+x.Message);}
 }
 void TileRight(object sender,MouseButtonEventArgs e){
  e.Handled=true;var entry=(DesktopEntry)((Border)sender).Tag;Keyboard.Focus(root);
  if(!selection.Contains(entry.Key)){selection.Clear();selection.Add(entry.Key);anchorKey=entry.Key;PaintAll();}
  var cursor=Forms.Cursor.Position;
  service.ShowItemMenu(this,SelectedEntries(),cursor.X,cursor.Y,delegate{BeginRename(entry);});
 }
 void KeyDown(object sender,KeyEventArgs e){
  if(e.OriginalSource is TextBox)return;
  var chosen=SelectedEntries();
  if(e.Key==Key.Enter&&chosen.Count>0){service.Open(chosen);e.Handled=true;}
  else if(e.Key==Key.F2&&chosen.Count==1){BeginRename(chosen[0]);e.Handled=true;}
  else if(e.Key==Key.Delete&&chosen.Count>0){service.Recycle(chosen);e.Handled=true;}
  else if(e.Key==Key.A&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){foreach(var x in entries)selection.Add(x.Key);PaintAll();e.Handled=true;}
  else if(e.Key==Key.Escape){ClearSelection();e.Handled=true;}
 }

 // ---- Rename in place
 public void BeginRename(DesktopEntry entry){
  if(entry==null||entry.IsVirtual)return;
  Border tile;if(!tiles.TryGetValue(entry.Key+"|"+entry.Name,out tile))return;
  var stack=(StackPanel)tile.Child;var label=(TextBlock)stack.Children[1];
  var box=new TextBox(){Text=entry.Name,FontSize=12,TextWrapping=TextWrapping.Wrap,TextAlignment=TextAlignment.Center,MaxHeight=52,AcceptsReturn=false,Padding=new Thickness(0)};
  stack.Children[1]=box;bool done=false;
  Action<bool> finish=delegate(bool commit){
   if(done)return;done=true;stack.Children[1]=label;Keyboard.Focus(root);
   if(commit&&box.Text.Trim()!=entry.Name)service.Rename(entry,box.Text.Trim());
  };
  box.KeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Enter){finish(true);e.Handled=true;}else if(e.Key==Key.Escape){finish(false);e.Handled=true;}};
  box.LostKeyboardFocus+=delegate{finish(true);};
  box.Loaded+=delegate{box.Focus();box.SelectAll();};
  if(box.IsLoaded){box.Focus();box.SelectAll();}
 }

 // ---- Drag in
 void DragOver(object sender,DragEventArgs e){
  e.Effects=service.DropEffect(this,e.Data,e.KeyStates);e.Handled=true;
  root.BorderBrush=e.Effects==DragDropEffects.None?Line:DropMark;
 }
 void OnDrop(object sender,DragEventArgs e){
  root.BorderBrush=Line;e.Handled=true;
  var effect=service.DropEffect(this,e.Data,e.KeyStates);if(effect==DragDropEffects.None)return;
  // Index of the tile under the pointer: free fences insert there.
  int index=entries.Count;var point=e.GetPosition(wrap);
  for(int i=0;i<wrap.Children.Count;i++){var t=(FrameworkElement)wrap.Children[i];var at=t.TranslatePoint(new Point(0,0),wrap);
   if(point.Y<at.Y+t.ActualHeight&&(point.Y<at.Y||point.X<at.X+t.ActualWidth/2)){index=i;break;}}
  e.Effects=effect;
  // Copy the data out now (the OLE data object is released after Drop returns) and let the drag finish
  // before file operations show their dialogs.
  var files=e.Data.GetData(DataFormats.FileDrop) as string[];
  Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate{service.Drop(this,files,index,effect);}));
 }

 // ---- Fence menu (title bar / empty area)
 void ShowFenceMenu(){
  var menu=new ContextMenu();
  Func<string,Action,MenuItem> item=delegate(string text,Action run){var m=new MenuItem(){Header=text};m.Click+=delegate{run();};return m;};
  menu.Items.Add(item(Config.Collapsed?"展开":"收起",delegate{service.ToggleCollapsed(this);}));
  if(Config.Kind!="other")menu.Items.Add(item("重命名分区",BeginTitleRename));
  if(Config.Kind=="rule")menu.Items.Add(new MenuItem(){Header="规则："+Config.Rule.Describe(),IsEnabled=false});
  if(Config.Kind=="folder")menu.Items.Add(item("在资源管理器中打开",delegate{if(Directory.Exists(Config.Folder))ShellItems.Open(Config.Folder);}));
  menu.Items.Add(new Separator());
  var add=new MenuItem(){Header="新建分区"};
  foreach(var id in FenceRule.PresetIds){string preset=id;add.Items.Add(item("规则："+FenceRule.PresetTitle(id),delegate{service.AddPreset(preset,Config.Display);}));}
  add.Items.Add(new Separator());
  add.Items.Add(item("自由分区",delegate{service.AddFree("收纳",Config.Display);}));
  add.Items.Add(item("文件夹分区…",delegate{service.AddFolderInteractive(Config.Display);}));
  menu.Items.Add(add);
  menu.Items.Add(item(service.Store.Unlocked?"锁定布局":"解锁布局（拖动标题移动，右下角改大小）",delegate{service.SetUnlocked(!service.Store.Unlocked);}));
  if(Config.Kind!="other"){menu.Items.Add(new Separator());menu.Items.Add(item("删除分区（文件不受影响）",delegate{service.Remove(Config.Id);}));}
  menu.PlacementTarget=root;menu.Placement=PlacementMode.MousePoint;menu.IsOpen=true;
 }
 void BeginTitleRename(){
  var dock=(DockPanel)titleBar.Children[0];
  var box=new TextBox(){Text=title.Text,FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
  int at=dock.Children.IndexOf(title);dock.Children[at]=box;bool done=false;
  Action<bool> finish=delegate(bool commit){if(done)return;done=true;dock.Children[at]=title;Keyboard.Focus(root);if(commit&&box.Text.Trim().Length>0)service.SetTitle(this,box.Text.Trim());};
  box.KeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Enter){finish(true);e.Handled=true;}else if(e.Key==Key.Escape){finish(false);e.Handled=true;}};
  box.LostKeyboardFocus+=delegate{finish(true);};
  box.Loaded+=delegate{box.Focus();box.SelectAll();};
 }

 // ---- Move / resize (unlocked layout), snapped to a 10-pixel grid inside the display.
 void TitleDown(object sender,MouseButtonEventArgs e){
  Keyboard.Focus(root);
  if(e.ClickCount==2){service.ToggleCollapsed(this);e.Handled=true;return;}
  if(!service.Store.Unlocked)return;
  BeginGesture(1);titleBar.CaptureMouse();e.Handled=true;
 }
 void BeginGesture(int kind){gesture=kind;gestureCursor=Forms.Cursor.Position;gestureBounds=Window.Bounds;}
 void GestureMove(object sender,MouseEventArgs e){
  if(gesture==0)return;
  var c=Forms.Cursor.Position;int dx=c.X-gestureCursor.X,dy=c.Y-gestureCursor.Y;var b=gestureBounds;
  if(gesture==1){b.X=Snap(b.X+dx);b.Y=Snap(b.Y+dy);}
  else{int min=(int)Math.Round(160*service.Scale);b.Width=Math.Max(min,Snap(b.Width+dx));b.Height=Math.Max((int)Math.Round(80*service.Scale),Snap(b.Height+dy));}
  b=service.ClampToDisplay(Config,b);
  if(b!=Window.Bounds)Mount(b);
 }
 static int Snap(int v){return (int)Math.Round(v/10.0)*10;}
 void GestureUp(object sender,MouseButtonEventArgs e){
  if(gesture==0)return;gesture=0;((UIElement)sender).ReleaseMouseCapture();
  service.Moved(this,Window.Bounds);
 }
 public void Close(){Removed=true;if(Window!=null&&!Window.IsClosed)Window.Close();}
}
}
