using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace TerminalLink;
public partial class DesktopWindow {
 sealed record UpdateView(string selected,bool terminal,bool phone,bool tray,bool maximized,double left,double top,double width,double height);
 UpdateView? updateRestore;string updateSelection="";
 readonly DispatcherTimer updatesTimer=new(){Interval=TimeSpan.FromSeconds(15)};
 bool UpdateIdle()=>!closing&&!IsActive&&IsEnabled&&!busy&&!addingTab&&!reorderingTabs&&!phoneAttaching&&!phoneClosing&&OwnedWindows.Count==0&&tabs.All(t=>!t.Sending&&t.Draft.Length==0&&t.Attachments.Count==0&&t.CanClose);
 string SaveUpdateView(){var rect=WindowState==WindowState.Normal?new Rect(Left,Top,Width,Height):RestoreBounds;return JsonSerializer.Serialize(new UpdateView(selected?.Id??"",terminalView,phoneView,inTray,WindowState==WindowState.Maximized||trayRestoreState==WindowState.Maximized,rect.Left,rect.Top,rect.Width,rect.Height));}
 void InitializeUpdates(){
  if(demo||liveCheck||!AutomaticUpdates.Installed)return;
  try{string? json=AutomaticUpdates.Resume();if(json!=null)updateRestore=JsonSerializer.Deserialize<UpdateView>(json);}catch{}
  if(updateRestore is {} saved){updateSelection=saved.selected;ShowActivated=false;if(saved.width>=MinWidth&&saved.height>=MinHeight&&double.IsFinite(saved.left)&&double.IsFinite(saved.top)){WindowStartupLocation=WindowStartupLocation.Manual;Left=saved.left;Top=saved.top;Width=saved.width;Height=saved.height;}}
  updatesTimer.Tick+=async(_,_)=>await AutomaticUpdates.Check(UpdateIdle,SaveUpdateView,Close,ShowNotice);
  ContentRendered+=(_,_)=>{
   if(updateRestore is {} s){SetView(s.terminal);if(s.phone)SetPhoneView(true);if(s.maximized)WindowState=WindowState.Maximized;if(s.tray)WindowState=WindowState.Minimized;updateRestore=null;}
   AutomaticUpdates.Healthy();updatesTimer.Start();
  };
  Closed+=(_,_)=>updatesTimer.Stop();
 }
}
