# Regenerate the existing >_< mark with Windows' built-in WPF renderer.
# Run with Windows PowerShell -NoProfile -STA -File desktop/generate-icon.ps1.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@()
foreach($size in $sizes){
 $visual=New-Object System.Windows.Media.DrawingVisual
 $draw=$visual.RenderOpen()
 try{
  $draw.PushTransform((New-Object System.Windows.Media.ScaleTransform ($size/48.0),($size/48.0)))
  $background=[System.Windows.Media.BrushConverter]::new().ConvertFromString('#102033')
  $foreground=[System.Windows.Media.BrushConverter]::new().ConvertFromString('#73D8CE')
  $draw.DrawRoundedRectangle($background,$null,[System.Windows.Rect]::new(0,0,48,48),10,10)
  $pen=[System.Windows.Media.Pen]::new($foreground,4)
  $pen.StartLineCap=$pen.EndLineCap=[System.Windows.Media.PenLineCap]::Round
  $pen.LineJoin=[System.Windows.Media.PenLineJoin]::Round
  $draw.DrawGeometry($null,$pen,[System.Windows.Media.Geometry]::Parse('M9,14 L18,23 L9,32 M39,14 L30,23 L39,32 M20,34 L28,34'))
  $draw.Pop()
 }finally{$draw.Close()}
 $bitmap=[System.Windows.Media.Imaging.RenderTargetBitmap]::new($size,$size,96,96,[System.Windows.Media.PixelFormats]::Pbgra32)
 $bitmap.Render($visual)
 $encoder=[System.Windows.Media.Imaging.PngBitmapEncoder]::new()
 $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $stream=[System.IO.MemoryStream]::new()
 try{$encoder.Save($stream);$frames+=,@{Size=$size;Bytes=$stream.ToArray()}}finally{$stream.Dispose()}
}
$directory=Join-Path $PSScriptRoot 'assets'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$path=Join-Path $directory 'TerminalLink.ico'
$file=[System.IO.File]::Create($path)
$writer=[System.IO.BinaryWriter]::new($file)
try{
 $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count)
 $offset=6+16*$frames.Count
 foreach($frame in $frames){
  $dimension=if($frame.Size -eq 256){0}else{$frame.Size}
  $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
  $writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32)
  $writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset)
  $offset+=$frame.Bytes.Length
 }
 foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}
}finally{$writer.Dispose();$file.Dispose()}
Get-FileHash -LiteralPath $path -Algorithm SHA256
