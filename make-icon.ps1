$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
public static class WpmIconArt {
 public static byte[] Draw(int size) {
  using(var b=new Bitmap(size,size,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(b)) {
   g.SmoothingMode=SmoothingMode.AntiAlias;g.PixelOffsetMode=PixelOffsetMode.HighQuality;g.ScaleTransform(size/256f,size/256f);
   using(var p=new GraphicsPath()) {
    p.AddArc(8,8,108,108,180,90);p.AddArc(140,8,108,108,270,90);p.AddArc(140,140,108,108,0,90);p.AddArc(8,140,108,108,90,90);p.CloseFigure();
    using(var brush=new LinearGradientBrush(new Rectangle(8,8,240,240),Color.FromArgb(66,92,230),Color.FromArgb(116,72,218),45))g.FillPath(brush,p);
   }
   using(var path=new GraphicsPath()) {
    path.AddPolygon(new[]{new PointF(39,185),new PointF(82,65),new PointF(109,65),new PointF(152,185),new PointF(123,185),new PointF(115,159),new PointF(75,159),new PointF(67,185)});
    path.AddPolygon(new[]{new PointF(83,135),new PointF(107,135),new PointF(95,93)});
    g.FillPath(Brushes.White,path);
   }
   using(var pen=new Pen(Color.FromArgb(203,234,255),16)) {
    pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
    g.DrawLine(pen,160,126,211,126);g.DrawLines(pen,new[]{new Point(186,99),new Point(213,126),new Point(186,153)});
   }
   using(var brush=new SolidBrush(Color.FromArgb(189,233,255)))g.FillEllipse(brush,194,57,20,20);
   using(var m=new MemoryStream()){b.Save(m,ImageFormat.Png);return m.ToArray();}
  }
 }
}
'@
$sizes=@(16,24,32,48,64,128,256)
$images=@();foreach($size in $sizes){$images+=,([WpmIconArt]::Draw($size))}
$path=Join-Path $PSScriptRoot 'assets\WinmmProxyMaker.ico'
$stream=[IO.File]::Create($path)
$writer=New-Object IO.BinaryWriter($stream)
try {
 $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Length)
 $offset=6+16*$sizes.Length
 for($i=0;$i -lt $sizes.Length;$i++){
  $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
  $writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0)
  $writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$images[$i].Length);$writer.Write([uint32]$offset)
  $offset+=$images[$i].Length
 }
 foreach($image in $images){$writer.Write([byte[]]$image)}
} finally {$writer.Dispose();$stream.Dispose()}
[IO.File]::WriteAllBytes((Join-Path $PSScriptRoot 'assets\icon-preview.png'),[WpmIconArt]::Draw(256))
