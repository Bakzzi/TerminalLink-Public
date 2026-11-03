using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
namespace TerminalLink;
internal static class RequestChecks {
 internal static void Run(Action<bool,string> check){
  NativeFieldChecks.Run(check);
  const string thread="00000000-0000-4000-8000-000000000123",latestQuestion="A newly submitted distinctive request about the desktop popup";
  var q=new QueuedQuestion("fixture-request","Which result did you see?",new[]{"One","Two"});
  var old=new Conversation(thread,new[]{new ConversationMessage("user","","An earlier unrelated distinctive question",1)},false,new[]{q},readOnly:true);
  var fresh=old with{messages=old.messages.Append(new("user","",latestQuestion,2)).ToArray()};
  var catalog=new LocalDaemonFeed.Catalog("fixture-daemon",new[]{new LocalDaemonFeed.Candidate("owned-fixture.jsonl",old)},true);
  int selected=0,keys=0,submits=0;string draft="";
  ConsoleFrame Read(){
   var choices=q.options.Append(draft.Length>0?draft:"Type your answer").Select((text,i)=>(i==selected?"› ":"  ")+(i+1)+". "+text);
   string text="› "+latestQuestion+"\n• I need one detail before continuing.\n\n• Queued follow-up inputs\n\n"+q.title+"\n\n"+string.Join("\n",choices)+"\n\nenter submit   ctrl + ] skip   alt + ↓ main prompt";
   return new("fixture",text,100,25,0,0,Array.Empty<int>());
  }
  var tab=new DesktopTab(new(new(0,0,"fixture","WindowsTerminal"),2,2,"fixture"));
  tab.Accept(Read(),fresh with{requests=new[]{q}});
  check(tab.Request?.id==q.id,"pending original question is available on the selected tab");
  tab.Accept(Read(),fresh with{requests=Array.Empty<QueuedQuestion>()});
  check(tab.Request==null,"answered original question does not return from a stale terminal prompt");
  var other=q with{id="another",title="Another unanswered question"};
  tab.Accept(Read(),fresh with{requests=new[]{other}});
  check(tab.Request?.id==other.id,"another unanswered original question remains available");
  tab.Accept(NativeFieldChecks.Frame(),fresh with{requests=Array.Empty<QueuedQuestion>()});
  check(tab.Request is {id:""} approval&&approval.title.StartsWith("Field "),"an active native approval still appears when there are no queued questions");
  var nativeQuestion=Read() with{text="Question 1/1\nA blocking question?\n\n› 1. Yes\n  2. No\n\nenter to submit answer"};
  check(NotificationInput.Parse(nativeQuestion.text) is {questionKind:true,followup:false},"fixture models a native blocking question");
  tab.Accept(nativeQuestion,fresh with{requests=Array.Empty<QueuedQuestion>()});
  check(tab.Request is {id:"",title:"A blocking question?"},"native blocking question remains available even when async queue is empty");
  bool Matches(LocalDaemonFeed.Catalog c)=>c.candidates.Count(x=>LocalDaemonFeed.Compatible(Read(),x.conversation))==1&&c.candidates.Single(x=>LocalDaemonFeed.Compatible(Read(),x.conversation)).conversation.id==thread;
  check(!Matches(catalog)&&LocalDaemonFeed.Compatible(Read(),fresh),"reproduces first-submit rejection when displayed history is newer than the input catalog");
  int verifications=0;var verified=LocalDaemonFeed.DesktopInputCatalog(catalog,thread,c=>{verifications++;return c with{conversation=fresh};});
  check(verified!=null&&Matches(verified)&&verifications==1&&catalog.candidates[0].conversation==old,"first submit refreshes the owned original before matching without altering discovery snapshot");
  check(LocalDaemonFeed.DesktopInputCatalog(catalog,thread,_=>null)==null&&LocalDaemonFeed.DesktopInputCatalog(catalog,thread,c=>c with{conversation=fresh with{id="wrong-thread"}})==null,"failed ownership or changed thread still rejects request submission");
  var ambiguous=verified! with{candidates=verified!.candidates.Append(new("other.jsonl",fresh with{id="other-thread"})).ToArray()};
  check(!Matches(ambiguous),"freshening one record does not weaken ambiguity rejection");
  JsonElement Command(int choice,string text)=>JsonSerializer.SerializeToElement(new{request="fixture-submit",kind="request",thread,requestKey=q.id,question=q.title,choices=q.options.Append("직접 입력"),followup=true,questionKind=true,choice,text});
  object Execute(int choice,string text)=>NotificationInput.ExecuteCore(Read,()=>fresh,s=>draft=s,(key,alt)=>{keys++;if(key=="DOWN")selected++;else if(key=="UP")selected--;else if(key=="ENTER")submits++;},Command(choice,text));
  var result=JsonSerializer.SerializeToElement(Execute(-1,"A typed answer"));
  check(result.Bool("ok")&&submits==1&&selected==2&&draft=="A typed answer","first free-text submit selects the native input row and sends exactly one Enter");
  selected=0;keys=submits=0;draft="";result=JsonSerializer.SerializeToElement(Execute(1,"ignored draft"));
  check(result.Bool("ok")&&submits==1&&selected==1&&draft.Length==0,"choice reply sends its selected option with no free-text leakage");
  selected=2;keys=submits=0;draft="Existing native draft";result=JsonSerializer.SerializeToElement(Execute(-1,"Replacement"));
  check(!result.Bool("ok")&&keys==0&&draft=="Existing native draft","existing native request draft is protected from overwrite");
 }
}
public partial class DesktopWindow {
 async Task CheckRequestUi(Action<bool,string> check){
  var q=new QueuedQuestion("popup-fixture","A request with selectable and free-text answers",new[]{"One","Two"});
  var tab=new DesktopTab(new(new(0,0,"fixture","WindowsTerminal"),3,1,"fixture"));
  var frame=new ConsoleFrame("fixture","",80,25,0,0,Array.Empty<int>());var conversation=new Conversation("popup-thread",Array.Empty<ConversationMessage>(),false,new[]{q});tab.Accept(frame,conversation);
  int submits=0,choiceSent=99;string textSent="";var response=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  Task Answer(int choice,string text){submits++;choiceSent=choice;textSent=text;return response.Task;}
  RequestWindow Open(QueuedQuestion question,bool free=true){var w=new RequestWindow(tab,question,Answer,free){Owner=this,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-4000,Top=-4000};w.Show();return w;}
  var popup=Open(q);
  try{
   await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);popup.UpdateLayout();
   popup.QuestionText.Select(2,7);
   check(popup.QuestionText.IsReadOnly&&popup.QuestionText.SelectedText==q.title.Substring(2,7),"request question is selectable and cannot be edited");
   var optionText=(System.Windows.Controls.TextBox)popup.Choices[1].Content;optionText.SelectAll();
   check(optionText.IsReadOnly&&optionText.SelectedText=="Two","request choices allow selecting their text without editing");
   optionText.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
   check(popup.Choices[1].IsChecked==true,"clicking selectable choice text still selects the answer");
   popup.Choices[0].IsChecked=true;
   check(popup.Draft.IsEnabled,"request free-text field remains clickable while a numbered option is selected");
   popup.Draft.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent});
   check(popup.Choices[^1].IsChecked==true,"clicking the request editor automatically selects direct input");
   popup.Choices[0].IsChecked=true;popup.Draft.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,null,popup.Draft){RoutedEvent=Keyboard.GotKeyboardFocusEvent});
   check(popup.Choices[^1].IsChecked==true,"keyboard focus also selects request direct input");
   popup.Choices[0].IsChecked=true;popup.Draft.Text="A pasted or typed answer";
   tab.Accept(frame,conversation with{requests=new[]{q with{id="another",title="A newer question"},q}});popup.Refresh(tab);
   check(popup.SendButton.IsEnabled&&popup.Draft.Text=="A pasted or typed answer","a newer first question cannot replace or invalidate the pinned request");
   tab.Accept(frame,conversation with{requests=new[]{q with{id="another",title="A newer question"}}});popup.Refresh(tab);
   check(!popup.SendButton.IsEnabled&&popup.Draft.Text=="A pasted or typed answer"&&popup.IsVisible,"external completion preserves the old popup and draft instead of showing another question");
   tab.Accept(frame,conversation);popup.Refresh(tab);
   check(popup.Choices[^1].IsChecked==true,"typing or pasting selects direct input without a separate radio click");
   DialogSnapshot(popup,"desktop-dialog-request.png");
   double height=popup.ActualHeight;var pending=popup.Submit();await popup.Submit();bool responsive=false;
   await Dispatcher.InvokeAsync(()=>responsive=true);
   check(responsive&&submits==1&&choiceSent==-1&&textSent=="A pasted or typed answer"&&popup.Sending&&!popup.Draft.IsEnabled&&!popup.SendButton.IsEnabled&&popup.Choices.All(c=>!c.IsEnabled),"request send stays responsive, locks editing and rejects duplicate clicks");
   response.SetException(new IOException("Synthetic first-send failure"));await pending;popup.UpdateLayout();
   check(!popup.Sending&&popup.Draft.IsEnabled&&popup.SendButton.IsEnabled&&popup.Draft.Text==textSent&&popup.Choices[^1].IsChecked==true&&popup.Status.Text.Contains("Synthetic")&&Math.Abs(height-popup.ActualHeight)<1,"failed request unlocks the same selected draft without resizing the popup");
   tab.Accept(frame,new("unavailable",Array.Empty<ConversationMessage>(),false,source:"unavailable",readState:"failed"));popup.Refresh(tab);
   check(popup.IsVisible&&!popup.SendButton.IsEnabled&&popup.Draft.IsEnabled&&popup.Draft.Text==textSent,"temporary read loss preserves editable request draft and blocks only submission");
   tab.Accept(frame,conversation);popup.Refresh(tab);popup.Close();popup=Open(q);
   check(popup.Draft.Text==textSent&&popup.Choices[^1].IsChecked==true,"closing and reopening a failed request restores its selection and draft");
   response=new(TaskCreationOptions.RunContinuationsAsynchronously);pending=popup.Submit();response.SetException(new RequestPendingException("전송 결과 확인 중"));await pending;
   check(popup.SendButton.IsEnabled&&!popup.Draft.IsEnabled&&popup.SendButton.Content.ToString()=="전송 결과 확인"&&submits==2,"unknown delivery preserves immutable draft and offers only status lookup");
   response=new(TaskCreationOptions.RunContinuationsAsynchronously);pending=popup.Submit();response.SetResult();await pending;
   check(!popup.IsVisible&&tab.RequestDrafts.Count==0&&submits==3,"successful request closes once and clears only its draft");
   var native=q with{id="",options=new[]{"One","Two","직접 입력"}};tab.Accept(frame,conversation with{requests=new[]{native}});popup=Open(native);
   check(popup.Choices.Count==3,"native follow-up displays direct input exactly once");popup.Close();
   tab.Accept(frame,conversation);popup=Open(q,false);check(popup.Draft.Visibility==Visibility.Collapsed&&popup.Choices.Count==2,"approval-only requests cannot switch to free-text input");popup.Close();
   tab.Accept(NativeFieldChecks.Frame(),conversation);
   var approval=tab.Request!;
   check(approval.id.Length==0&&approval.title.Contains(NativeFieldChecks.Question)&&approval.options.Length==4,"blocking native approval takes precedence over an older queued question");
   popup=Open(approval,false);
   check(popup.IsVisible&&popup.Draft.Visibility==Visibility.Collapsed&&popup.Choices.Count==4,"native field permission displays all four choices without a free-text row");
   check(popup.Choices.All(c=>c.IsChecked!=true),"permission popup requires explicit choice rather than preselecting native Allow");
   int before=submits;await popup.Submit();check(submits==before&&popup.Status.Text.Contains("선택"),"opening and submitting without a choice never grants permission");
   popup.Choices[1].IsChecked=true;
   tab.Accept(NativeFieldChecks.Frame(NativeFieldChecks.Text(1,unanswered:1)),conversation);popup.Refresh(tab);
   check(popup.SendButton.IsEnabled&&popup.Choices[1].IsChecked==true&&!popup.Status.Text.Contains("변경"),"open popup keeps its selection and send button when native unanswered counter appears");
   tab.Accept(NativeFieldChecks.Frame(NativeFieldChecks.Text(1,unanswered:1).Replace("example.invalid","changed.invalid")),conversation);popup.Refresh(tab);
   check(!popup.SendButton.IsEnabled&&popup.Choices[1].IsChecked==true,"changed permission origin disables sending without discarding selected draft");
   tab.Accept(NativeFieldChecks.Frame(NativeFieldChecks.Text(1,unanswered:1)),conversation);popup.Refresh(tab);
   response=new(TaskCreationOptions.RunContinuationsAsynchronously);pending=popup.Submit();response.SetResult();await pending;
   check(submits==before+1&&choiceSent==1&&textSent.Length==0&&!popup.IsVisible,"same permission popup submits selected scope on the first click after counter update");
   popup.Close();var oldSelected=selected;
   try{
    selected=tab;RenderState();check(RequestButton.Visibility==Visibility.Visible,"main request button appears for native field approval");
    string stale="• Queued follow-up inputs\n\n"+q.title+"\n\n› 1. One\n  2. Two\n  3. Type your answer\n\nenter submit   ctrl + ] skip   alt + ↓ main prompt";
    var oldFrame=frame with{text=stale};tab.Accept(oldFrame,conversation);RenderState();
    check(RequestButton.Visibility==Visibility.Visible,"main request button appears while the original question is pending");
    tab.Accept(oldFrame,conversation with{requests=Array.Empty<QueuedQuestion>()});RenderState();
    check(RequestButton.Visibility==Visibility.Collapsed,"main request button clears immediately after original completion even if the terminal frame is stale");
    tab.Accept(oldFrame,conversation with{requests=new[]{q with{id="other",title="Another unanswered question"}}});RenderState();
    check(RequestButton.Visibility==Visibility.Visible,"main request button remains for a different unanswered question");
   }
   finally{selected=oldSelected;RenderState();}
  }finally{popup.Close();}
 }
}
