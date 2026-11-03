using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf=System.Windows.Controls;
using Media=System.Windows.Media;

namespace TerminalLink;
public partial class DesktopWindow {
 static void DialogSnapshot(Window window,string name){
  window.UpdateLayout();var visual=(FrameworkElement)window.Content;
  var bitmap=new Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,Media.PixelFormats.Pbgra32);bitmap.Render(visual);
  var encoder=new Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(AppContext.BaseDirectory,name));encoder.Save(output);
 }
 async Task CheckDialogUi(Action<bool,string> check){
  if(!demo)throw new InvalidOperationException("Synthetic dialog check only");
  MessageDialog Message()=>new("새 탭","선택한 Windows Terminal 창에 새 탭을 추가할까요?","새 탭 만들기",true){Owner=this,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-4000,Top=-4000};
  bool? Run(Action<MessageDialog> action){
   var dialog=Message();Exception? failure=null;
   dialog.Loaded+=(_,_)=>dialog.Dispatcher.BeginInvoke(()=>{try{action(dialog);}catch(Exception e){failure=e;dialog.Close();}},DispatcherPriority.ApplicationIdle);
   var result=dialog.ShowDialog();if(failure!=null)throw failure;return result;
  }
  check(Run(d=>{check(d.CancelButton.IsDefault&&!d.ConfirmButton.IsDefault,"themed confirmation keeps cancel as the default action");DialogSnapshot(d,"desktop-dialog-confirm.png");d.CancelButton.RaiseEvent(new RoutedEventArgs(Wpf.Button.ClickEvent));})!=true,"cancel cannot approve a terminal operation");
  check(Run(d=>d.CaptionClose.RaiseEvent(new RoutedEventArgs(Wpf.Button.ClickEvent)))!=true,"themed title-bar close cannot approve a terminal operation");
  check(Run(d=>d.RaiseEvent(new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(d)!,0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent}))!=true,"Escape cannot approve a terminal operation");
  check(Run(d=>d.ConfirmButton.RaiseEvent(new RoutedEventArgs(Wpf.Button.ClickEvent)))==true,"explicit confirm still approves exactly once");
  var file=new SharedFile("fixture","완료된 작업물.zip",100,"",1,2);int saved=0,cleared=0;
  var box=new FileBoxWindow(new[]{file},(_,selectedFile)=>{check(selectedFile==file,"file box keeps the selected file identity");saved++;},_=>cleared++){Owner=this,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-4000,Top=-4000};
  try{box.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);DialogSnapshot(box,"desktop-dialog-files.png");
   var list=(Wpf.StackPanel)((Wpf.ScrollViewer)box.Body.Content).Content;var button=(Wpf.Button)list.Children[2];button.RaiseEvent(new RoutedEventArgs(Wpf.Button.ClickEvent));check(saved==1,"themed file box invokes its save action once without reading private files");
   var clear=(Wpf.Button)list.Children[3];clear.RaiseEvent(new RoutedEventArgs(Wpf.Button.ClickEvent));check(cleared==1,"file box clear action is available in the themed window");
  }finally{box.Close();}
 }
}
