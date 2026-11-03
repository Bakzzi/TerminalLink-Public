using System.Text.Json;
namespace TerminalLink;
internal static class TextRemote {
 static readonly System.Collections.Concurrent.ConcurrentDictionary<string,byte> notificationSent=new();

 sealed class ReplyWatch : IDisposable {
  internal readonly ConsoleBridge Bridge;readonly CodexFeed feed=new();Conversation? previous;string lastText="",lastTitle="";bool first=true;
  internal ReplyWatch(ConsoleTarget target){Bridge=new(target);}
  internal object? Read(){
   if(Bridge.Latest is not ConsoleFrame frame)return null;
   var conversation=feed.Read(frame.peers);
   string text=frame.text;
   bool mayRequest=frame.title.Contains("Action Required",StringComparison.OrdinalIgnoreCase)||text.Contains("Queued follow-up inputs")||text.Contains("enter to submit ")||text.Contains("Press enter to confirm");
   string requestText=mayRequest?text[Math.Max(0,text.Length-16000)..]:"";
   if(!first&&ReferenceEquals(conversation,previous)&&requestText==lastText&&frame.title==lastTitle)return null;
   first=false;previous=conversation;lastText=requestText;lastTitle=frame.title;
   return new{type="replyState",id=Bridge.Target.Id,title=frame.title,conversation=DisplayText.ForDisplay(conversation),requestText};
  }
  public void Dispose()=>Bridge.Dispose();
 }

 internal static async Task Run(Stream stream,SemaphoreSlim gate,Identity identity,string fingerprint,Func<List<TerminalWindow>> allowed,CancellationTokenSource life,bool updatesEnabled=false,EventHub? hub=null,bool eventMode=false,PushDelivery? push=null,SharedFiles? sharedFiles=null,bool chunks=false){
  using var shared=new SharedDownload(sharedFiles??new SharedFiles());
  using var images=new ImageUploads();using var files=new FileUploads();
  var updates=new AppUpdates();var sync=new object();ConsoleBridge? bridge=null;List<ConsoleTarget> targets=new();
  using var catalogGate=new SemaphoreSlim(1,1);string catalogKey="";long catalogTime=0;
  async Task List(string? selectId=null,bool onlyChanged=false){
   await catalogGate.WaitAsync(life.Token);
   try{
    var fresh=ConsoleNative.Targets(allowed());string key=string.Join(";",fresh.Select(t=>t.Id).OrderBy(x=>x));
    catalogTime=Environment.TickCount64;
    if(onlyChanged&&key==catalogKey)return;
    fresh=await ConsoleTitles.Read(fresh,life.Token);lock(sync)targets=fresh;catalogKey=key;
    await Protocol.Json(stream,gate,new{type="windows",selectId,windows=fresh.Select(t=>new{id=t.Id,title=t.Name})},life.Token);
   }finally{catalogGate.Release();}
  }
  var watches=new Dictionary<string,ReplyWatch>();
  bool visible=!eventMode;long viewGeneration=0,eventCursor=0,retryGeneration=0;long lastRetry=-2000;bool eventSubscribed=false;
  EventHub.Terminal? selectedTerminal=null;
  void Release(ConsoleBridge? value){if(!eventMode)value?.Dispose();}
  var created=new Dictionary<string,string>();var closed=new Dictionary<string,string>();string closingId="";long lastCreate=-10000;
  await List();
  var sender=Task.Run(async()=>{
   ConsoleBridge? previous=null;ConsoleFrame? lastFrame=null;Conversation? lastConversation=null;ConversationReader feed=new();long heartbeat=0,lastFeed=0,lastWatch=0,lastUpdateCheck=-10000;string? lastError=null,lastRelease="initial";long lastView=-1,lastRetryGeneration=0;
   try{while(!life.IsCancellationRequested){
    if(!identity.IsTrusted(fingerprint))throw new IOException("접속 권한이 해제되었습니다.");
    shared.ExpireIdle();
    if(Volatile.Read(ref visible)&&Environment.TickCount64-Volatile.Read(ref catalogTime)>=2000)await List(onlyChanged:true);
    if(updatesEnabled&&Environment.TickCount64-lastUpdateCheck>=5000){
     lastUpdateCheck=Environment.TickCount64;string? release=updates.Current()?.sha256;
     if(release!=lastRelease){lastRelease=release;await updates.Info(stream,gate,life.Token);}
    }
    ConsoleBridge? selected;EventHub.Terminal? currentTerminal;lock(sync){selected=bridge?.Target.Id==closingId?null:bridge;currentTerminal=selectedTerminal;}
    if(lastView!=Volatile.Read(ref viewGeneration)){lastView=Volatile.Read(ref viewGeneration);lastFrame=null;lastConversation=null;}
     if(!ReferenceEquals(selected,previous)){previous=selected;lastFrame=null;lastConversation=null;feed=new();lastFeed=0;lastError=null;}
    if(lastRetryGeneration!=Volatile.Read(ref retryGeneration)){lastRetryGeneration=Volatile.Read(ref retryGeneration);feed.Retry();lastFeed=0;}
    if(selected!=null){
     if(selected.Closed||!selected.Target.Valid||!allowed().Any(w=>w.Handle==selected.Target.Window.Handle&&w.Pid==selected.Target.Window.Pid)){
      await Protocol.Json(stream,gate,new{type="closed",id=selected.Target.Id,message=selected.Error??"터미널이 닫혔거나 공유가 해제되었습니다."},life.Token);
      lock(sync){if(ReferenceEquals(bridge,selected)){Release(bridge);bridge=null;}}
     }else if(Volatile.Read(ref visible)&&selected.Latest is ConsoleFrame frame){
      Conversation? conversation=lastConversation;
      if(Environment.TickCount64-lastFeed>=1000){lastFeed=Environment.TickCount64;conversation=eventMode?currentTerminal?.Conversation:feed.Read(frame);}
      if(lastFrame==null||lastFrame.text!=frame.text||lastFrame.title!=frame.title||lastFrame.columns!=frame.columns||lastFrame.rows!=frame.rows||!ReferenceEquals(conversation,lastConversation)){
       // JSON only: Android lays out the body using its own width and font size.
       var update=new Dictionary<string,object?>{{"type","terminalText"},{"id",selected.Target.Id},{"title",frame.title},{"text",frame.text},{"columns",frame.columns},{"rows",frame.rows}};
       if(lastFrame==null||!ReferenceEquals(conversation,lastConversation))update["conversation"]=ConversationWire.ForClient(lastConversation,conversation,eventMode,chunks);
       await ConversationWire.Send(stream,gate,update,chunks,life.Token);
       lastFrame=frame;lastConversation=conversation;
      }
     }
     if(selected.Error is string error&&error!=lastError){lastError=error;await Protocol.Json(stream,gate,new{type="status",message=error},life.Token);}
    }
    if(!eventMode&&Environment.TickCount64-lastWatch>=1000){
     lastWatch=Environment.TickCount64;var notifications=new List<object>();
     lock(sync)foreach(var pair in watches.ToArray()){
      var watch=pair.Value;var target=watch.Bridge.Target;
      if(watch.Bridge.Closed||!target.Valid||!allowed().Any(w=>w.Handle==target.Window.Handle&&w.Pid==target.Window.Pid)){
       watch.Dispose();watches.Remove(pair.Key);notifications.Add(new{type="replyWatchClosed",id=pair.Key});
      }else if(watch.Read() is object value)notifications.Add(value);
     }
     foreach(var value in notifications)await Protocol.Json(stream,gate,value,life.Token);
    }
    if(eventMode&&eventSubscribed){object? batch;lock(sync)batch=hub!.Since(ref eventCursor);if(batch!=null)await Protocol.Json(stream,gate,batch,life.Token);}
    if(!eventMode&&Environment.TickCount64-heartbeat>4000){heartbeat=Environment.TickCount64;await Protocol.Json(stream,gate,new{type="ping"},life.Token);}
    await Task.Delay(250,life.Token);
   }}catch(OperationCanceledException){}catch{life.Cancel();}
  },life.Token);
  try{while(!life.IsCancellationRequested){
   using var timeout=CancellationTokenSource.CreateLinkedTokenSource(life.Token);timeout.CancelAfter(TimeSpan.FromSeconds(eventMode?150:30));var message=await Protocol.ReadJson(stream,timeout.Token);
   if(!identity.IsTrusted(fingerprint))throw new IOException("접속 권한이 해제되었습니다.");
   string op=message.Str("op");if(op=="ping"){if(eventMode)await Protocol.Json(stream,gate,new{type="ping"},life.Token);continue;}
   if(eventMode&&op=="viewState"){lock(sync){visible=message.Bool("visible");Interlocked.Increment(ref viewGeneration);}continue;}
   if(eventMode&&op=="eventSync"){bool reset=message.Str("epoch")!=hub!.Epoch;lock(sync){eventCursor=reset?hub!.Head:(long)message.Num("after");eventSubscribed=true;}if(reset)await Protocol.Json(stream,gate,hub!.Snapshot(),life.Token);continue;}
   if(eventMode&&op=="pushRegister"){await Protocol.Json(stream,gate,push!.Register(fingerprint,message),life.Token);continue;}
   if(eventMode&&op=="pushConfirm"){await Protocol.Json(stream,gate,push!.Confirm(fingerprint,message.Str("challenge")),life.Token);continue;}
   if(eventMode&&op=="pushDisable"){push!.Disable(fingerprint);await Protocol.Json(stream,gate,new{type="pushDisabled"},life.Token);continue;}
   if(eventMode&&op=="park"){bool ok=push!.Ready(fingerprint);if(ok)push.Park(fingerprint,(long)message.Num("after"),message.Str("epoch"));await Protocol.Json(stream,gate,new{type="parked",ok},life.Token);if(ok)return;continue;}if(op=="list"){await List();continue;}
   if(updatesEnabled&&op=="updateCheck"){await updates.Info(stream,gate,life.Token);continue;}
   if(updatesEnabled&&op=="updateGet"){await updates.Download(stream,gate,message,()=>identity.IsTrusted(fingerprint),life.Token);continue;}
   if(op is "sharedList" or "sharedOpen" or "sharedRead" or "sharedCancel" or "sharedDelete"){await Protocol.Json(stream,gate,shared.Process(message),life.Token);continue;}
   if(op is "imageBegin" or "imageChunk" or "imageEnd" or "imageCancel" or "imageSend" or "fileBegin" or "fileChunk" or "fileEnd" or "fileCancel" or "attachmentSend"){
    string request=message.Str("request");
    try{
     if(!Guid.TryParseExact(request,"D",out _))throw new IOException("잘못된 사진 요청입니다.");
     object? data=null;string resultMessage="";
     if(op is "imageSend" or "attachmentSend"){
      ConsoleBridge selected;
      lock(sync)selected=bridge??throw new IOException("터미널을 먼저 선택하세요.");
      if(selected.Target.Id!=message.Str("id")||!selected.Target.Valid||!allowed().Any(w=>w.Handle==selected.Target.Window.Handle&&w.Pid==selected.Target.Window.Pid))throw new IOException("선택한 터미널이 바뀌었습니다.");
      string[] paths=op=="imageSend"||message.TryGetProperty("images",out var imageIds)&&imageIds.GetArrayLength()>0?images.Paths(message):Array.Empty<string>();
      var documents=op=="attachmentSend"&&message.TryGetProperty("files",out var fileIds)&&fileIds.GetArrayLength()>0?files.Paths(message):Array.Empty<FileUploads.Attachment>();
      if(paths.Length+documents.Length is <1 or >4)throw new IOException("사진과 파일은 합쳐서 4개까지 보낼 수 있습니다.");
      if(notificationSent.Count>10000||!notificationSent.TryAdd(fingerprint+":"+request,0))throw new IOException("이미 처리한 전송입니다. 대화에서 확인하세요.");
      // Retain before injection: on an uncertain acknowledgement, the composer may still own these files.
      if(paths.Length>0)images.Retain(message);if(documents.Length>0)files.Retain(message);
      bool remote=message.Str("thread").StartsWith("remote:",StringComparison.Ordinal);
      JsonElement input;
      if(remote){
       if(documents.Length>0)throw new IOException("SSH 원격에서는 사진만 첨부할 수 있습니다.");
       var upload=await Task.Run(()=>RemoteImages.Transfer(selected,message.Str("thread"),paths,message.Str("text"),life.Token),life.Token);
       life.Token.ThrowIfCancellationRequested();
       lock(sync)if(bridge!=selected)throw new IOException("선택한 터미널이 바뀌었습니다.");
       if(!identity.IsTrusted(fingerprint)||!selected.Target.Valid||!allowed().Any(w=>w.Handle==selected.Target.Window.Handle&&w.Pid==selected.Target.Window.Pid))throw new IOException("첨부 전송 권한 또는 터미널이 바뀌었습니다.");
       input=JsonSerializer.SerializeToElement(new{op="remoteImageInput",request,upload,text=message.Str("text"),thread=message.Str("thread")});
      }else input=JsonSerializer.SerializeToElement(new{op="imageInput",request,paths,files=documents,text=message.Str("text"),thread=message.Str("thread")});
      var result=await selected.Notification(input,life.Token,remote?TimeSpan.FromSeconds(45):null);
      if(paths.Length>0)images.Complete(message);if(documents.Length>0)files.Complete(message);
      if(!result.Bool("ok"))throw new IOException(result.Str("message"));
      resultMessage=result.Str("message");
     }else data=op.StartsWith("file",StringComparison.Ordinal)?files.Process(message):images.Process(message);
     await Protocol.Json(stream,gate,new{type="imageResult",request,ok=true,data,message=resultMessage},life.Token);
    }catch(Exception e)when(e is IOException or TimeoutException or JsonException or InvalidOperationException or OverflowException or UnauthorizedAccessException){
     await Protocol.Json(stream,gate,new{type="imageResult",request,ok=false,message=e is TimeoutException?"전송 결과를 확인하지 못했습니다. 대화와 터미널 입력칸을 확인하세요.":e.Message},life.Token);
    }
    continue;
   }
   if(op=="watchReplies"||op=="unwatchReplies"){
    string id=message.Str("id");
    if(eventMode){if(op=="watchReplies"){var target=targets.FirstOrDefault(t=>t.Id==id&&t.Valid&&allowed().Any(w=>w.Handle==t.Window.Handle&&w.Pid==t.Window.Pid));if(target!=null)hub!.Track(target);}continue;}
    lock(sync){
     if(op=="unwatchReplies"){if(watches.Remove(id,out var old))old.Dispose();}
     else if(!watches.ContainsKey(id)&&watches.Count<32){
      var target=targets.FirstOrDefault(t=>t.Id==id&&t.Valid&&allowed().Any(w=>w.Handle==t.Window.Handle&&w.Pid==t.Window.Pid));
      if(target!=null)watches[id]=new ReplyWatch(target);
     }
    }
    continue;
   }
   if(op=="notificationInput"){
    string request=message.Str("request"),id=message.Str("id");
    try{
     if(!Guid.TryParseExact(request,"D",out _)||notificationSent.Count>10000)throw new IOException("알림을 다시 열어 주세요.");
     var target=targets.FirstOrDefault(t=>t.Id==id&&t.Valid&&allowed().Any(w=>w.Handle==t.Window.Handle&&w.Pid==t.Window.Pid))??throw new IOException("해당 터미널이 닫혔거나 공유가 해제되었습니다.");
     if(!notificationSent.TryAdd(fingerprint+":"+request,0))throw new IOException("이미 처리한 답장입니다. 앱에서 결과를 확인하세요.");
     using var input=new ConsoleBridge(target);
     var result=await input.Notification(message,life.Token,TimeSpan.FromSeconds(50));
     await Protocol.Json(stream,gate,new{type="notificationResult",request,ok=result.Bool("ok"),message=result.Str("message")},life.Token);
    }catch(Exception e)when(e is IOException or TimeoutException){await Protocol.Json(stream,gate,new{type="notificationResult",request,ok=false,message=e is TimeoutException?"전송 결과를 확인하지 못했습니다. 앱에서 확인하세요.":e.Message},life.Token);}
    continue;
   }
   if(op=="createTerminal"){
    string request=message.Str("request");
    try{
     if(!Guid.TryParseExact(request,"D",out _))throw new IOException("잘못된 추가 요청입니다.");
     if(created.TryGetValue(request,out var existing)){
      await List(existing);await Protocol.Json(stream,gate,new{type="terminalCreated",request,id=existing},life.Token);continue;
     }
     if(Environment.TickCount64-lastCreate<2000)throw new IOException("터미널 추가가 끝난 뒤 다시 눌러주세요.");
     var source=targets.FirstOrDefault(t=>t.Id==message.Str("source")&&t.Valid);
     if(source==null)throw new IOException("공유 중인 터미널을 먼저 선택하세요.");
     lastCreate=Environment.TickCount64;
     var added=await NewTerminal.Open(source,allowed,life.Token);
     if(created.Count>=64)created.Clear();created[request]=added.Id;
     await List(added.Id);
     await Protocol.Json(stream,gate,new{type="terminalCreated",request,id=added.Id},life.Token);
    }catch(IOException e){await Protocol.Json(stream,gate,new{type="terminalCreateError",request,message=e.Message},life.Token);}
    continue;
   }
   if(op=="closeTerminal"){
    string request=message.Str("request"),id=message.Str("id");
    try{
     if(!Guid.TryParseExact(request,"D",out _)||!message.Bool("confirmed"))throw new IOException("탭 닫기 확인이 필요합니다.");
     if(closed.TryGetValue(request,out var done)){
      if(done!=id)throw new IOException("닫기 요청의 대상이 변경되었습니다.");
      await List();await Protocol.Json(stream,gate,new{type="terminalClosed",request,id},life.Token);continue;
     }
     ConsoleTarget target;
     lock(sync){
      target=targets.FirstOrDefault(t=>t.Id==id&&t.Valid)??throw new IOException("닫을 탭이 이미 닫혔거나 변경되었습니다.");
      if(bridge?.Target.Id!=id)throw new IOException("선택한 탭만 닫을 수 있습니다.");
      closingId=id;
     }
     bool Authorized()=>identity.IsTrusted(fingerprint)&&allowed().Any(w=>w.Handle==target.Window.Handle&&w.Pid==target.Window.Pid);
     await CloseTerminal.Close(target,Authorized,life.Token);
     if(closed.Count>=64)closed.Clear();closed[request]=id;
     lock(sync){if(bridge?.Target.Id==id){Release(bridge);bridge=null;}}
     await List();
     await Protocol.Json(stream,gate,new{type="terminalClosed",request,id},life.Token);
    }catch(IOException e){
     await Protocol.Json(stream,gate,new{type="terminalCloseError",request,id,message=e.Message},life.Token);
    }finally{lock(sync)closingId="";}
    continue;
   }
   if(op=="conversationRetry"){
    string result;
    lock(sync){
     if(bridge==null||bridge.Target.Id!=message.Str("id")||!bridge.Target.Valid||!allowed().Any(w=>w.Handle==bridge.Target.Window.Handle&&w.Pid==bridge.Target.Window.Pid))result="선택한 터미널을 확인한 뒤 다시 시도하세요.";
     else if(Environment.TickCount64-lastRetry<2000)result="대화 기록을 확인하고 있습니다.";
     else{lastRetry=Environment.TickCount64;if(eventMode)selectedTerminal?.Retry();else Interlocked.Increment(ref retryGeneration);Interlocked.Increment(ref viewGeneration);result="대화 기록을 다시 확인합니다. 읽기 상태는 대화 화면에 표시됩니다.";}
    }
    await Protocol.Json(stream,gate,new{type="status",message=result},life.Token);continue;
   }
   if(op=="select"){
    var target=targets.FirstOrDefault(t=>t.Id==message.Str("id")&&t.Valid&&allowed().Any(w=>w.Handle==t.Window.Handle&&w.Pid==t.Window.Pid));if(target==null)throw new IOException("공유가 허용된 실행 중인 터미널만 선택할 수 있습니다.");
    lock(sync){if(bridge?.Target.Id!=target.Id){Release(bridge);if(eventMode){selectedTerminal=hub!.Track(target);bridge=selectedTerminal.Bridge;}else bridge=new(target);}}
    await Protocol.Json(stream,gate,new{type="selected",title=target.Name,view="text"},life.Token);continue;
   }
   try{
    lock(sync){if(bridge==null||!bridge.Target.Valid||!allowed().Any(w=>w.Handle==bridge.Target.Window.Handle&&w.Pid==bridge.Target.Window.Pid))throw new IOException("터미널을 먼저 선택하세요.");if(op is not ("text" or "key"))throw new IOException("텍스트 모드에서 지원하지 않는 요청입니다.");bridge.Send(message);}
   }catch(IOException e){await Protocol.Json(stream,gate,new{type="status",message=e.Message},life.Token);}
  }}finally{life.Cancel();lock(sync){Release(bridge);bridge=null;foreach(var watch in watches.Values)watch.Dispose();watches.Clear();}try{await sender;}catch{}}
 }
}