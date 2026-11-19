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
// Adjustable parameters of an HTML wallpaper. The page declares them itself, so a
// single AI-generated file stays self-contained:
//   <script type="application/json" id="awall-properties">
//   {"speed":{"type":"slider","label":"速度","min":0.2,"max":3,"step":0.1,"value":1},
//    "tint":{"type":"color","label":"主色","value":"#5ea8ff"},
//    "clock":{"type":"bool","label":"显示时钟","value":true},
//    "mode":{"type":"combo","label":"风格","options":[{"value":"calm","label":"平静"},{"value":"wild","label":"狂野"}],"value":"calm"}}
//   </script>
// The user's values live in %LOCALAPPDATA%\AWall\properties\<file>.json, never in the library file.
public sealed class WebProperty {
 public string Name, Type, Label; public double Min, Max=1, Step=0.01; public object Default;
 public List<KeyValuePair<string,string>> Options=new List<KeyValuePair<string,string>>();
 static readonly Regex Hex=new Regex("^#[0-9a-fA-F]{6}$");
 // Coerce a stored or incoming value to this property's type; null when it cannot be used.
 public object Normalize(object value){
  try{
   if(value==null)return null;
   if(Type=="slider"){double d=Convert.ToDouble(value,CultureInfo.InvariantCulture);if(Double.IsNaN(d)||Double.IsInfinity(d))return null;return Math.Max(Min,Math.Min(Max,d));}
   if(Type=="bool"){if(value is bool)return value;return Convert.ToBoolean(value,CultureInfo.InvariantCulture);}
   string s=Convert.ToString(value,CultureInfo.InvariantCulture);
   if(Type=="color")return Hex.IsMatch(s)?s.ToLowerInvariant():null;
   if(Type=="combo")return Options.Any(o=>o.Key==s)?s:null;
  }catch{}
  return null;
 }
}

public static class WebProperties {
 static readonly Regex Block=new Regex("<script\\b[^>]*\\bid\\s*=\\s*[\"']awall-properties[\"'][^>]*>(.*?)</script>",RegexOptions.IgnoreCase|RegexOptions.Singleline);
 static string Folder {get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AWall","properties");}}
 static string StorePath(Item item){return Path.Combine(Folder,Path.GetFileName(item.File)+".json");}

 // Declared properties in page order; empty when the page declares none or the block is malformed.
 public static List<WebProperty> Parse(Item item,out string error){
  error=null;var list=new List<WebProperty>();
  try{
   if(item==null||item.Type!="web"||!File.Exists(item.File))return list;
   var m=Block.Match(File.ReadAllText(item.File,Encoding.UTF8));if(!m.Success)return list;
   var root=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(m.Groups[1].Value);
   foreach(var pair in root){
    var d=pair.Value as Dictionary<string,object>;if(d==null)continue;
    var p=new WebProperty(){Name=pair.Key,Type=Str(d,"type","").ToLowerInvariant(),Label=Str(d,"label",pair.Key)};
    if(p.Type=="slider"){p.Min=Num(d,"min",0);p.Max=Num(d,"max",1);p.Step=Num(d,"step",(p.Max-p.Min)/100);if(p.Max<=p.Min||p.Step<=0)continue;}
    else if(p.Type=="combo"){
     var arr=d.ContainsKey("options")?d["options"] as System.Collections.ArrayList:null;
     var obj=d.ContainsKey("options")?d["options"] as Dictionary<string,object>:null;
     if(arr!=null)foreach(var o in arr){var od=o as Dictionary<string,object>;if(od!=null)p.Options.Add(new KeyValuePair<string,string>(Str(od,"value",""),Str(od,"label",Str(od,"value",""))));else if(o!=null)p.Options.Add(new KeyValuePair<string,string>(Convert.ToString(o),Convert.ToString(o)));}
     else if(obj!=null)foreach(var o in obj)p.Options.Add(new KeyValuePair<string,string>(o.Key,Convert.ToString(o.Value)));
     if(p.Options.Count==0)continue;
    }
    else if(p.Type!="bool"&&p.Type!="color")continue;
    p.Default=p.Normalize(d.ContainsKey("value")?d["value"]:null)??(p.Type=="slider"?(object)p.Min:p.Type=="bool"?(object)false:p.Type=="color"?"#ffffff":p.Options[0].Key);
    list.Add(p);
   }
  }catch(Exception e){error="参数声明格式有误："+e.Message;list.Clear();}
  return list;
 }
 public static List<WebProperty> Parse(Item item){string error;return Parse(item,out error);}

 // Defaults overridden by the user's saved values.
 public static Dictionary<string,object> Values(Item item,List<WebProperty> props){
  var values=props.ToDictionary(p=>p.Name,p=>p.Default);
  try{
   string path=StorePath(item);if(!File.Exists(path))return values;
   var saved=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));
   foreach(var p in props){object v;if(saved!=null&&saved.TryGetValue(p.Name,out v)){var n=p.Normalize(v);if(n!=null)values[p.Name]=n;}}
  }catch{}
  return values;
 }
 public static Dictionary<string,object> Values(Item item){return Values(item,Parse(item));}
 public static void Save(Item item,Dictionary<string,object> values){
  Directory.CreateDirectory(Folder);
  File.WriteAllText(StorePath(item),new JavaScriptSerializer().Serialize(values),Encoding.UTF8);
 }
 public static void Reset(Item item){try{File.Delete(StorePath(item));}catch{}}
 public static string Json(object value){return new JavaScriptSerializer().Serialize(value);}

 static string Str(Dictionary<string,object> d,string key,string fallback){object v;return d.TryGetValue(key,out v)&&v!=null?Convert.ToString(v,CultureInfo.InvariantCulture):fallback;}
 static double Num(Dictionary<string,object> d,string key,double fallback){object v;try{return d.TryGetValue(key,out v)&&v!=null?Convert.ToDouble(v,CultureInfo.InvariantCulture):fallback;}catch{return fallback;}}
}

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
