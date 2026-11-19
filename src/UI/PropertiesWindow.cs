using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace AWall {
// Small editor window. Every change is pushed live through onChange; saving is debounced.
public sealed class PropertiesWindow : Window {
 readonly Item item; readonly List<WebProperty> props; readonly Dictionary<string,object> values; readonly Action<string,object> onChange;
 readonly DispatcherTimer saveTimer=new DispatcherTimer(){Interval=TimeSpan.FromMilliseconds(400)};
 readonly StackPanel rows=new StackPanel(); bool building;
 public PropertiesWindow(Window owner,Item item,List<WebProperty> props,Action<string,object> onChange,bool dark){
  this.item=item;this.props=props;this.onChange=onChange;values=WebProperties.Values(item,props);
  Owner=owner;Resources=owner.Resources;Title="壁纸参数 · "+item.Title;Width=420;SizeToContent=SizeToContent.Height;MaxHeight=680;
  ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  SetResourceReference(BackgroundProperty,"Bg");SetResourceReference(ForegroundProperty,"Text");FontFamily=owner.FontFamily;FontSize=13;UseLayoutRounding=true;
  SourceInitialized+=delegate{Theme.TitleBar(new WindowInteropHelper(this).Handle,dark);};
  saveTimer.Tick+=delegate{saveTimer.Stop();Persist();};
  Closed+=delegate{if(saveTimer.IsEnabled){saveTimer.Stop();Persist();}};
  var dock=new DockPanel(){Margin=new Thickness(20,16,20,16)};Content=dock;
  var footer=new DockPanel(){Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(footer,Dock.Bottom);dock.Children.Add(footer);
  var close=new Button(){Content="完成",Style=TryFindResource("PrimaryButton") as Style,IsDefault=true,IsCancel=true};close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);
  var reset=new Button(){Content="恢复默认",Style=TryFindResource("SoftButton") as Style,HorizontalAlignment=HorizontalAlignment.Left};reset.Click+=delegate{ResetAll();};footer.Children.Add(reset);
  var scroll=new ScrollViewer(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=rows};dock.Children.Add(scroll);
  Build();
 }
 void Persist(){try{WebProperties.Save(item,values);}catch{}}
 void Changed(WebProperty p,object value){
  if(building)return;value=p.Normalize(value);if(value==null)return;
  values[p.Name]=value;if(onChange!=null)onChange(p.Name,value);saveTimer.Stop();saveTimer.Start();
 }
 void ResetAll(){
  saveTimer.Stop();WebProperties.Reset(item);
  foreach(var p in props){values[p.Name]=p.Default;if(onChange!=null)onChange(p.Name,p.Default);}
  Build();
 }
 void Build(){
  building=true;rows.Children.Clear();
  foreach(var prop in props){
   var p=prop;var row=new Grid(){Margin=new Thickness(0,0,0,14)};rows.Children.Add(row);
   row.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(110)});row.ColumnDefinitions.Add(new ColumnDefinition());
   var label=new TextBlock(){Text=p.Label,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=p.Label};label.SetResourceReference(TextBlock.ForegroundProperty,"Muted");row.Children.Add(label);
   FrameworkElement editor;object v=values[p.Name];
   if(p.Type=="slider"){
    var panel=new DockPanel();var readout=new TextBlock(){Width=52,TextAlignment=TextAlignment.Right,VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(readout,Dock.Right);panel.Children.Add(readout);
    int decimals=Math.Max(0,Math.Min(4,(int)Math.Ceiling(-Math.Log10(p.Step)-1e-9)));
    var slider=new Slider(){Minimum=p.Min,Maximum=p.Max,SmallChange=p.Step,LargeChange=p.Step*10,TickFrequency=p.Step,IsSnapToTickEnabled=true,Value=(double)v,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)};
    readout.Text=((double)v).ToString("F"+decimals,CultureInfo.InvariantCulture);
    slider.ValueChanged+=delegate{readout.Text=slider.Value.ToString("F"+decimals,CultureInfo.InvariantCulture);Changed(p,Math.Round(slider.Value,decimals));};
    panel.Children.Add(slider);editor=panel;
   }else if(p.Type=="bool"){
    var check=new CheckBox(){IsChecked=(bool)v,Style=TryFindResource("Switch") as Style,VerticalAlignment=VerticalAlignment.Center};
    check.Click+=delegate{Changed(p,check.IsChecked==true);};editor=check;
   }else if(p.Type=="color"){
    var panel=new StackPanel(){Orientation=Orientation.Horizontal};
    var swatch=new Border(){Width=28,Height=28,CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,10,0),Cursor=System.Windows.Input.Cursors.Hand,ToolTip="选择颜色"};
    swatch.SetResourceReference(Border.BorderBrushProperty,"Line");
    var text=new TextBox(){Width=96,Text=(string)v,VerticalContentAlignment=VerticalAlignment.Center,Style=TryFindResource("SearchBox") as Style};
    Action<string> paint=delegate(string hex){try{swatch.Background=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));}catch{}};paint((string)v);
    text.TextChanged+=delegate{var n=p.Normalize(text.Text.Trim());if(n!=null){paint((string)n);Changed(p,n);}};
    swatch.MouseLeftButtonUp+=delegate{
     var c=(Color)ColorConverter.ConvertFromString((string)values[p.Name]);
     using(var dialog=new Forms.ColorDialog(){FullOpen=true,Color=System.Drawing.Color.FromArgb(c.R,c.G,c.B)}){
      if(dialog.ShowDialog()==Forms.DialogResult.OK)text.Text=String.Format("#{0:x2}{1:x2}{2:x2}",dialog.Color.R,dialog.Color.G,dialog.Color.B);
     }
    };
    panel.Children.Add(swatch);panel.Children.Add(text);editor=panel;
   }else{
    var combo=new ComboBox(){MinWidth=160,HorizontalAlignment=HorizontalAlignment.Left,DisplayMemberPath="Value",SelectedValuePath="Key",ItemsSource=p.Options,SelectedValue=v};
    combo.SelectionChanged+=delegate{if(combo.SelectedValue!=null)Changed(p,combo.SelectedValue);};editor=combo;
   }
   Grid.SetColumn(editor,1);row.Children.Add(editor);
  }
  building=false;
 }
}
}
