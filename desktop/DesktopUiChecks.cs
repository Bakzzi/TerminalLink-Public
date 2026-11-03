using System.Windows;
using System.Windows.Threading;
using Media=System.Windows.Media;

namespace TerminalLink;
public partial class DesktopWindow {
 internal async Task CheckSending(){
  var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);passed.Add("PASS "+name);}
  try{
   if(!demo)throw new InvalidOperationException("Synthetic UI only");
   var first=selected!;Composer.Text="First pending message";
   var completion=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);DemoSend=()=>completion.Task;
   var pending=Send();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(!pending.IsCompleted&&Tabs.IsEnabled&&!Composer.IsEnabled&&!AttachButton.IsEnabled&&!RemoveAttachmentButton.IsEnabled&&!SendButton.IsEnabled&&(string)SendButton.Content=="전송 중…","pending send locks its composer and attachment add/remove while sidebar stays enabled");
   var second=new DesktopTab(new(new(0,0,"demo-two","WindowsTerminal"),2,2,"Second synthetic tab"));
   second.Accept(new("demo-two","",80,25,0,0,Array.Empty<int>()),new("demo-two",new[]{new ConversationMessage("user","","Second tab",1)},false));tabs.Add(second);Tabs.SelectedItem=second;
   Composer.Text="Untouched second draft";await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();second.Accept(second.Frame!,second.Original! with{weeklyUsage=new(42,now+3600,now)});RenderState();
   Check(WeeklyUsage.Text=="42%","selected tab displays its own weekly used percentage");
   second.Accept(second.Frame!,second.Original! with{weeklyUsage=new(42,now-1,now-3600)});RenderState();
   Check(WeeklyUsage.Text=="--%","expired quota is not displayed as current");
   Check(selected==second&&Messages.ItemsSource==second.Messages&&!pending.IsCompleted,"tab changes complete before the first send acknowledgement");
   Check(Composer.IsEnabled,"another tab draft remains editable during the first tab send");
   second.Accept(second.Frame!,second.Original! with{messages=second.Original!.messages.Append(new("assistant","commentary","Progress before final reply",2)).ToArray()});RenderState();UpdateLayout();
   Check(Messages.Items.Count==2&&((ChatRow)Messages.Items[1]).Speaker=="Codex · 진행중","progress is visible while sending, before any final reply arrives");
   var progressRow=Messages.Items[1];second.Accept(second.Frame!,second.Original! with{messages=second.Original!.messages.Append(new("assistant","final","Fresh reply",3)).ToArray()});RenderState();UpdateLayout();
   Check(Messages.Items.Count==3&&ReferenceEquals(progressRow,Messages.Items[1])&&((ChatRow)Messages.Items[2]).Speaker=="Codex","final reply follows the existing progress row without rebuilding it");
   second.ScrollOffset=88;second.FollowBottom=false;var rows=second.Messages.ToArray();
   MoveTab(first,tabs.Count);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   Check(tabs[1]==first&&Tabs.SelectedItem==second&&selected==second&&Composer.Text=="Untouched second draft"&&second.ScrollOffset==88&&!second.FollowBottom&&second.Messages.SequenceEqual(rows)&&!pending.IsCompleted,"drag reorder during send preserves selection, draft, scroll and message objects");
   MoveTab(first,0);ApplyTabOrder();UpdateLayout();
   var firstRow=(System.Windows.Controls.ListBoxItem)Tabs.ItemContainerGenerator.ContainerFromItem(first);
   var rowTop=firstRow.TranslatePoint(new System.Windows.Point(20,0),Tabs);
   Check(TabDropAt(new(rowTop.X,rowTop.Y+2)).index==0&&TabDropAt(new(rowTop.X,rowTop.Y+firstRow.ActualHeight-2)).index==1,"drop position distinguishes insertion above and below a tab");
   var bitmap=new Media.Imaging.RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,Media.PixelFormats.Pbgra32);bitmap.Render(this);
   var point=Tabs.TransformToAncestor(this).Transform(new System.Windows.Point(8,Math.Max(1,Tabs.ActualHeight-12)));byte[] pixel=new byte[4];bitmap.CopyPixels(new Int32Rect((int)point.X,(int)point.Y,1,1),pixel,4,0);
   Check(pixel[0]<70&&pixel[1]<70&&pixel[2]<70,"sidebar stays dark during a delayed send");
   completion.SetResult();await pending;
   Check(selected==second&&Composer.Text=="Untouched second draft"&&second.Draft==Composer.Text&&first.Draft=="","successful old send preserves selected tab and its draft");
   Check(first.Messages.Any(m=>m.Text=="First pending message"),"successful send is visible before the next original record refresh");
   var written=first.Messages.Single(m=>m.Text=="First pending message");
   Check(written.PendingReceipt&&written.Speaker=="나 · 접수 확인 중","transport completion shows a pending receipt rather than a false completed message");
   var card=new MessageCard{Message=written};var header=(System.Windows.Controls.TextBlock)((System.Windows.Controls.StackPanel)card.Child).Children[0];
   written.RefreshDelivery(Environment.TickCount64+8001);await Dispatcher.Yield(DispatcherPriority.DataBind);
   Check(header.Text=="나 · 접수 미확인"&&written.Text=="First pending message","rendered header changes on missing receipt without rebuilding or erasing message text");
   Tabs.SelectedItem=first;Composer.Text="Retry this message";
   completion=new(TaskCreationOptions.RunContinuationsAsynchronously);DemoSend=()=>completion.Task;pending=Send();Tabs.SelectedItem=second;
   completion.SetException(new IOException("Synthetic send failure"));await pending;
   Check(first.Draft=="Retry this message"&&first.SendError=="Synthetic send failure"&&second.SendError==""&&Composer.Text=="Untouched second draft","failed send stays with its original tab and preserves both drafts");
   Tabs.SelectedItem=first;Check(Composer.IsEnabled&&Composer.Text=="Retry this message","failure unlocks the retained draft for editing");completion=new(TaskCreationOptions.RunContinuationsAsynchronously);DemoSend=()=>completion.Task;pending=Send();Check(!Composer.IsEnabled,"retry locks editing again");completion.SetResult();await pending;
   Check(Composer.IsEnabled&&Composer.Text==""&&first.Messages.Any(m=>m.Text=="Retry this message"),"success immediately displays the message and enables a fresh draft");
   first.Ready=false;first.MessageIdentity="local:synthetic-native";first.Error="Synthetic original history unavailable";RenderState();
   Check(SendButton.IsEnabled&&Composer.IsEnabled,"history read failure leaves the selected native terminal composer usable");
   Composer.Text="Send while history is unavailable";DemoSend=()=>Task.CompletedTask;await Send();
   Check(first.Messages.Any(m=>m.Text=="Send while history is unavailable")&&Composer.Text==""&&first.Error.Length>0,"successful ordinary send appears while the independent history error remains visible");
   first.Ready=true;first.MessageIdentity="";first.Error="";Composer.Text="Next draft while sending";
   int opened=0;DemoConfirmAdd=_=>false;DemoOpenTab=_=>{opened++;return Task.FromResult(first.Target);};
   AddTabButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
   Check(opened==0&&tabs.Count==2&&AddTabButton.IsEnabled,"new tab button requires confirmation and cancellation creates nothing");
   var creation=new TaskCompletionSource<ConsoleTarget>(TaskCreationOptions.RunContinuationsAsynchronously);ConsoleTarget? captured=null;
   DemoConfirmAdd=_=>true;DemoOpenTab=t=>{captured=t;opened++;return creation.Task;};var addTask=AddTab();Tabs.SelectedItem=second;await AddTab();
   Check(opened==1&&!AddTabButton.IsEnabled&&captured==first.Target,"new tab fixes the target before awaiting and rejects duplicate creation");
   var newTarget=new ConsoleTarget(first.Target.OriginalWindow,3,3,"Third synthetic tab");creation.SetResult(newTarget);await addTask;
   Check(tabs.Count==3&&selected?.Target==newTarget&&AddTabButton.IsEnabled&&first.Draft=="Next draft while sending"&&second.Draft=="Untouched second draft","new tab is selected after creation without changing existing drafts");
   Tabs.SelectedItem=first;
   // Exercise the caption commands on this synthetic window only. Keep it invisible
   // while maximizing so validation does not cover the user's working screen.
   Opacity=0;ShowInTaskbar=false;
   async Task ClickCaption(System.Windows.Controls.Button button){button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));await Task.Delay(120);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();}
   await ClickCaption(MaximizeButton);
   Check(WindowState==WindowState.Maximized,"caption maximize command changes the window state");
   var work=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;
   var topLeft=WindowFrame.PointToScreen(new System.Windows.Point(0,0));var bottomRight=WindowFrame.PointToScreen(new System.Windows.Point(WindowFrame.ActualWidth,WindowFrame.ActualHeight));
   Check(topLeft.X>=work.Left-1&&topLeft.Y>=work.Top-1&&bottomRight.X<=work.Right+1&&bottomRight.Y<=work.Bottom+1,$"maximized content and caption stay inside the monitor work area: {topLeft} to {bottomRight}; {work}");
   await ClickCaption(MaximizeButton);await ClickCaption(MinimizeButton);
   Check(WindowState==WindowState.Minimized,"caption minimize command changes the window state");
   RestoreFromTray();await Task.Delay(120);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   for(int i=0;i<20&&WindowState==WindowState.Minimized;i++)await Task.Delay(50);
   Check(WindowState!=WindowState.Minimized&&selected==first&&Composer.Text=="Next draft while sending",$"caption restore preserves the selected conversation and draft ({WindowState})");
   WindowState=WindowState.Normal;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
   await CheckUnreadUi(first,second,Check);
   await CheckRequestUi(Check);
   await CheckDialogUi(Check);
   await CheckPhoneLink(Check);
   await CheckTerminalScrolling(Check);
   await CheckTabClosing(first,second,Check);
   Check(!UpdateIdle(),"automatic update preserves drafts on every tab");
   foreach(var t in tabs)t.Draft="";Composer.Clear();
   Check(UpdateIdle(),"inactive idle desktop can update");
   first.Sending=true;Check(!UpdateIdle(),"automatic update waits for message send");first.Sending=false;
   first.Attachments.Add("synthetic.png");Check(!UpdateIdle(),"automatic update waits for attachment");first.Attachments.Clear();
   first.SetClosing(true);Check(!UpdateIdle(),"automatic update waits for tab close");first.SetClosing(false);
   var popup=new Window{Owner=this,ShowActivated=false,ShowInTaskbar=false,Left=-4000,Top=-4000};popup.Show();Check(!UpdateIdle(),"automatic update waits for request or file dialog");popup.Close();
   var saved=System.Text.Json.JsonSerializer.Deserialize<UpdateView>(SaveUpdateView());Check(saved?.selected==selected?.Id,"automatic update restores selected native tab identity");
  }catch(Exception e){passed.Add("FAIL "+e);Environment.ExitCode=1;}
  finally{File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-ui-checks.txt"),passed);CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));}
 }
 async Task CheckTerminalScrolling(Action<bool,string> check){
  string lines=string.Join("\n",Enumerable.Range(1,200).Select(i=>"Terminal fixture line "+i));
  SetView(true);SetTerminalText(lines,true);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();
  check(TerminalText.ExtentHeight>TerminalText.ViewportHeight&&TerminalText.ExtentHeight-TerminalText.ViewportHeight-TerminalText.VerticalOffset<1,"terminal opens at the latest line");
  SetTerminalText(lines+"\nnew output");SetTerminalText(lines+"\nnew output\nmore output");
  await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();
  check(TerminalText.ExtentHeight-TerminalText.ViewportHeight-TerminalText.VerticalOffset<1,"consecutive terminal refreshes retain the bottom after layout");
  TerminalText.ScrollToVerticalOffset(150);await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);double offset=TerminalText.VerticalOffset;
  check(offset>1&&TerminalText.ExtentHeight-TerminalText.ViewportHeight-offset>40,"terminal history can be scrolled manually");
  SetTerminalText(lines+"\nnext update");SetTerminalText(lines+"\nnext update\nlatest update");
  await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();
  check(Math.Abs(TerminalText.VerticalOffset-offset)<1,"refreshes preserve an explicitly selected history position");
  TerminalText.ScrollToEnd();await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
  SetTerminalText(lines+"\nlast");await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);UpdateLayout();
  check(TerminalText.ExtentHeight-TerminalText.ViewportHeight-TerminalText.VerticalOffset<1,"returning to the bottom follows later terminal updates");
  SetView(false);
 }
 async Task CheckTabClosing(DesktopTab first,DesktopTab second,Action<bool,string> check){
  UpdateLayout();
  System.Windows.Controls.Button? CloseButtonIn(DependencyObject node){
   if(node is System.Windows.Controls.Button b&&b.Name=="TabCloseButton")return b;
   for(int i=0;i<Media.VisualTreeHelper.GetChildrenCount(node);i++){var result=CloseButtonIn(Media.VisualTreeHelper.GetChild(node,i));if(result!=null)return result;}return null;
  }
  var row=(System.Windows.Controls.ListBoxItem)Tabs.ItemContainerGenerator.ContainerFromItem(second);
  var button=CloseButtonIn(row)??throw new Exception("Tab close button was not rendered");
  int confirmations=0,closes=0;ConsoleTarget? target=null;
  DemoConfirmClose=t=>{target=t;confirmations++;return false;};DemoCloseTab=_=>{closes++;return Task.CompletedTask;};
  button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
  check(confirmations==1&&target==second.Target&&closes==0&&tabs.Contains(second)&&second.CanClose&&selected==first&&Composer.Text=="Next draft while sending","row close button confirms its own tab; cancel preserves selection and draft");
  check(IsTabButton(button.Content as DependencyObject)&&!IsTabButton(row),"close icon hit testing is excluded from tab drag and selection");
  DemoConfirmClose=_=>true;DemoCloseTab=_=>throw new IOException("Synthetic close failure");
  bool failed=false;try{await CloseTab(second);}catch(IOException){failed=true;}
  check(failed&&tabs.Contains(second)&&second.CanClose&&selected==first,"failed close keeps the tab and enables retry");
  var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  DemoCloseTab=t=>{target=t;closes++;return closed.Task;};var pending=CloseTab(second);await CloseTab(second);
  check(closes==1&&target==second.Target&&!second.CanClose&&selected==first,"pending close fixes its target and ignores duplicate clicks");
  MoveTab(second,0);closed.SetResult();await pending;
  check(!tabs.Contains(second)&&tabs.Contains(first)&&selected==first&&Composer.Text=="Next draft while sending","closing an unselected reordered tab preserves the active conversation");
  DemoCloseTab=_=>Task.CompletedTask;await CloseTab(first);
  check(tabs.Count==1&&selected==tabs[0]&&Messages.ItemsSource==selected.Messages,"closing the active tab selects a surviving tab");
  await CloseTab(tabs[0]);
  check(tabs.Count==0&&selected==null&&Messages.ItemsSource==null&&Composer.Text==""&&!Composer.IsEnabled&&!SendButton.IsEnabled&&RequestButton.Visibility==Visibility.Collapsed,"closing the last tab clears the conversation and disables stale input");
  tabs.Add(first);EnsureTabSelection();
  check(selected==first&&Composer.IsEnabled&&SendButton.IsEnabled,"newly discovered tabs are usable after the list was empty");
 }
}
