using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf=System.Windows.Controls;
using Media=System.Windows.Media;

namespace TerminalLink;

public partial class DesktopWindow : Window {
 readonly bool demo;
 readonly DesktopSession session=new();
 readonly CancellationTokenSource life=new();
 readonly ObservableCollection<DesktopTab> tabs=new();
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(1)};
 readonly VisibilityLease? visibility;
 DesktopTab? selected;
 bool ticking,busy,closing,terminalView,changingScroll,windowInitialized;
 long catalogTime;
 long selectionVersion;
 long terminalRevision;
 DesktopTab? terminalPendingTab;
 bool terminalRestorePending,terminalPendingBottom;
 double terminalPendingOffset;
 Wpf.ScrollViewer? scroll;
 RequestWindow? requestWindow;
 readonly HashSet<long> shown=new();
 readonly bool renderPreview;
 readonly bool liveCheck;
 string actionError="";
 internal Func<Task>? DemoSend;
 public DesktopWindow(bool demo,bool renderPreview=false,bool liveCheck=false,bool showPhoneLink=false){
  this.renderPreview=renderPreview;
  this.liveCheck=liveCheck;
  this.demo=demo;InitializeComponent();InitializeChrome();InitializePhoneLink();ShowActivated=!liveCheck&&!renderPreview;Title=demo?"TerminalLink Desktop · 미리보기":"TerminalLink Desktop";
  tabOrder=new(demo?null:Path.Combine(DesktopPaths.Root,"tab-order-v1.json"));
  receipts=new(demo?null:Path.Combine(DesktopPaths.Root,"read-receipts-v1.json"));InitializeUnread();
  Tabs.ItemsSource=tabs;Tabs.SelectionChanged+=async(_,_)=>{if(!reorderingTabs&&Tabs.SelectedItem is DesktopTab t&&t!=selected)await Select(t);};InitializeTabs();
  if(!demo)visibility=new VisibilityLease();
  RefreshButton.Click+=async(_,_)=>await Refresh();RetryButton.Click+=async(_,_)=>{actionError="";if(selected!=null){selected.SendError="";selected.Reader.Retry();}await Tick();};
  SendButton.Click+=async(_,_)=>await Send();
  Composer.PreviewKeyDown+=async(_,e)=>{if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.None&&!ImeComposing){e.Handled=true;await Send();}};
  TextCompositionManager.AddPreviewTextInputStartHandler(Composer,(_,_)=>ImeComposing=true);
  TextCompositionManager.AddPreviewTextInputHandler(Composer,(_,_)=>ImeComposing=false);
  Composer.TextChanged+=(_,_)=>{if(selected!=null)selected.Draft=Composer.Text;};
  ConversationButton.Click+=(_,_)=>SetView(false);TerminalButton.Click+=(_,_)=>SetView(true);
  LatestButton.Click+=(_,_)=>{if(terminalView){TerminalText.ScrollToEnd();UpdateLatestButton();}else Bottom();};AttachButton.Click+=(_,_)=>Attach();RemoveAttachmentButton.Click+=(_,_)=>ClearAttachments();
  TerminalText.AddHandler(Wpf.ScrollViewer.ScrollChangedEvent,new Wpf.ScrollChangedEventHandler((_,_)=>UpdateLatestButton()));
  RequestButton.Click+=(_,_)=>ShowRequest();FileBoxButton.Click+=async(_,_)=>await Action(()=>{FileBox();return Task.CompletedTask;});
  NativeButton.Click+=async(_,_)=>await NativeToggle();
  foreach(var key in new[]{"ENTER","ESC","TAB","UP","DOWN","LEFT","RIGHT","CTRL+C"}){
   var button=Button(key);button.Click+=async(_,_)=>{var tab=selected;if(tab==null||busy)return;await Action(async()=>{if(!demo)await Task.Run(()=>session.Key(tab,key=="CTRL+C"?"C":key,key=="CTRL+C"));});};Keys.Children.Add(button);
  }
  Loaded+=async(_,_)=>{
   if(windowInitialized)return;windowInitialized=true;
   scroll=FindScroll(Messages);if(scroll!=null)scroll.ScrollChanged+=(_,_)=>{if(!changingScroll&&selected!=null){selected.ScrollOffset=scroll.VerticalOffset;selected.FollowBottom=scroll.ScrollableHeight-scroll.VerticalOffset<40;AcknowledgeVisibleReply();}UpdateLatestButton();};
   if(demo){LoadDemo();if(renderPreview)ExportPreview();return;}
   phoneWork=EnsurePhoneLink(showPhoneLink,showPhoneLink);await phoneWork;phoneHealth.Start();
   await Refresh();timer.Tick+=async(_,_)=>await Tick();timer.Start();unreadTimer.Start();if(liveCheck)_=VerifyLive();
  };
  Closing+=async(_,e)=>{
   if(!phoneClosed&&(phoneLease!=null||phoneAttaching)){
    e.Cancel=true;if(phoneClosing)return;phoneClosing=true;
    try{await ClosePhoneLink(true);phoneClosed=true;Close();}catch(Exception error){SetPhoneView(true);PhoneStatus(error.Message);}finally{phoneClosing=false;}return;
   }
   closing=true;timer.Stop();unreadTimer.Stop();phoneHealth.Stop();life.Cancel();requestWindow?.Close();visibility?.Dispose();
  };
  Closed+=(_,_)=>{_=Task.Run(async()=>{await backgroundWork;session.Dispose();});};
  SetView(false);SetIcon();InitializeTray();InitializeUpdates();
 }
 bool ImeComposing;
 void SetIcon()=>Icon=DesktopIcon.Image;
 internal static Wpf.Button Button(string text)=>new(){Content=text,Padding=new(12,8,12,8),Margin=new(4)};
 internal static Wpf.ScrollViewer? FindScroll(DependencyObject root){if(root is Wpf.ScrollViewer s)return s;for(int i=0;i<Media.VisualTreeHelper.GetChildrenCount(root);i++){var found=FindScroll(Media.VisualTreeHelper.GetChild(root,i));if(found!=null)return found;}return null;}
 async Task Action(Func<Task> action){try{await action();}catch(OperationCanceledException){}catch(Exception e){Error(e is TimeoutException?"처리 결과를 확인하지 못했습니다. 자동으로 재전송하지 않았습니다.":e.Message);}}
 void Error(string message){actionError=DesktopErrorText.Display(message);ShowNotice(actionError);}
 void ShowNotice(string message){
  var visibility=message.Length>0?Visibility.Visible:Visibility.Collapsed;
  if(Notice.Text==message&&NoticeBar.Visibility==visibility)return;
  var tab=selected;bool follow=tab?.FollowBottom==true,wasChanging=changingScroll;
  double offset=scroll?.VerticalOffset??0;
  bool terminalFollow=TerminalText.ExtentHeight-TerminalText.ViewportHeight-TerminalText.VerticalOffset<40;
  double terminalOffset=TerminalText.VerticalOffset;
  changingScroll=true;
  try{
   Notice.Text=message;NoticeBar.Visibility=visibility;
   if(tab==null||phoneView)return;
   // A banner changes the viewport, not the user's scroll intent. Complete
   // that layout while ScrollChanged is suppressed, then restore the position.
   UpdateLayout();
   if(terminalView){if(terminalFollow)TerminalText.ScrollToEnd();else TerminalText.ScrollToVerticalOffset(terminalOffset);}
   else if(scroll!=null){if(follow){if(Messages.Items.Count>0)Messages.ScrollIntoView(Messages.Items[^1]);scroll.ScrollToEnd();}else scroll.ScrollToVerticalOffset(offset);}
   UpdateLayout();
  }finally{changingScroll=wasChanging;}
 }
 void ExportPreview(){
  var delayed=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(600)};
  delayed.Tick+=(_,_)=>{delayed.Stop();UpdateLayout();var visual=(FrameworkElement)Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,Media.PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"desktop-preview.png")))encoder.Save(output);Close();};delayed.Start();
 }
 async Task Refresh(){if(demo)return;await Action(async()=>{
  var fresh=await Task.Run(()=>session.Discover(life.Token));if(closing)return;
  foreach(var target in fresh){var prior=tabs.FirstOrDefault(t=>t.Id==target.Id);if(prior==null)tabs.Add(new(target,receipts));else prior.Rename(target);}
  foreach(var old in tabs.Where(t=>!fresh.Any(f=>f.Id==t.Id)).ToArray())tabs.Remove(old);
  ApplyTabOrder();
  catalogTime=Environment.TickCount64;
  EnsureTabSelection();
 });}
 async Task Select(DesktopTab tab){
  if(closing)return;
  // Background rediscovery must not dismiss the phone page; explicit tab clicks do.
  if(selected!=null){selected.Draft=Composer.Text;if(scroll!=null)selected.ScrollOffset=scroll.VerticalOffset;}
  requestWindow?.Close();actionError="";selected=tab;UpdateTabControls();Composer.Text=tab.Draft;Messages.ItemsSource=tab.Messages;TabTitle.Text=tab.Name;
  SetTerminalText(tab.Frame?.text??"",true);RenderState();RestoreScroll(tab);AttachmentLabel();
  if(demo)return;long version=++selectionVersion;
  await Action(async()=>{await Task.Run(()=>session.Select(tab,version));if(selected!=tab||closing)return;await Tick();});
 }
 async Task Tick(){
  if(ticking||closing||demo)return;ticking=true;
  try{
   visibility?.CheckHealth();if(Environment.TickCount64-catalogTime>4000)await Refresh();var tab=selected;if(tab==null)return;
   var result=await Task.Run(()=>session.Read(tab));if(closing||selected!=tab)return;
   bool follow=tab.FollowBottom;int count=tab.Messages.Count;
   changingScroll=true;
   if(result.frame!=null){tab.Accept(result.frame,result.conversation);if(tab.Target.Name!=result.frame.title&&result.frame.title.Length>0)tab.Rename(tab.Target with{Name=result.frame.title});
    if(terminalView&&TerminalText.Text!=result.frame.text)SetTerminalText(result.frame.text);
   }
   if(result.error!=null)tab.Error=result.error;
   RenderState();
   if(count!=tab.Messages.Count&&follow)Bottom();
   _=Dispatcher.BeginInvoke(()=>{changingScroll=false;UpdateLatestButton();AcknowledgeVisibleReply();},DispatcherPriority.ContextIdle);
   if(result.frame!=null&&!shown.Contains(tab.Target.Window.Handle)){
    await visibility!.Set(tab.Target.Window,true);shown.Add(tab.Target.Window.Handle);NativeButton.Content="Windows Terminal 열기";
   }
  }catch(OperationCanceledException){}catch(Exception e){Error(e.Message);}finally{ticking=false;}
 }
 async Task VerifyLive(){
  var results=new List<object>();var windows=tabs.Select(t=>t.Target.Window).DistinctBy(w=>w.Handle).ToArray();
  try{
   foreach(var tab in tabs.ToArray()){
    if(closing)break;Tabs.SelectedItem=tab;
    long until=Environment.TickCount64+18000;
    while(!closing&&(!tab.Ready||!shown.Contains(tab.Target.Window.Handle))&&Environment.TickCount64<until)await Task.Delay(300);
    results.Add(new{tab=tab.Name,tab.Ready,messages=tab.Messages.Count,source=tab.Original?.source,valid=tab.Target.Valid,hidden=WindowVisibility.Hidden(tab.Target.Window),error=tab.Error,actionError,remoteDiagnostic=RemoteCodexFeed.Diagnostic,answerAnchors=tab.Frame==null?0:RemoteCodexFeed.AnswerAnchors(tab.Frame).Length});
   }
   UpdateLayout();var visual=(FrameworkElement)Content;var bmp=new System.Windows.Media.Imaging.RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,Media.PixelFormats.Pbgra32);bmp.Render(visual);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));using(var output=File.Create(Path.Combine(AppContext.BaseDirectory,"desktop-live.png")))encoder.Save(output);
  }catch(Exception e){results.Add(new{error=e.Message});}
  finally{
   visibility?.Dispose();
   var restored=windows.Select(w=>new{sameWindow=WindowVisibility.Same(w),hidden=WindowVisibility.Same(w)?WindowVisibility.Hidden(w):-1}).ToArray();
   File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"desktop-live-check.json"),JsonSerializer.Serialize(new{results,restored,inputsSent=0,memoryMB=Process.GetCurrentProcess().WorkingSet64/1024/1024},new JsonSerializerOptions{WriteIndented=true}));Close();
  }
 }
 void RenderState(){
  UpdateLatestButton();
  var usage=selected?.Original?.weeklyUsage;long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
  bool valid=usage!=null&&usage.usedPercent>=0&&usage.usedPercent<=100&&usage.resetsAt>now&&usage.observedAt>0&&usage.observedAt<=now+300;
  WeeklyUsage.Text=valid?$"{usage!.usedPercent:0}%":"--%";
  WeeklyUsage.ToolTip=valid?$"Codex 주간 사용률 {WeeklyUsage.Text} · 확인 시각 {DateTimeOffset.FromUnixTimeSeconds(usage!.observedAt).ToLocalTime():MM-dd HH:mm}":"Codex 주간 사용률을 확인하지 못했습니다.";
  if(selected==null)return;var tab=selected;
  foreach(var row in tab.Messages.Where(m=>m.PendingReceipt))row.RefreshDelivery(Environment.TickCount64);
  Composer.IsEnabled=!tab.Sending;AttachButton.IsEnabled=!tab.Sending&&tab.NewIdentity.Length==0;RemoveAttachmentButton.IsEnabled=!tab.Sending&&tab.Attachments.Count>0;NativeButton.IsEnabled=true;
  TabTitle.Text=tab.Name;EmptyText.Visibility=!terminalView&&tab.Messages.Count==0?Visibility.Visible:Visibility.Collapsed;
  EmptyText.Text=tab.NewIdentity.Length>0?"새 Codex 대화입니다.\n아래에서 첫 메시지를 보내세요. 첨부는 대화가 시작된 뒤 사용할 수 있어요.":tab.Error.Length>0?"대화 기록을 확인하지 못했습니다.\n일반 메시지는 아래 입력창에서 선택한 터미널로 보낼 수 있습니다.":"대화 원본을 읽고 있어요.";
  ShowNotice(DesktopErrorText.Display(actionError.Length>0?actionError:tab.SendError.Length>0?tab.SendError:tab.Error.Length>0?(tab.Messages.Count>0?"연결 확인 중입니다. 마지막으로 확인한 대화를 유지합니다. "+DesktopErrorText.Display(tab.Error):tab.Error):""));
  SendButton.IsEnabled=!busy&&(terminalView||tab.CanSendConversation);SendButton.Content=tab.Sending?"전송 중…":"전송 ↑";
  RequestButton.Visibility=tab.Request!=null?Visibility.Visible:Visibility.Collapsed;
  var last=tab.Original?.messages.LastOrDefault();
  bool working=tab.Frame!=null&&Regex.IsMatch(string.Join("\n",tab.Frame.text.Split('\n').TakeLast(12)),@"(?im)(esc to interrupt|Working\s*\()");
  ActivityText.Text=working?"작업 중…":last?.role=="assistant"&&last.phase is "final" or "final_answer"?"답변 완료":"";
  Footer.Text=demo?"미리보기 · 실제 터미널에 입력하지 않음":$"{tabs.Count}개의 터미널 · 로컬 연결";
  if(requestWindow!=null)requestWindow.Refresh(tab);
 }
 void SetTerminalText(string text,bool bottom=false){
  var tab=selected;bool pending=terminalRestorePending&&terminalPendingTab==tab;
  bool follow=bottom||(pending?terminalPendingBottom:TerminalText.ExtentHeight-TerminalText.ViewportHeight-TerminalText.VerticalOffset<40);
  double offset=pending?terminalPendingOffset:TerminalText.VerticalOffset;
  terminalPendingTab=tab;terminalPendingBottom=follow;terminalPendingOffset=offset;terminalRestorePending=true;long revision=++terminalRevision;
  TerminalText.Text=text;
  _=Dispatcher.BeginInvoke(()=>{
   if(revision!=terminalRevision)return;
   terminalRestorePending=false;
   if(!terminalView||selected!=tab)return;
   TerminalText.UpdateLayout();
   if(follow)TerminalText.ScrollToEnd();else TerminalText.ScrollToVerticalOffset(offset);
   TerminalText.UpdateLayout();UpdateLatestButton();
  },DispatcherPriority.ContextIdle);
 }
 void SetView(bool terminal){terminalView=terminal;ConversationButton.Background=(Media.Brush)new Media.BrushConverter().ConvertFromString(terminal?"#282C30":"#34514F")!;TerminalButton.Background=(Media.Brush)new Media.BrushConverter().ConvertFromString(terminal?"#34514F":"#282C30")!;Messages.Visibility=terminal?Visibility.Collapsed:Visibility.Visible;TerminalText.Visibility=terminal?Visibility.Visible:Visibility.Collapsed;Keys.Visibility=terminal?Visibility.Visible:Visibility.Collapsed;AttachButton.Visibility=RemoveAttachmentButton.Visibility=terminal?Visibility.Collapsed:Visibility.Visible;if(terminal)SetTerminalText(selected?.Frame?.text??"터미널 내용을 기다리고 있습니다.",true);RenderState();}
 void UpdateLatestButton(){
  if(selected==null||!terminalView&&selected.Messages.Count==0){LatestButton.Visibility=Visibility.Hidden;return;}
  if(changingScroll)return;
  bool older=terminalView?TerminalText.ExtentHeight-TerminalText.ViewportHeight-TerminalText.VerticalOffset>1:scroll!=null&&scroll.ScrollableHeight-scroll.VerticalOffset>1;
  // Reserve the row's size so hiding the button cannot shift the scroll viewport.
  LatestButton.Visibility=older?Visibility.Visible:Visibility.Hidden;
 }
 void Bottom(){var tab=selected;if(tab!=null)tab.FollowBottom=true;changingScroll=true;LatestButton.Visibility=Visibility.Hidden;if(Messages.Items.Count>0)Messages.ScrollIntoView(Messages.Items[^1]);Dispatcher.BeginInvoke(()=>{if(selected!=tab)return;scroll??=FindScroll(Messages);scroll?.ScrollToEnd();changingScroll=false;UpdateLatestButton();AcknowledgeVisibleReply();},DispatcherPriority.ContextIdle);}
 void RestoreScroll(DesktopTab tab){changingScroll=true;LatestButton.Visibility=Visibility.Hidden;Dispatcher.BeginInvoke(()=>{scroll??=FindScroll(Messages);if(selected!=tab)return;if(tab.FollowBottom)Bottom();else scroll?.ScrollToVerticalOffset(tab.ScrollOffset);changingScroll=false;UpdateLatestButton();},DispatcherPriority.ContextIdle);}
 async Task Send(){
  var tab=selected;if(tab==null||busy||(string.IsNullOrWhiteSpace(Composer.Text)&&tab.Attachments.Count==0))return;
  bool start=!demo&&!terminalView&&tab.NewIdentity.Length>0;
  if(start&&tab.Attachments.Count>0){Error("첫 메시지를 보낸 뒤 첨부할 수 있습니다. 선택한 첨부는 유지됩니다.");return;}
  if(!demo&&!terminalView&&tab.Attachments.Count>0&&(!tab.Ready||tab.Original==null)){Error("첨부할 대화 원본을 확인하지 못했습니다. 선택한 첨부는 유지됩니다.");return;}
  string text=Composer.Text;bool terminal=terminalView;var target=tab.Target;string thread=tab.Original?.id??"";long after=tab.Original?.messages.LastOrDefault()?.sequence??0;string[] attachments=tab.Attachments.ToArray();string identity=tab.MessageIdentity;tab.SendError="";actionError="";tab.Starting=start;busy=tab.Sending=true;RenderState();
  try{
   string[] deliveredImages=Array.Empty<string>();
   if(demo){if(DemoSend!=null)await DemoSend();}
   else deliveredImages=await Task.Run(()=>session.Send(target,thread,attachments,text,terminal,identity,life.Token));
   if(closing)return;
   if(!terminal){if(tab.Original==null)tab.ConfirmStart(text,thread);else tab.ConfirmSent(thread,after,text,attachments,deliveredImages);}
   if(tab.Draft==text){tab.Draft="";if(selected==tab&&Composer.Text==text)Composer.Clear();}
   foreach(string attachment in attachments)tab.Attachments.Remove(attachment);
   if(selected==tab){AttachmentLabel();Bottom();}
  }catch(Exception e){if(!closing)tab.SendError=e is TimeoutException?"전송 결과를 확인하지 못했습니다. 입력 내용은 유지했으며 자동 재전송하지 않았습니다.":DesktopErrorText.Display(e.Message);}
  finally{busy=tab.Sending=false;tab.Starting=false;if(!closing){RenderState();if(selected==tab)Composer.Focus();}}
 }
 void Attach(){if(selected==null||selected.Sending)return;var picker=new Microsoft.Win32.OpenFileDialog{Multiselect=true,Title="사진 또는 파일 첨부",Filter="모든 파일|*.*"};if(picker.ShowDialog(this)!=true)return;if(selected.Attachments.Count+picker.FileNames.Length>4){Error("사진과 파일은 합쳐서 4개까지 첨부할 수 있습니다.");return;}selected.Attachments.AddRange(picker.FileNames);AttachmentLabel();}
 void ClearAttachments(){if(selected==null||selected.Sending)return;selected.Attachments.Clear();AttachmentLabel();}
 void AttachmentLabel(){AttachmentText.Text=selected==null?"":string.Join(" · ",selected.Attachments.Select(Path.GetFileName));AttachmentText.Visibility=AttachmentText.Text.Length>0?Visibility.Visible:Visibility.Collapsed;RemoveAttachmentButton.IsEnabled=selected is {Sending:false}&&selected.Attachments.Count>0;}
 void ShowRequest(){
  var tab=selected;if(tab?.Request is not QueuedQuestion q){Error("현재 확인된 요청이 없습니다.");return;}if(requestWindow!=null){requestWindow.Activate();return;}
  if(q.id.Length>0&&tab.Original?.requests is {Length:>1} pending){
   var picker=new ThemedWindow{Owner=this,Title="요청 선택",Width=580,Height=420};var list=new Wpf.StackPanel{Margin=new(20)};QueuedQuestion? chosen=null;
   foreach(var item in pending){var option=Button("");option.Content=new Wpf.TextBlock{Text=item.title,TextWrapping=TextWrapping.Wrap};option.HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left;option.Margin=new(0,0,0,8);option.Click+=(_,_)=>{chosen=item;picker.Close();};list.Children.Add(option);}
   picker.SetBody(new Wpf.ScrollViewer{Content=list,VerticalScrollBarVisibility=Wpf.ScrollBarVisibility.Hidden});picker.ShowDialog();if(chosen!=null)OpenRequest(tab,chosen);return;
  }
  OpenRequest(tab,q);
 }
 void OpenRequest(DesktopTab tab,QueuedQuestion q){string thread=tab.Original?.id??"";bool free=q.id.Length>0||NotificationInput.Parse(tab.Frame?.text??"")?.questionKind==true;requestWindow=new RequestWindow(tab,q,async(choice,text)=>{if(demo)return;await Task.Run(()=>session.Answer(tab,thread,q,choice,text,life.Token));},free){Owner=this};foreach(var type in new[]{typeof(Wpf.Button),typeof(Wpf.TextBox)})requestWindow.Resources[type]=Resources[type];requestWindow.Closed+=(_,_)=>requestWindow=null;requestWindow.Show();}
 async Task NativeToggle(){if(selected==null||demo)return;await Action(async()=>{
  var target=selected.Target;bool visible=WindowVisibility.Hidden(target.Window)==0;
  if(visible){await visibility!.Set(target.Window,true);shown.Add(target.Window.Handle);NativeButton.Content="Windows Terminal 열기";return;}
  await visibility!.Set(target.Window,false);NativeButton.Content="Windows Terminal 숨기기";
  using var worker=Process.Start(DesktopProgram.Worker("--open-native",target))??throw new IOException("기존 탭을 선택하지 못했습니다.");_=worker.StandardError.ReadToEndAsync();
  try{string? outcome=await worker.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(6));if(outcome!="ok"){WindowVisibility.ShowForeground(target.Window);throw new IOException(outcome??"창은 표시했지만 탭 선택을 확인하지 못했습니다.");}}
  finally{try{if(!worker.HasExited)worker.Kill();}catch{}}
 });}
 void FileBox(){
  if(demo){Error("미리보기에서는 개인 파일함을 열지 않습니다.");return;}
  var directory=Path.Combine(DesktopPaths.Existing,"shared-files");var store=new SharedFiles(directory);
  // Find is read-only; List also cleans old files, so do not call it from this client.
  var items=Directory.Exists(directory)?Directory.EnumerateDirectories(directory).Select(Path.GetFileName).Where(n=>Regex.IsMatch(n??"","^[a-f0-9]{32}$")).Select(n=>store.Find(n!)).OfType<SharedFile>().ToArray():Array.Empty<SharedFile>();
  var win=new FileBoxWindow(items,(owner,file)=>{try{var save=new Microsoft.Win32.SaveFileDialog{FileName=file.name};if(save.ShowDialog(owner)==true){if(store.Find(file.id)==null){MessageDialog.Notify(owner,"파일함","만료된 파일입니다.");return;}File.Copy(store.Data(file.id),save.FileName,true);}}catch(Exception e){MessageDialog.Notify(owner,"파일 저장",e.Message);}},owner=>{
   if(!MessageDialog.Confirm(owner,"파일함 비우기",$"현재 표시된 파일 {items.Length}개를 파일함에서 삭제합니다.\n\nPC 원본과 휴대폰에 받은 파일은 유지됩니다.","비우기",true))return;
   var result=FileBoxClear.Remove(store,items);string message=result.failed==0?$"파일 {result.removed}개를 파일함에서 삭제했습니다.":$"{result.removed}개를 삭제했습니다. {result.failed}개는 삭제하지 못했거나 변경됐습니다. 파일함을 다시 확인해 주세요.";
   MessageDialog.Notify(owner,"파일함 비우기",message);owner.Close();
  }){Owner=this};win.ShowDialog();
 }
 void LoadDemo(){
  var t=new DesktopTab(new(new(0,0,"미리보기","WindowsTerminal"),1,1,"TerminalLink · 미리보기"));tabs.Add(t);var conversation=new Conversation("demo",new[]{new ConversationMessage("user","","모바일에서 하던 대화를 PC에서도 이어서 할 수 있어?",1),new ConversationMessage("assistant","final","네. 같은 터미널의 질문과 답변을 PC 화면에서 확인하고 바로 입력할 수 있어요.\n\n**기존 작업은 그대로 유지돼요.**\n\n| 기능 | 동작 |\n| --- | --- |\n| 대화 | 질문과 답변 읽기 |\n| 요청 | 팝업에서 선택 또는 직접 답변 |\n| Windows Terminal | 같은 창을 숨기거나 다시 표시 |\n\n창 오른쪽 위의 **닫기**를 누르면 숨긴 Windows Terminal이 다시 나타납니다.",2)},false,new[]{new QueuedQuestion("demo-q","PC 화면에서 요청도 바로 답할 수 있는지 확인해 볼까요?",new[]{"확인했어요","다시 볼게요"})});t.Accept(new("미리보기","PS> 기존 Windows Terminal의 텍스트가 표시됩니다.",80,25,0,0,Array.Empty<int>()),conversation);Tabs.SelectedItem=t;Bottom();
 }
}
