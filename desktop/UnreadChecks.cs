using System.Windows;
using Media=System.Windows.Media;
namespace TerminalLink;
internal static class UnreadChecks {
 internal static void Run(Action<bool,string> check){
  string path=Path.Combine(AppContext.BaseDirectory,"read-check-"+Guid.NewGuid().ToString("N")+".json");
  try{
   var store=new ReadReceipts(path);var tracker=new UnreadReplies(store);
   Conversation Chat(params ConversationMessage[] messages)=>new("unread-fixture",messages,false);
   var progress=Chat(new("user","","Question",1),new("assistant","commentary","Working",2));tracker.Observe(progress);check(!tracker.HasUnread,"progress alone never marks a tab unread");
   var answer=progress with{messages=progress.messages.Append(new("assistant","final","Answer",3)).ToArray()};tracker.Observe(answer);check(tracker.HasUnread,"a final reply marks its conversation unread");
   tracker.MarkDisplayed(answer with{id="another-thread"});check(tracker.HasUnread,"viewing a different conversation cannot clear unread replies");
   tracker.MarkDisplayed(answer);tracker.Observe(answer with{});check(!tracker.HasUnread,"replayed final reply stays read");
   var sharedReceipts=new ReadReceipts(null);var target=new ConsoleTarget(new(0,0,"unread","WindowsTerminal"),1,1,"unread");var one=new DesktopTab(target,sharedReceipts);var two=new DesktopTab(target with{Pid=2},sharedReceipts);var frame=new ConsoleFrame("unread","",80,25,0,0,Array.Empty<int>());
   one.Accept(frame,answer);two.Accept(frame,answer);int changed=0;two.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(DesktopTab.HasUnread))changed++;};one.MarkReplyRead();two.ObserveReplies(answer);
   check(!one.HasUnread&&!two.HasUnread&&changed==1,"the same conversation in two tabs clears both indicators");
   var later=answer with{messages=answer.messages.Append(new("assistant","final_answer","Later answer",5)).ToArray()};tracker.Observe(later);tracker.MarkDisplayed(answer);check(tracker.HasUnread,"viewing an older snapshot cannot clear a newer reply");
   tracker.Observe(new("unavailable",Array.Empty<ConversationMessage>(),false,source:"unavailable",readState:"failed"));check(tracker.HasUnread,"temporary read errors preserve unread state");
   var restored=new UnreadReplies(new(path));restored.Observe(later);check(restored.HasUnread,"unread watermark survives reopening the settings");
   restored.MarkDisplayed(later);var restarted=new UnreadReplies(new(path));restarted.Observe(later);check(!restarted.HasUnread,"read replies remain read after app restart");
   restarted.Observe(later with{id="new-thread"});check(restarted.HasUnread,"a different thread has independent read state");
   check(!File.ReadAllText(path).Contains("unread-fixture")&&!File.ReadAllText(path).Contains("Later answer"),"read settings do not contain conversation IDs or message content");
   check(UnreadReplies.Visible(true,false,false,false,true,true)&&!UnreadReplies.Visible(false,false,false,false,true,true)&&!UnreadReplies.Visible(true,true,false,false,true,true)&&!UnreadReplies.Visible(true,false,true,false,true,true)&&!UnreadReplies.Visible(true,false,false,true,true,true)&&!UnreadReplies.Visible(true,false,false,false,true,false)&&!UnreadReplies.Visible(true,false,false,false,false,true),"only the active conversation at the latest reply is acknowledged");
  }finally{if(File.Exists(path))File.Delete(path);}
 }
}
public partial class DesktopWindow {
 async Task CheckUnreadUi(DesktopTab first,DesktopTab second,Action<bool,string> check){
  Tabs.SelectedItem=first;var firstRows=Messages.ItemsSource;string draft=Composer.Text;
  second.MarkReplyRead();var reply=second.Original! with{messages=second.Original!.messages.Append(new("assistant","final","Background reply",100)).ToArray()};
  var remaining=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int polls=0;Action<BackgroundObservation>? publish=null;
  DemoBackgroundRead=(other,deliver,_)=>{check(other.Contains(second)&&!other.Contains(first),"background monitor receives inactive tabs only");polls++;publish=deliver;return remaining.Task;};
  var waiting=CheckBackground();await CheckBackground();
  var clock=System.Diagnostics.Stopwatch.StartNew();await Task.Run(()=>publish!(new(second,reply)));
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
  check(second.HasUnread&&!waiting.IsCompleted,$"badge published before slower tabs finish ({clock.ElapsedMilliseconds} ms)");
  remaining.SetResult();await waiting;
  check(polls==1&&second.HasUnread&&selected==first&&Messages.ItemsSource==firstRows&&Composer.Text==draft,"inactive reply updates its badge without selecting or rebuilding the active conversation");
  await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();
  var row=(System.Windows.Controls.ListBoxItem)Tabs.ItemContainerGenerator.ContainerFromItem(second);
  FrameworkElement? FindDot(DependencyObject node){if(node is FrameworkElement f&&f.Name=="UnreadDot")return f;for(int i=0;i<Media.VisualTreeHelper.GetChildrenCount(node);i++){var found=FindDot(Media.VisualTreeHelper.GetChild(node,i));if(found!=null)return found;}return null;}
  var dot=FindDot(row);check(dot?.Visibility==Visibility.Visible,"unread reply renders a blue dot on its tab");
  DemoBackgroundRead=(_,_,_)=>throw new IOException("Synthetic monitor interruption");await CheckBackground();check(second.HasUnread,"monitor interruptions do not clear a pending dot");
  second.Accept(second.Frame!,reply);second.MarkReplyRead();await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
  check(!second.HasUnread&&dot?.Visibility==Visibility.Collapsed,"acknowledging the displayed reply removes the dot");
  // Exercise the production timer while the selected conversation's refresh is blocked.
  var detected=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  DemoBackgroundRead=(_,deliver,_)=>{deliver(new(second,reply with{messages=reply.messages.Append(new("assistant","final","Next background reply",101)).ToArray()}));detected.TrySetResult();return Task.CompletedTask;};
  ticking=true;clock.Restart();unreadTimer.Start();
  try{await detected.Task.WaitAsync(TimeSpan.FromSeconds(2));await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);UpdateLayout();check(second.HasUnread&&dot?.Visibility==Visibility.Visible&&clock.ElapsedMilliseconds<1750,$"independent timer displays unread reply with selected refresh blocked ({clock.ElapsedMilliseconds} ms)");}
  finally{unreadTimer.Stop();ticking=false;DemoBackgroundRead=null;}
 }
}
