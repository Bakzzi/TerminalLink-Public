using System.Windows;
using System.Windows.Automation;
using Media=System.Windows.Media;

namespace TerminalLink;
public partial class DesktopWindow {
 void InitializeChrome(){
  MinimizeButton.Click+=(_,_)=>SystemCommands.MinimizeWindow(this);
  MaximizeButton.Click+=(_,_)=>{if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);};
  CloseButton.Click+=(_,_)=>Close();
  StateChanged+=(_,_)=>UpdateChrome();UpdateChrome();
  SizeChanged+=(_,_)=>UpdateChromeInsets();LocationChanged+=(_,_)=>UpdateChromeInsets();DpiChanged+=(_,_)=>UpdateChromeInsets();
 }
 void UpdateChrome(){
  bool maximized=WindowState==WindowState.Maximized;
  MaximizeGlyph.Data=Media.Geometry.Parse(maximized?"M2,0 L10,0 10,8 M0,2 L8,2 8,10 0,10 Z":"M0,0 L10,0 10,10 0,10 Z");
  string title=maximized?"이전 크기로":"최대화";MaximizeButton.ToolTip=title;AutomationProperties.SetName(MaximizeButton,title);
  UpdateChromeInsets();
 }
 void UpdateChromeInsets(){
  if(WindowState!=WindowState.Maximized){WindowFrame.Margin=new Thickness(0);WindowFrame.BorderThickness=new Thickness(1);return;}
  if(PresentationSource.FromVisual(this)==null||ActualWidth<=0||ActualHeight<=0)return;
  // Windows maximizes its invisible resize edges past the work area. Keep our
  // client-drawn caption inside the actual work area, including per-monitor DPI.
  var area=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
  var origin=PointToScreen(new System.Windows.Point(0,0));var dpi=Media.VisualTreeHelper.GetDpi(this);
  WindowFrame.Margin=new Thickness(Math.Max(0,(area.Left-origin.X)/dpi.DpiScaleX),Math.Max(0,(area.Top-origin.Y)/dpi.DpiScaleY),Math.Max(0,(origin.X+ActualWidth*dpi.DpiScaleX-area.Right)/dpi.DpiScaleX),Math.Max(0,(origin.Y+ActualHeight*dpi.DpiScaleY-area.Bottom)/dpi.DpiScaleY));
  WindowFrame.BorderThickness=new Thickness(0);
 }
}
