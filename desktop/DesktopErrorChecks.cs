using System.Windows;
using System.Windows.Threading;
using Size=System.Windows.Size;
using Point=System.Windows.Point;

namespace TerminalLink;
public partial class DesktopWindow {
 internal async Task CheckErrorUi(){
  var passed=new List<string>();
  void Check(bool ok,string name){if(!ok)throw new Exception(name);passed.Add("PASS "+name);}
  try{
   if(!demo)throw new InvalidOperationException("Synthetic error check only");
   string raw="Command 'python3 -c '"+RemoteCodexFeed.Script+"' '{\"path\":\"/private-fixture/record.jsonl\"}' has timed out.";
   Check(DesktopErrorText.Display(raw)==DesktopErrorText.RemoteTimeout,"SSH command timeout has actionable text without source or private arguments");
   Check(!DesktopErrorText.Display("Remote failure: "+raw).Contains("private-fixture"),"wrapped worker errors cannot expose command arguments");
   Check(DesktopErrorText.Display(new string('x',10000))==DesktopErrorText.Unexpected&&DesktopErrorText.Display(string.Join('\n',Enumerable.Repeat("short",100)))==DesktopErrorText.Unexpected,"oversized and multiline diagnostics are not presented as UI copy");
   Check(DesktopErrorText.Display("PC에 작성 중인 내용이 있습니다. 앱에서 확인하세요.")=="PC에 작성 중인 내용이 있습니다. 앱에서 확인하세요.","normal actionable validation messages are preserved");
   Check(!DesktopErrorText.Display("사진을 보낼 대화가 바뀌었거나 아직 연결되지 않았습니다.").Contains("사진"),"ordinary message failures do not incorrectly describe a photo upload");
   var first=selected!;
   first.Accept(first.Frame!,first.Original! with{messages=first.Original!.messages.Concat(Enumerable.Range(3,40).Select(i=>new ConversationMessage(i%2==0?"assistant":"user",i%2==0?"final":"","Synthetic scrolling message "+i,i))).ToArray()});
   RenderState();Bottom();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   var original=first.Original;var rows=first.Messages.ToArray();
   int sends=0;DemoSend=()=>{sends++;return Task.FromException(new IOException(raw));};Composer.Text="Keep my unsent draft";
   await Send();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(sends==1&&Composer.IsEnabled&&Composer.Text=="Keep my unsent draft"&&first.SendError==DesktopErrorText.RemoteTimeout,"worker send failure unlocks the original draft without retransmitting");
   Check(first.Original==original&&first.Messages.SequenceEqual(rows)&&!Notice.Text.Contains("python3")&&!Notice.Text.Contains("private-fixture"),"send error preserves verified message objects and does not leak diagnostics");
   Check(first.FollowBottom&&scroll!=null&&scroll.ScrollableHeight-scroll.VerticalOffset<1,"a failed send keeps the newest message visible when its error banner appears");
   first.SendError="";RenderState();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(first.FollowBottom&&scroll!.ScrollableHeight-scroll.VerticalOffset<1,"clearing an error preserves the latest position");
   scroll!.ScrollToVerticalOffset(200);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);double reading=scroll.VerticalOffset;
   Check(reading>0&&!first.FollowBottom,"older-message fixture has a real independent reading position");
   await Send();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(!first.FollowBottom&&Math.Abs(scroll.VerticalOffset-reading)<1&&Composer.Text=="Keep my unsent draft","a failed send preserves an older reading position and the editable draft");
   first.SendError="";RenderState();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(!first.FollowBottom&&Math.Abs(scroll.VerticalOffset-reading)<1,"clearing the error does not move an older reading position");
   first.SendError=DesktopErrorText.RemoteTimeout;RenderState();
   foreach(var size in new[]{new Size(1240,840),new Size(840,570),new Size(1920,1040)}){
    Width=size.Width;Height=size.Height;RenderState();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();
    var input=Composer.TranslatePoint(new Point(0,0),ConversationPane);
    Check(NoticeBar.ActualHeight<=100&&Messages.ActualHeight>80&&input.Y>=0&&input.Y+Composer.ActualHeight<ConversationPane.ActualHeight&&RetryButton.IsVisible&&RetryButton.ActualHeight<60,$"error banner leaves chat, retry and composer usable at {size.Width}x{size.Height}");
   }
   Width=1240;Height=840;UpdateLayout();DialogSnapshot(this,"desktop-error-preview.png");
   // A future caller bypassing the text adapter must still not break the layout.
   Notice.Text=raw;UpdateLayout();Check(NoticeBar.ActualHeight<=100&&Messages.ActualHeight>80,"layout independently contains an accidental unfiltered diagnostic");
   first.SendError="";Error(raw);RenderState();Check(Notice.Text==DesktopErrorText.RemoteTimeout,"general action failures use the same safe display boundary");
   actionError="";first.Error=raw;RenderState();Check(!Notice.Text.Contains("python3")&&Notice.Text.Contains("원격"),"refresh failures are sanitized without clearing existing history");first.Error="";
   var second=new DesktopTab(first.Target with{Pid=2,Started=2,Name="Another synthetic tab"});
   var q=new QueuedQuestion("request-fixture","A synthetic question",new[]{"One","Two"});
   second.Accept(first.Frame!,new("other-thread",new[]{new ConversationMessage("user","","Other tab",1)},false,new[]{q}));tabs.Add(second);Tabs.SelectedItem=second;RenderState();
   Check(NoticeBar.Visibility==Visibility.Collapsed&&first.Draft=="Keep my unsent draft","switching tabs does not carry another tab error or change its draft");
   using var cleanup=new PopupCleanup();int replies=0;
   var popup=new RequestWindow(second,q,(_,_)=>{replies++;return Task.FromException(new IOException(raw));}){Owner=this,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-4000,Top=-4000};cleanup.Window=popup;
   popup.Show();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);popup.Draft.Text="Keep my request answer";await popup.Submit();popup.UpdateLayout();
   Check(replies==1&&popup.Draft.IsEnabled&&popup.SendButton.IsEnabled&&popup.Draft.Text=="Keep my request answer"&&popup.Status.Text==DesktopErrorText.RemoteTimeout,"request reply errors preserve the answer and use safe status text");
   Check(popup.Status.ActualHeight<100,"request error does not become a command-sized content block");
   PhoneStatus(raw);Check(PhoneLinkMessage.Text==DesktopErrorText.RemoteTimeout,"phone-link errors use the same display boundary");
  }catch(Exception e){passed.Add("FAIL "+e);Environment.ExitCode=1;}
  finally{File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-error-checks.txt"),passed);Close();}
 }
 sealed class PopupCleanup : IDisposable {internal Window? Window;public void Dispose()=>Window?.Close();}
}
