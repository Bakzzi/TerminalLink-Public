using System.Windows;
using System.Windows.Threading;

namespace TerminalLink;
public partial class DesktopWindow {
 internal async Task CheckTrayUi(){
  var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new IOException(name);passed.Add("PASS "+name);}
  try{
   await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   var tab=selected!;var source=Messages.ItemsSource;var rows=tab.Messages.ToArray();int count=tabs.Count;
   Composer.Text="Unsent tray draft";var state=WindowState;bool taskbar=ShowInTaskbar;
   WindowState=WindowState.Minimized;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(inTray&&!IsVisible&&!ShowInTaskbar&&trayIcon?.Visible==true&&WindowState==WindowState.Minimized,"minimize hides the window and taskbar entry while exposing the tray icon");
   var icon=trayIcon;RestoreFromTray();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(!inTray&&IsVisible&&ShowInTaskbar==taskbar&&WindowState==state&&trayIcon?.Visible==false,"tray open restores the original window state and taskbar visibility");
   Check(selected==tab&&ReferenceEquals(source,Messages.ItemsSource)&&rows.SequenceEqual(tab.Messages)&&Composer.Text=="Unsent tray draft","tray restore keeps conversation controls, messages and the unsent draft");
   for(int i=0;i<3;i++){WindowState=WindowState.Minimized;RestoreFromTray();}
   RaiseEvent(new RoutedEventArgs(LoadedEvent));await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(ReferenceEquals(trayIcon,icon)&&tabs.Count==count&&selected==tab&&ReferenceEquals(source,Messages.ItemsSource)&&Composer.Text=="Unsent tray draft","repeated restore and Loaded events do not create new icons, tabs or conversation controls");
   var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);DemoSend=()=>completion.Task;var send=Send();
   WindowState=WindowState.Minimized;RestoreFromTray();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(tab.Sending&&!Composer.IsEnabled&&Composer.Text=="Unsent tray draft"&&!send.IsCompleted,"pending send survives tray restore with its editor still locked");
   completion.SetResult();await send;DemoSend=null;
   Check(!tab.Sending&&Composer.IsEnabled&&Composer.Text.Length==0,"pending send completes once after restoration");
   SetPhoneView(true);WindowState=WindowState.Minimized;RestoreFromTray();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(phoneView&&PhoneLinkPane.Visibility==Visibility.Visible&&ConversationPane.Visibility==Visibility.Hidden&&Equals(Tabs.Tag,"phone-page"),"tray restore preserves the phone page and its navigation highlight");
   WindowState=WindowState.Minimized;trayMenu!.Items[1].PerformClick();
   Check(closing&&!IsVisible&&trayIcon==null&&trayImage==null,"tray exit follows normal close and disposes the notification icon");
  }catch(Exception e){passed.Add("FAIL "+e);Environment.ExitCode=1;}
  finally{File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-tray-checks.txt"),passed);if(!closing)Close();}
 }
}
