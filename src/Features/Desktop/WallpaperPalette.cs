using System;
using System.IO;
using System.Windows.Media;

namespace AWall {
// Fence colours derived from the wallpaper. Cards stay dark (text must stay readable over any wallpaper);
// the wallpaper decides their hue, and its most saturated colour family becomes the selection / accent.
public sealed class FencePalette {
 public Color Back,Line,Hover,Selected,Text,Muted,Accent;
 public static readonly FencePalette Default=new FencePalette(){
  Back=Color.FromRgb(0x1F,0x22,0x29),Line=Color.FromRgb(0x34,0x3A,0x46),Hover=Color.FromRgb(0x2C,0x31,0x3B),Selected=Color.FromRgb(0x33,0x49,0x6E),
  Text=Color.FromRgb(0xF0,0xF2,0xF7),Muted=Color.FromRgb(0x9A,0xA3,0xB5),Accent=Color.FromRgb(0x5E,0xA8,0xFF)};
 public static FencePalette From(Color average,Color accent){
  double h,s,l,ah,as_,al;WallpaperPalette.ToHsl(average,out h,out s,out l);WallpaperPalette.ToHsl(accent,out ah,out as_,out al);
  double tint=Math.Min(s,0.32);// a hint of the wallpaper's hue, never a loud card
  return new FencePalette(){
   Back=WallpaperPalette.FromHsl(h,tint,0.13),Line=WallpaperPalette.FromHsl(h,tint,0.24),Hover=WallpaperPalette.FromHsl(h,tint,0.19),
   Selected=WallpaperPalette.FromHsl(ah,Math.Max(0.30,Math.Min(as_,0.55)),0.32),
   Text=Color.FromRgb(0xF0,0xF2,0xF7),Muted=WallpaperPalette.FromHsl(h,Math.Min(s,0.14),0.68),
   Accent=WallpaperPalette.FromHsl(ah,Math.Max(0.45,Math.Min(as_,0.8)),0.62)};
 }
 public bool SameAs(FencePalette o){return o!=null&&o.Back==Back&&o.Selected==Selected&&o.Line==Line;}
}

public static class WallpaperPalette {
 // Null path or unreadable image -> Default.
 public static FencePalette FromImage(string path){
  if(String.IsNullOrEmpty(path)||!File.Exists(path))return FencePalette.Default;
  try{
   using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
   using(var image=System.Drawing.Image.FromStream(stream,false,false)){Color average,accent;Analyze(image,out average,out accent);return FencePalette.From(average,accent);}
  }catch{return FencePalette.Default;}
 }
 // Average colour, and the mean colour of the hue family (24 bins) with the most saturated, bright weight.
 public static void Analyze(System.Drawing.Image image,out Color average,out Color accent){
  const int W=64,H=36;
  using(var small=new System.Drawing.Bitmap(W,H))
  using(var g=System.Drawing.Graphics.FromImage(small)){
   g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;g.DrawImage(image,0,0,W,H);
   double r=0,gr=0,b=0;var weight=new double[24];var sum=new double[24,3];
   for(int y=0;y<H;y++)for(int x=0;x<W;x++){
    var p=small.GetPixel(x,y);r+=p.R;gr+=p.G;b+=p.B;
    double h,s,v;ToHsv(p.R,p.G,p.B,out h,out s,out v);
    if(s<0.2||v<0.15)continue;
    int bin=(int)(h/15)%24;double w=s*v;weight[bin]+=w;sum[bin,0]+=p.R*w;sum[bin,1]+=p.G*w;sum[bin,2]+=p.B*w;
   }
   int n=W*H;average=Color.FromRgb((byte)(r/n),(byte)(gr/n),(byte)(b/n));
   int best=-1;double top=0;for(int i=0;i<24;i++){double around=weight[i]+0.5*(weight[(i+23)%24]+weight[(i+1)%24]);if(around>top){top=around;best=i;}}
   // Too little colour (grey / black / white wallpapers): the accent follows the average.
   if(best<0||weight[best]<n*0.02)accent=average;
   else accent=Color.FromRgb((byte)(sum[best,0]/weight[best]),(byte)(sum[best,1]/weight[best]),(byte)(sum[best,2]/weight[best]));
  }
 }
 static void ToHsv(int r,int g,int b,out double h,out double s,out double v){
  double R=r/255.0,G=g/255.0,B=b/255.0,max=Math.Max(R,Math.Max(G,B)),min=Math.Min(R,Math.Min(G,B)),d=max-min;
  v=max;s=max==0?0:d/max;h=0;
  if(d>0){if(max==R)h=60*(((G-B)/d)%6);else if(max==G)h=60*((B-R)/d+2);else h=60*((R-G)/d+4);}
  if(h<0)h+=360;
 }
 public static void ToHsl(Color c,out double h,out double s,out double l){
  double R=c.R/255.0,G=c.G/255.0,B=c.B/255.0,max=Math.Max(R,Math.Max(G,B)),min=Math.Min(R,Math.Min(G,B)),d=max-min;
  l=(max+min)/2;s=d==0?0:d/(1-Math.Abs(2*l-1));h=0;
  if(d>0){if(max==R)h=60*(((G-B)/d)%6);else if(max==G)h=60*((B-R)/d+2);else h=60*((R-G)/d+4);}
  if(h<0)h+=360;
 }
 public static Color FromHsl(double h,double s,double l){
  double c=(1-Math.Abs(2*l-1))*s,x=c*(1-Math.Abs((h/60)%2-1)),m=l-c/2,r=0,g=0,b=0;
  if(h<60){r=c;g=x;}else if(h<120){r=x;g=c;}else if(h<180){g=c;b=x;}else if(h<240){g=x;b=c;}else if(h<300){r=x;b=c;}else{r=c;b=x;}
  return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
 }
}
}
