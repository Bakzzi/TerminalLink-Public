using System.Windows;
using System.Windows.Automation;
using Binding=System.Windows.Data.Binding;
using System.Windows.Input;
using System.Windows.Shell;
using Wpf=System.Windows.Controls;
using Media=System.Windows.Media;

namespace TerminalLink;

internal class ThemedWindow : Window {
 internal readonly Wpf.ContentControl Body=new();
 internal readonly Wpf.Button CaptionClose;
 readonly Wpf.Border frame;
 readonly Wpf.Button maximize;
 readonly System.Windows.Shapes.Path maximizeGlyph;
 internal static Media.Brush Brush(string color)=>(Media.Brush)new Media.BrushConverter().ConvertFromString(color)!;
 internal ThemedWindow(){
  Icon=DesktopIcon.Image;
  Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/TerminalLink-Desktop;component/Theme.xaml",UriKind.Relative)});
  Background=Brush("#151719");Foreground=Brush("#E9EAED");FontFamily=new Media.FontFamily("Malgun Gothic, Segoe UI");FontSize=14;
  Width=580;Height=420;MinWidth=400;MinHeight=180;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterOwner;
  UseLayoutRounding=true;SnapsToDevicePixels=true;
  WindowChrome.SetWindowChrome(this,new(){CaptionHeight=38,ResizeBorderThickness=new(6),GlassFrameThickness=new(0),CornerRadius=new(8),UseAeroCaptionButtons=false});
  var grid=new Wpf.Grid();grid.RowDefinitions.Add(new(){Height=new(37)});grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
  var caption=new Wpf.DockPanel();var buttons=new Wpf.StackPanel{Orientation=Wpf.Orientation.Horizontal};Wpf.DockPanel.SetDock(buttons,Wpf.Dock.Right);caption.Children.Add(buttons);
  Wpf.Button Caption(string name){var b=new Wpf.Button{Style=(Style)Resources["CaptionButton"],ToolTip=name};AutomationProperties.SetName(b,name);buttons.Children.Add(b);return b;}
  maximize=Caption("최대화");maximizeGlyph=new(){Data=Media.Geometry.Parse("M0,0 L10,0 10,10 0,10 Z"),Width=10,Height=10,Stroke=Brush("#B9C2C9"),StrokeThickness=1};maximize.Content=maximizeGlyph;
  maximize.Click+=(_,_)=>{if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);};
  CaptionClose=Caption("닫기");CaptionClose.Tag="close";CaptionClose.Content=new System.Windows.Shapes.Path{Data=Media.Geometry.Parse("M0,0 L10,10 M10,0 L0,10"),Width=10,Height=10,Stroke=Brush("#B9C2C9"),StrokeThickness=1};CaptionClose.Click+=(_,_)=>Close();
  var label=new Wpf.StackPanel{Orientation=Wpf.Orientation.Horizontal,Margin=new(16,0,8,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};
  label.Children.Add(new Wpf.TextBlock{Text=">_<",FontFamily=new("Consolas"),FontSize=16,Foreground=Brush("#73D8CE"),Margin=new(0,0,9,0)});
  var title=new Wpf.TextBlock{FontSize=12,Foreground=Brush("#A8B2BC"),VerticalAlignment=VerticalAlignment.Center};title.SetBinding(Wpf.TextBlock.TextProperty,new Binding(nameof(Title)){Source=this});label.Children.Add(title);caption.Children.Add(label);
  grid.Children.Add(new Wpf.Border{Background=Brush("#101214"),BorderBrush=Brush("#252A2E"),BorderThickness=new(0,0,0,1),Child=caption});
  Wpf.Grid.SetRow(Body,1);grid.Children.Add(Body);
  frame=new(){Background=Background,BorderBrush=Brush("#30363C"),BorderThickness=new(1),Child=grid};Content=frame;
  Loaded+=(_,_)=>{maximize.Visibility=ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip?Visibility.Visible:Visibility.Collapsed;UpdateChrome();};
  StateChanged+=(_,_)=>UpdateChrome();SizeChanged+=(_,_)=>UpdateChrome();LocationChanged+=(_,_)=>UpdateChrome();DpiChanged+=(_,_)=>UpdateChrome();
 }
 internal void SetBody(UIElement body)=>Body.Content=body;
 void UpdateChrome(){
  bool max=WindowState==WindowState.Maximized;maximizeGlyph.Data=Media.Geometry.Parse(max?"M2,0 L10,0 10,8 M0,2 L8,2 8,10 0,10 Z":"M0,0 L10,0 10,10 0,10 Z");
  string name=max?"이전 크기로":"최대화";maximize.ToolTip=name;AutomationProperties.SetName(maximize,name);
  if(!max){frame.Margin=new(0);frame.BorderThickness=new(1);return;}
  if(PresentationSource.FromVisual(this)==null||ActualWidth<=0||ActualHeight<=0)return;
  var work=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
  var origin=PointToScreen(new System.Windows.Point(0,0));var dpi=Media.VisualTreeHelper.GetDpi(this);
  frame.Margin=new(Math.Max(0,(work.Left-origin.X)/dpi.DpiScaleX),Math.Max(0,(work.Top-origin.Y)/dpi.DpiScaleY),Math.Max(0,(origin.X+ActualWidth*dpi.DpiScaleX-work.Right)/dpi.DpiScaleX),Math.Max(0,(origin.Y+ActualHeight*dpi.DpiScaleY-work.Bottom)/dpi.DpiScaleY));frame.BorderThickness=new(0);
 }
}

internal sealed class MessageDialog : ThemedWindow {
 internal readonly Wpf.Button ConfirmButton,CancelButton;
 internal MessageDialog(string title,string message,string confirm="확인",bool cancel=false,bool destructive=false){
  Title=title;Width=480;MinHeight=0;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;
  var panel=new Wpf.StackPanel{Margin=new(24)};
  panel.Children.Add(new Wpf.TextBlock{Text=message,TextWrapping=TextWrapping.Wrap,FontSize=15,LineHeight=24,Margin=new(4,2,4,24)});
  var actions=new Wpf.StackPanel{Orientation=Wpf.Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right};
  CancelButton=DesktopWindow.Button("취소");CancelButton.IsDefault=cancel;CancelButton.Visibility=cancel?Visibility.Visible:Visibility.Collapsed;CancelButton.Click+=(_,_)=>DialogResult=false;actions.Children.Add(CancelButton);
  ConfirmButton=DesktopWindow.Button(confirm);ConfirmButton.IsDefault=!cancel;ConfirmButton.Background=Brush(destructive?"#743B3B":"#397B74");ConfirmButton.Click+=(_,_)=>DialogResult=true;actions.Children.Add(ConfirmButton);
  panel.Children.Add(actions);SetBody(panel);Loaded+=(_,_)=>{if(cancel)CancelButton.Focus();else ConfirmButton.Focus();};
  PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;DialogResult=false;}};
 }
 internal static bool Confirm(Window owner,string title,string message,string confirm,bool destructive=false)=>new MessageDialog(title,message,confirm,true,destructive){Owner=owner}.ShowDialog()==true;
 internal static void Notify(Window? owner,string title,string message){var dialog=new MessageDialog(title,DesktopErrorText.Display(message));if(owner?.IsVisible==true)dialog.Owner=owner;else{dialog.WindowStartupLocation=WindowStartupLocation.CenterScreen;dialog.ShowInTaskbar=true;}dialog.ShowDialog();}
}

internal sealed class FileBoxWindow : ThemedWindow {
 internal FileBoxWindow(SharedFile[] items,Action<Window,SharedFile> save,Action<Window>? clear=null){
  Title="파일함";Width=580;Height=420;
  var list=new Wpf.StackPanel{Margin=new(24)};
  list.Children.Add(new Wpf.TextBlock{Text="임시 파일함",FontSize=20,FontWeight=FontWeights.SemiBold,Margin=new(4,0,4,8)});
  list.Children.Add(new Wpf.TextBlock{Text="필요한 파일을 선택해서 저장하세요.",Foreground=Brush("#87939A"),Margin=new(4,0,4,20)});
  foreach(var file in items){var button=DesktopWindow.Button("");button.Margin=new(4);button.Content=new Wpf.TextBlock{Text=file.name+" · 저장",TextWrapping=TextWrapping.Wrap};button.Click+=(_,_)=>save(this,file);list.Children.Add(button);}
  if(items.Length==0)list.Children.Add(new Wpf.TextBlock{Text="보관 중인 파일이 없습니다.",Foreground=Brush("#A8B2BC"),Margin=new(4,20,4,4)});
  var clearButton=DesktopWindow.Button("파일함 비우기");clearButton.Margin=new(4,16,4,4);clearButton.IsEnabled=items.Length>0&&clear!=null;clearButton.Click+=(_,_)=>clear?.Invoke(this);list.Children.Add(clearButton);
  SetBody(new Wpf.ScrollViewer{Content=list,VerticalScrollBarVisibility=Wpf.ScrollBarVisibility.Hidden});
 }
}
