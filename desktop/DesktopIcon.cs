using System.Windows.Media.Imaging;

namespace TerminalLink;

internal static class DesktopIcon {
 // The same multi-resolution artwork is embedded in the Windows executable and UI.
 static Stream Open()=>typeof(DesktopIcon).Assembly.GetManifestResourceStream("TerminalLink.Desktop.Icon")
  ?? throw new IOException("TerminalLink icon resource is missing.");
 internal static BitmapFrame Image {get;}=LoadImage();
 static BitmapFrame LoadImage(){
  using var stream=Open();
  var image=BitmapFrame.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
  image.Freeze();return image;
 }
 internal static System.Drawing.Icon CreateTrayIcon(){
  using var stream=Open();
  using var icon=new System.Drawing.Icon(stream,System.Windows.Forms.SystemInformation.SmallIconSize);
  return (System.Drawing.Icon)icon.Clone();
 }
}
