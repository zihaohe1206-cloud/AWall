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
}
