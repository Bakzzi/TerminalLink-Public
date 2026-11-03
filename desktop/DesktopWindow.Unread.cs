using System.Windows;
using System.Windows.Threading;

namespace TerminalLink;
public partial class DesktopWindow {
 readonly ReadReceipts receipts;
 bool backgroundTicking;
 readonly DispatcherTimer unreadTimer=new(){Interval=TimeSpan.FromSeconds(1)};
 Task backgroundWork=Task.CompletedTask;
 internal Func<DesktopTab[],Action<BackgroundObservation>,CancellationToken,Task>? DemoBackgroundRead;
 void InitializeUnread(){
  Activated+=(_,_)=>Dispatcher.BeginInvoke(AcknowledgeVisibleReply,DispatcherPriority.ContextIdle);
  unreadTimer.Tick+=(_,_)=>{if(!backgroundTicking&&!closing)backgroundWork=CheckBackground();};
 }
 void PublishBackground(BackgroundObservation result){
  void Apply(){if(!closing&&result.Tab!=selected&&tabs.Contains(result.Tab))result.Tab.ObserveReplies(result.Conversation);}
  if(Dispatcher.CheckAccess())Apply();else Dispatcher.BeginInvoke(Apply,DispatcherPriority.Normal);
 }
 async Task CheckBackground(){
  if(backgroundTicking||closing)return;backgroundTicking=true;
  try{
   var other=tabs.Where(t=>t!=selected&&t.CanClose).ToArray();if(other.Length==0)return;
   if(demo){if(DemoBackgroundRead!=null)await DemoBackgroundRead(other,PublishBackground,life.Token);}
   else await session.ReadBackground(other,PublishBackground,life.Token);
  }catch(OperationCanceledException){}catch(Exception){/* A transient miss must not clear an unread answer. */}
  finally{backgroundTicking=false;}
 }
 void AcknowledgeVisibleReply(){
  var tab=selected;if(tab==null||closing||phoneView)return;scroll??=FindScroll(Messages);
  bool bottom=scroll!=null&&scroll.ScrollableHeight-scroll.VerticalOffset<4;
  if(!UnreadReplies.Visible(IsActive,WindowState==WindowState.Minimized,terminalView,requestWindow!=null,tab.Ready,bottom))return;
  try{tab.MarkReplyRead();foreach(var other in tabs)other.RefreshUnread();}catch(Exception e)when(e is IOException or UnauthorizedAccessException){Error("읽음 상태를 저장하지 못했습니다. 앱을 다시 열면 파란 점이 다시 표시될 수 있어요.");}
 }
}
