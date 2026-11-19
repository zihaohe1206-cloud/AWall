// Wallpaper Engine import confirmation window (logic in Core/Library/WeImport.cs).
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace AWall {
// The confirmation list. Shares the main window's theme resources.
public sealed class WeImportWindow : Window {
 readonly List<KeyValuePair<CheckBox, WeCandidate>> rows = new List<KeyValuePair<CheckBox, WeCandidate>>();
 public CheckBox CopyAssets;
 public bool ChangeFolder;
 public List<WeCandidate> Chosen { get { return rows.Where(r => r.Key.IsChecked == true).Select(r => r.Value).ToList(); } }

 public WeImportWindow(Window owner, bool dark, string workshop, List<WeCandidate> list, int unsupported, bool offerAssets) {
  Owner = owner; Title = "从 Wallpaper Engine 导入"; Width = 640; Height = 620; MinWidth = 520; MinHeight = 420;
  WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
  Resources.MergedDictionaries.Add(owner.Resources);
  SetResourceReference(BackgroundProperty, "Bg"); SetResourceReference(ForegroundProperty, "Text");
  FontFamily = owner.FontFamily; FontSize = 13; UseLayoutRounding = true;
  Icon = owner.Icon;
  SourceInitialized += delegate { Theme.TitleBar(new WindowInteropHelper(this).Handle, dark); };

  var grid = new Grid() { Margin = new Thickness(20) };
  foreach (var h in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) grid.RowDefinitions.Add(new RowDefinition() { Height = h });
  Content = grid;

  // Row 0: source folder + change.
  var head = new DockPanel() { LastChildFill = true };
  var change = MakeButton("更换目录", "SoftButton"); change.Click += delegate { ChangeFolder = true; DialogResult = false; };
  DockPanel.SetDock(change, Dock.Right); head.Children.Add(change);
  var src = new StackPanel() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
  src.Children.Add(Faint(new TextBlock() { Text = "来源目录", FontSize = 12 }));
  src.Children.Add(new TextBlock() { Text = workshop, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = workshop, Margin = new Thickness(0, 2, 0, 0) });
  head.Children.Add(src);
  grid.Children.Add(head);

  // Row 1: summary.
  int fresh = list.Count(x => !x.Duplicate), dup = list.Count - fresh;
  var summary = Faint(new TextBlock() { Margin = new Thickness(0, 14, 0, 8), TextWrapping = TextWrapping.Wrap,
   Text = "找到 " + list.Count + " 个可用素材：新素材 " + fresh + " 个（默认选中），已在素材库 " + dup + " 个" + (unsupported > 0 ? "；另有 " + unsupported + " 个网页 / 程序等不兼容类型，已忽略。" : "。") });
  Grid.SetRow(summary, 1); grid.Children.Add(summary);

  // Row 2: list.
  var card = new Border() { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 8, 14, 8) };
  card.SetResourceReference(Border.BackgroundProperty, "Surface"); card.SetResourceReference(Border.BorderBrushProperty, "Line");
  var stack = new StackPanel();
  var scroll = new ScrollViewer() { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
  card.Child = scroll; Grid.SetRow(card, 2); grid.Children.Add(card);
  var switchStyle = owner.TryFindResource("Switch") as Style;
  foreach (var c in list) {
   var row = new DockPanel() { Margin = new Thickness(0, 6, 0, 6) };
   var info = Faint(new TextBlock() { Text = c.Duplicate ? "已存在：" + c.DuplicateOf : SizeText(c.Size), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) });
   DockPanel.SetDock(info, Dock.Right); row.Children.Add(info);
   var label = new StackPanel() { Orientation = Orientation.Horizontal };
   label.Children.Add(Badge(c.Type == "video"));
   label.Children.Add(new TextBlock() { Text = c.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340, ToolTip = c.Title + "（工坊 ID " + c.Id + "）" });
   var check = new CheckBox() { Content = label, IsChecked = !c.Duplicate, IsEnabled = !c.Duplicate, Style = switchStyle, Opacity = c.Duplicate ? 0.55 : 1 };
   row.Children.Add(check);
   stack.Children.Add(row);
   rows.Add(new KeyValuePair<CheckBox, WeCandidate>(check, c));
  }
  if (list.Count == 0) stack.Children.Add(Faint(new TextBlock() { Text = "这个目录里没有可用的 MP4 / PKG 素材。", Margin = new Thickness(0, 8, 0, 8) }));

  // Row 3: shared assets offer.
  CopyAssets = new CheckBox() { Content = "同时复制共享资源（素材库还没有 we-assets，缺少它很多 PKG 无法正常渲染）", IsChecked = true, Style = switchStyle, Margin = new Thickness(0, 12, 0, 0), Visibility = offerAssets ? Visibility.Visible : Visibility.Collapsed };
  Grid.SetRow(CopyAssets, 3); grid.Children.Add(CopyAssets);

  // Row 4: actions.
  var foot = new DockPanel() { Margin = new Thickness(0, 16, 0, 0) };
  var all = MakeButton("全选", "SoftButton"); all.Click += delegate { foreach (var r in rows) if (r.Key.IsEnabled) r.Key.IsChecked = true; };
  var none = MakeButton("全不选", "SoftButton"); none.Click += delegate { foreach (var r in rows) r.Key.IsChecked = false; };
  var ok = MakeButton("导入所选", "PrimaryButton"); ok.Click += delegate { DialogResult = true; };
  var cancel = MakeButton("取消", "SoftButton"); cancel.Click += delegate { DialogResult = false; };
  foreach (var b in new[] { all, none }) foot.Children.Add(b);
  var right = new StackPanel() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
  right.Children.Add(cancel); right.Children.Add(ok);
  foot.Children.Add(right);
  Grid.SetRow(foot, 4); grid.Children.Add(foot);
 }

 Button MakeButton(string text, string style) {
  var b = new Button() { Content = text, Margin = new Thickness(0, 0, 8, 0) };
  var s = Owner.TryFindResource(style) as Style; if (s != null) b.Style = s;
  return b;
 }
 static T Faint<T>(T e) where T : FrameworkElement { e.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Faint"); return e; }
 static Border Badge(bool video) {
  var b = new Border() { CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
  b.SetResourceReference(Border.BackgroundProperty, video ? "VideoBack" : "SceneBack");
  var t = new TextBlock() { Text = video ? "MP4" : "PKG", FontSize = 11, FontWeight = FontWeights.SemiBold };
  t.SetResourceReference(TextBlock.ForegroundProperty, video ? "VideoFore" : "SceneFore");
  b.Child = t; return b;
 }
 static string SizeText(long bytes) { return bytes >= 1 << 30 ? (bytes / 1073741824.0).ToString("0.0") + " GB" : (bytes / 1048576.0).ToString("0.0") + " MB"; }
}
}
