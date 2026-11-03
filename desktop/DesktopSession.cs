using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;

namespace TerminalLink;

internal sealed class ChatRow : INotifyPropertyChanged {
 internal ConversationMessage Source{get;}
 readonly string speaker;
 readonly long writtenAt=Environment.TickCount64;
 bool delayed;
 internal bool PendingReceipt=>Source.role=="user"&&Source.phase=="sent";
 public string Speaker=>PendingReceipt?(delayed?"나 · 접수 미확인":"나 · 접수 확인 중"):speaker;
 public string? DeliveryHint=>PendingReceipt?"터미널에는 전달했지만 Codex 접수는 아직 확인되지 않았습니다. 입력칸에 남아 있는지 터미널에서 확인하세요. 자동 재전송하지 않습니다.":null;
 public event PropertyChangedEventHandler? PropertyChanged;
 internal void RefreshDelivery(long now){if(PendingReceipt&&!delayed&&now-writtenAt>=8000){delayed=true;PropertyChanged?.Invoke(this,new(nameof(Speaker)));}}
 public string Text{get;}
 public bool Mine{get;}
 public long Sequence{get;}
 internal ChatRow(ConversationMessage m){Source=m;var clean=DisplayText.Message(m);speaker=m.role=="user"?"나":m.phase=="commentary"?"Codex · 진행중":"Codex";Text=m.role=="user"&&m.phase is not ("sent" or "attachment-display")&&ReferenceEquals(clean,m)?AttachmentMessage.Read(clean.text).Display:clean.text;Mine=m.role=="user";Sequence=m.sequence;}
}

internal sealed class DesktopTab : INotifyPropertyChanged {
 internal ConsoleTarget Target;
 internal readonly ConversationReader Reader=new();
 internal Conversation? Original;
 internal ConsoleFrame? Frame;
 internal bool Ready;
 internal string NewIdentity="";
 internal string MessageIdentity="";
 internal bool CanSendConversation=>Ready&&Original!=null||NewIdentity.Length>0||MessageIdentity.Length>0;
 bool firstSubmitted;
 internal bool AwaitingFirstRecord=>firstSubmitted&&Original==null;
 internal bool Starting;
 internal string WelcomeReadIdentity="";
 internal string Error="",Draft="";
 internal string SendError="";
 internal bool Sending;
 readonly SentEchoes sent=new();
 internal void ConfirmSent(string thread,long after,string text,string[] files,string[]? imagePaths=null){if(Original?.id!=thread)return;sent.Add(thread,after,text,files,imagePaths);RefreshMessages();}
 internal void ConfirmStart(string text,string thread){
  firstSubmitted=true;NewIdentity="";
  if(Original!=null){if(Original.id==thread)ConfirmSent(thread,0,text,Array.Empty<string>());return;}
  Messages.Add(new(new("user","sent",text,-1)));
 }
 internal double ScrollOffset;
 internal bool FollowBottom=true;
 readonly UnreadReplies unread;
 bool hasUnread;
 public bool HasUnread=>hasUnread;
 internal void RefreshUnread(){bool value=unread.HasUnread;if(hasUnread==value)return;hasUnread=value;PropertyChanged?.Invoke(this,new(nameof(HasUnread)));}
 internal void ObserveReplies(Conversation? chat){unread.Observe(chat);RefreshUnread();}
 internal void MarkReplyRead(){try{unread.MarkDisplayed(Original);}finally{RefreshUnread();}}
 internal readonly List<string> Attachments=new();
 internal readonly Dictionary<string,(int choice,string text)> RequestDrafts=new();
 internal readonly Dictionary<string,string> RequestSubmissions=new();
 public string Name=>Target.Name;
 public string Id=>Target.Id;
 bool closing;
 public bool CanClose=>!closing;
 internal void SetClosing(bool value){closing=value;PropertyChanged?.Invoke(this,new(nameof(CanClose)));}
 public ObservableCollection<ChatRow> Messages{get;}=new();
 public event PropertyChangedEventHandler? PropertyChanged;
 internal DesktopTab(ConsoleTarget target,ReadReceipts? receipts=null){Target=target;unread=new(receipts??new(null));}
 internal QueuedQuestion? Request{
  get{
   var prompt=Frame==null?null:NotificationInput.Parse(Frame.text);
   // A blocking native approval takes precedence over an older queued question.
   if(prompt is {questionKind:false})return new("",prompt.question,prompt.choices);
   // An authoritative empty request list means the question was resolved.
   // A stale terminal frame must not resurrect it after the original record advances.
   if(Original?.requests is {} pending){
    if(pending.FirstOrDefault() is {} next)return next;
    // Native blocking questions do not necessarily appear in the async queue.
    if(prompt is {questionKind:true,followup:false})return new("",prompt.question,prompt.choices);
    return null;
   }
   return prompt==null?null:new QueuedQuestion("",prompt.question,prompt.choices);
  }
 }
 internal void Rename(ConsoleTarget target){if(Target.Name==target.Name)return;Target=target;PropertyChanged?.Invoke(this,new(nameof(Name)));}
 internal void Accept(ConsoleFrame frame,Conversation? conversation){
  Frame=frame;Ready=ConversationReader.IsOriginal(conversation);
  MessageIdentity=NativeMessageInput.CanCompose(frame)?NativeMessageInput.Identity(frame):"";
  NewIdentity=!Ready&&!firstSubmitted?DesktopStartInput.Identity(frame):"";
  if(NewIdentity.Length>0&&Original!=null){Original=null;Messages.Clear();ScrollOffset=0;FollowBottom=true;}
  if(!Ready){Error=NewIdentity.Length>0||Starting?"":AwaitingFirstRecord?"입력을 터미널에 전달했습니다. Codex 접수와 대화 원본 연결을 확인 중입니다.":conversation?.readError??"대화 기록을 확인할 수 없습니다. 터미널 보기에서 확인할 수 있습니다.";return;}
  firstSubmitted=false;
  Error="";
  ObserveReplies(conversation);
  if(Original?.id!=conversation!.id){Messages.Clear();ScrollOffset=0;FollowBottom=true;}
  if(ReferenceEquals(Original,conversation))return;
  Original=conversation;
  RefreshMessages();
 }
 void RefreshMessages(){
  if(Original==null)return;
  var visible=sent.Project(Original).Where(m=>m.role=="user"||m.role=="assistant"&&m.phase is "commentary" or "final" or "final_answer" or "").ToArray();
  // Keep completed row instances and their rendered controls when only the tail changes.
  int i=0;
  while(i<Messages.Count&&i<visible.Length&&Messages[i].Source==visible[i])i++;
  while(Messages.Count>i)Messages.RemoveAt(Messages.Count-1);
  for(;i<visible.Length;i++)Messages.Add(new ChatRow(visible[i]));
 }
}

internal sealed class DesktopSession : IDisposable {
 readonly SemaphoreSlim gate=new(1,1);
 readonly RetainedConnections<ConsoleBridge> connections=new();
 readonly DesktopBackgroundReader background=new();
 internal static List<TerminalWindow> Windows()=>TerminalWindows.Read().Where(w=>w.Process.Equals("WindowsTerminal",StringComparison.OrdinalIgnoreCase)).ToList();
 internal async Task<List<ConsoleTarget>> Discover(CancellationToken ct)=>await ConsoleTitles.Read(ConsoleNative.Targets(Windows()),ct);
 internal void Select(DesktopTab tab,long version)=>connections.Select(tab.Id,version,()=>new(tab.Target));
 internal (ConsoleFrame? frame,Conversation? conversation,string? error) Read(DesktopTab tab){
   using var lease=connections.ReadSelected(tab.Id);if(lease==null)return(null,null,null);var bridge=lease.Value;
   if(bridge.Closed||!bridge.Target.Valid)return(null,null,bridge.Error??"터미널이 종료되었거나 연결이 바뀌었습니다.");
   var frame=bridge.Latest;if(frame==null)return(null,null,bridge.Error);
   lock(tab.Reader)return(frame,ReadOriginal(tab,frame),bridge.Error);
 }
 static Conversation? ReadOriginal(DesktopTab tab,ConsoleFrame frame){
  // A verified fresh welcome screen is not the old chat's temporary redraw.
  // Release only its display binding; never touch a terminal or original file.
  if(DesktopStartInput.Identity(frame) is {Length:>0} identity){
   if(tab.WelcomeReadIdentity!=identity&&!tab.Sending&&!tab.AwaitingFirstRecord){LocalDaemonFeed.Forget(identity);tab.Reader.Retry();}
   tab.WelcomeReadIdentity=identity;return null;
  }
  tab.WelcomeReadIdentity="";
  return tab.Reader.Read(frame);
 }
 internal Task ReadBackground(DesktopTab[] tabs,Action<BackgroundObservation> publish,CancellationToken ct)=>Task.Run(async()=>{
  int i=0;
  await foreach(var f in background.Stream(tabs.Select(t=>t.Target).ToArray(),ct)){
   var tab=tabs[i++];Conversation? chat=null;
   try{if(f.Frame!=null&&tab.Target.Valid)lock(tab.Reader)chat=ReadOriginal(tab,f.Frame);}catch(IOException){}catch(UnauthorizedAccessException){}
   publish(new(tab,chat));
  }
 },ct);
 internal async Task<string[]> Send(ConsoleTarget target,string thread,string[] attachments,string text,bool terminal,string identity,CancellationToken ct){
  using var lease=connections.Acquire(target.Id,()=>new(target));
  await gate.WaitAsync(ct);try{
   var b=Require(lease.Value);
   if(terminal&&text.Length==0){b.Send(JsonSerializer.SerializeToElement(new{op="key",key="ENTER"}));return Array.Empty<string>();}
   if(!terminal&&attachments.Length>0){if(thread.Length==0)throw new IOException("첨부할 대화 원본을 확인하지 못했습니다. 선택한 첨부는 유지됩니다.");return await Attach(b,thread,attachments,text,ct);}
   await Result(b,JsonSerializer.SerializeToElement(new{op="notificationInput",request=Guid.NewGuid().ToString(),kind="nativeMessage",identity,text}),ct);
   return Array.Empty<string>();
  }finally{gate.Release();}
 }
 internal async Task<string> Start(ConsoleTarget target,string identity,string text,CancellationToken ct){
  using var lease=connections.Acquire(target.Id,()=>new(target));
  await gate.WaitAsync(ct);try{
   var b=Require(lease.Value);
   var result=await b.Notification(JsonSerializer.SerializeToElement(new{op="notificationInput",request=Guid.NewGuid().ToString(),kind="startMessage",identity,text}),ct,TimeSpan.FromSeconds(20));
   if(!result.Bool("ok"))throw new IOException(result.Str("message"));
   return result.Str("thread");
  }finally{gate.Release();}
 }
 internal async Task Answer(DesktopTab tab,string thread,QueuedQuestion question,int choice,string text,CancellationToken ct){
  if(question.id.Length>0){
   string identity=thread+"\n"+question.id;
   bool pending=tab.RequestSubmissions.TryGetValue(identity,out var nonce);nonce??=Guid.NewGuid().ToString();
   var command=pending?JsonSerializer.SerializeToElement(new{op="requestStatus",request=nonce,id=tab.Id}):JsonSerializer.SerializeToElement(new{op="notificationInput",request=nonce,id=tab.Id,kind="request",thread,requestKey=question.id,question=question.title,choices=question.options.Length>0?question.options.Append("직접 입력").ToArray():question.options,choice,text});
   tab.RequestSubmissions[identity]=nonce;
   JsonElement result;
   try{result=await RequestClient.Send(command,ct);}catch(RequestNotSentException)when(!pending){tab.RequestSubmissions.Remove(identity);throw;}catch(Exception e)when(e is IOException or TimeoutException or OperationCanceledException){throw new RequestPendingException("전송 결과를 확인하지 못했습니다. ‘전송 결과 확인’을 눌러 주세요. 자동으로 다시 보내지 않습니다. "+e.Message);}
   if(result.Bool("ok")){tab.RequestSubmissions.Remove(identity);return;}
   if(result.Str("delivery")=="Rejected"){tab.RequestSubmissions.Remove(identity);throw new IOException(result.Str("message"));}
   if(result.Str("submissionId") is {Length:>0} canonical)tab.RequestSubmissions[identity]=canonical;
   throw new RequestPendingException(result.Str("message"));
  }
  using var lease=connections.Acquire(tab.Id,()=>new(tab.Target));
  await gate.WaitAsync(ct);try{
   var b=Require(lease.Value);
   if(!tab.Ready||tab.Original==null||tab.Original.id!=thread)throw new IOException("연결 또는 대화가 바뀌었습니다. 작성한 답변은 유지됩니다.");
   bool followup=true,questionKind=true;string[] choices;
   if(question.id.Length>0){
    var current=tab.Original.requests?.FirstOrDefault(q=>q.id==question.id);
    if(current==null||current.title!=question.title||!current.options.SequenceEqual(question.options))throw new IOException("요청이 완료되거나 바뀌었습니다.");
    choices=question.options.Length>0?question.options.Append("직접 입력").ToArray():question.options;
   }else{
    var current=b.Latest==null?null:NotificationInput.Parse(b.Latest.text);
    if(current==null||current.question!=question.title||!current.choices.SequenceEqual(question.options))throw new IOException("요청이 완료되거나 바뀌었습니다.");
    choices=current.choices;followup=current.followup;questionKind=current.questionKind;
   }
   await Result(b,JsonSerializer.SerializeToElement(new{op="notificationInput",request=Guid.NewGuid().ToString(),kind="request",thread,requestKey=question.id,question=question.title,choices,followup,questionKind,choice,text}),ct);
  }finally{gate.Release();}
 }
 internal async Task Key(DesktopTab tab,string key,bool ctrl=false){using var lease=connections.Acquire(tab.Id,()=>new(tab.Target));await gate.WaitAsync();try{Require(lease.Value).Send(JsonSerializer.SerializeToElement(new{op="key",key,ctrl}));}finally{gate.Release();}}
 static ConsoleBridge Require(ConsoleBridge bridge)=>bridge.Target.Valid&&!bridge.Closed?bridge:throw new IOException("선택한 터미널의 연결이 바뀌었습니다.");
 static async Task Result(ConsoleBridge b,JsonElement cmd,CancellationToken ct){var result=await b.Notification(cmd,ct,TimeSpan.FromSeconds(LocalImageTarget.DeliverySeconds));if(!result.Bool("ok"))throw new IOException(result.Str("message"));}
 static async Task<string[]> Attach(ConsoleBridge b,string thread,string[] attachments,string text,CancellationToken ct){
  if(attachments.Length>4)throw new IOException("사진과 파일은 합쳐서 4개까지 첨부하세요.");
  Directory.CreateDirectory(DesktopPaths.AttachmentDirectory);
  var images=new List<string>();var files=new List<FileUploads.Attachment>();
  foreach(var source in attachments){
   ct.ThrowIfCancellationRequested();var info=new FileInfo(source);if(!info.Exists||info.Length>32*1024*1024)throw new IOException("첨부 파일을 확인하세요. 파일당 최대 32MB입니다.");
   bool image=new[]{".png",".jpg",".jpeg",".webp",".bmp",".gif"}.Contains(info.Extension.ToLowerInvariant());
   string dest=Path.Combine(DesktopPaths.AttachmentDirectory,Guid.NewGuid().ToString("N")+(image?".png":info.Extension));
   if(image){if(info.Length>8*1024*1024)throw new IOException("사진은 8MB 이하로 첨부하세요.");using var bitmap=System.Drawing.Image.FromFile(source);if((long)bitmap.Width*bitmap.Height>40000000)throw new IOException("사진의 해상도가 너무 큽니다.");bitmap.Save(dest,System.Drawing.Imaging.ImageFormat.Png);images.Add(dest);}
   else{File.Copy(source,dest,false);files.Add(new(dest,FileUploads.SafeName(info.Name)));}
  }
  string request=Guid.NewGuid().ToString();
  if(thread.StartsWith("remote:",StringComparison.Ordinal)){
   var upload=await Task.Run(()=>RemoteImages.Transfer(b,thread,images.ToArray(),text,ct,files.ToArray()),ct);
   await Result(b,JsonSerializer.SerializeToElement(new{op="remoteImageInput",request,upload,text,thread}),ct);
   return upload.ImagePaths;
  }else await Result(b,JsonSerializer.SerializeToElement(new{op="imageInput",request,paths=images,files,text,thread}),ct);
  return images.ToArray();
 }
 public void Dispose(){connections.Dispose();background.Dispose();RemoteCodexFeed.Stop();}
}
