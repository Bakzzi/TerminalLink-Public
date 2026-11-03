using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
namespace TerminalLink;
internal sealed class RemoteServer : IDisposable {
 readonly Identity identity;
 readonly Func<List<TerminalWindow>> allowed;
 readonly Func<string,string,Task<bool>> approve;
 readonly Action<string> log;
 readonly EventHub events;
 readonly Requests.NativeRequestService requests;
 readonly PushDelivery push;
 readonly System.Threading.Timer fileCleanup;readonly SharedFiles sharedFiles;
 readonly SemaphoreSlim capacity=new(4,4);
 readonly object connectionsLock=new();
 readonly HashSet<TcpClient> clients=new();
 CancellationTokenSource? life;
 TcpListener? listener;
 TcpClient? active;
 int failedPairings;
 bool updating;
 internal bool UpdateIdle {get{lock(connectionsLock)return clients.Count==0&&!requests.Busy;}}
 internal void PauseForUpdate(){lock(connectionsLock){if(clients.Count!=0||!requests.PauseForUpdate())throw new IOException("연결 또는 답변 전송이 끝나기를 기다립니다.");updating=true;listener?.Stop();}}
 internal int Port=>((IPEndPoint)listener!.LocalEndpoint).Port;
 internal RemoteServer(Identity identity,Func<List<TerminalWindow>> allowed,Func<string,string,Task<bool>> approve,Action<string> log,SharedFiles? sharedFiles=null,string? isolatedRequests=null){
  this.identity=identity;this.allowed=allowed;this.approve=approve;this.log=log;events=new EventHub(allowed);requests=new(events,allowed,isolatedRequests);events.RequestObservation=requests.Observe;push=new PushDelivery(identity,events);
  this.sharedFiles=sharedFiles??new SharedFiles();
  fileCleanup=new System.Threading.Timer(_=>this.sharedFiles.Cleanup(),null,TimeSpan.Zero,TimeSpan.FromMinutes(1));
 }
 internal void Start(int port,IPAddress? bind=null){
  life=new();listener=new TcpListener(bind??IPAddress.Any,port);listener.Start(8);_=Task.Run(()=>Accept(life.Token));log("연결 대기 중 · TCP "+Port);
 }
 async Task Accept(CancellationToken token){
  while(!token.IsCancellationRequested)try{
   var client=await listener!.AcceptTcpClientAsync(token);
   if(!capacity.Wait(0)){client.Dispose();continue;}
   lock(connectionsLock){if(updating){client.Dispose();capacity.Release();break;}clients.Add(client);}
   _=Handle(client,token);
  }catch(OperationCanceledException){break;}catch(Exception e){if(updating)break;if(!token.IsCancellationRequested)log("연결 오류: "+e.Message);}
 }
 async Task Handle(TcpClient client,CancellationToken shutdown){
  using var linked=CancellationTokenSource.CreateLinkedTokenSource(shutdown);
  using var gate=new SemaphoreSlim(1,1);
  WindowCapture? capture=null;
  TerminalWindow? selected=null;
  Task? frames=null;
  object selectionLock=new(); bool textMode=false,updateMode=false,eventMode=false,chunks=false;
  try{
   client.NoDelay=true;
   using var stream=new SslStream(client.GetStream(),false,(_,cert,_,_)=>identity.AcceptsCertificate(cert));
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(shutdown)){
    deadline.CancelAfter(TimeSpan.FromSeconds(12));
    await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{
     ServerCertificate=identity.Certificate,ClientCertificateRequired=true,
     EnabledSslProtocols=Environment.OSVersion.Version.Build>=22000?SslProtocols.Tls12|SslProtocols.Tls13:SslProtocols.Tls12,
     CertificateRevocationCheckMode=X509RevocationMode.NoCheck,AllowRenegotiation=false,EncryptionPolicy=EncryptionPolicy.RequireEncryption
    },deadline.Token);
   }
   if(stream.RemoteCertificate==null)throw new AuthenticationException("기기 인증서가 없습니다.");
   string fingerprint=Identity.Hash(stream.RemoteCertificate);
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(shutdown)){
    deadline.CancelAfter(TimeSpan.FromSeconds(60));
    var hello=await Protocol.ReadJson(stream,deadline.Token);
    textMode=hello.Str("view")=="text";updateMode=hello.Num("updates")==1;eventMode=textMode&&hello.Num("events")==1;
    chunks=hello.Num("conversationChunks")==1;
     if(hello.Str("op")!="hello")throw new AuthenticationException("Invalid hello");
    if(!identity.IsTrusted(fingerprint)){
     string token=hello.Str("token"),name=hello.Str("name","Android");
     if(name.Length>80||!identity.CheckPairToken(token)){
      if(Interlocked.Increment(ref failedPairings)>=5)identity.ClosePairing();
      throw new AuthenticationException("등록 코드가 만료되었거나 일치하지 않습니다.");
     }
     await Protocol.Json(stream,gate,new{type="status",message="PC에서 새 휴대폰 등록을 승인해 주세요."},deadline.Token);
     bool approved=await approve(name,fingerprint).WaitAsync(deadline.Token);
     if(!approved||!identity.Register(fingerprint,name,token))throw new AuthenticationException("등록이 승인되지 않았습니다.");
     log("휴대폰 등록 완료: "+name);
    }
   }
   if(!identity.IsTrusted(fingerprint))throw new AuthenticationException("접속 권한이 해제되었습니다.");
   lock(connectionsLock){active?.Dispose();active=client;push.Connected(true);}
   log("휴대폰 연결됨 · "+stream.SslProtocol+" · "+stream.NegotiatedCipherSuite);
   await Protocol.Json(stream,gate,new{type="ready",protocol=2,events=1,push=push.Configured?1:0,heartbeatSeconds=45,updates=1,createTerminal=1,closeTerminal=1,replyWatch=1,requestWatch=1,requestLifecycle=1,notificationReply=1,nativeNotificationReply=1,messageSubmit=1,nativeMessageSubmit=1,startMessage=1,imageUpload=1,remoteImageUpload=1,localImageUpload=1,remoteFileUpload=1,localFileUpload=1,fileUpload=1,sharedFiles=1,sharedUpload=1,message="인증 완료"},linked.Token);
   if(textMode){await TextRemote.Run(stream,gate,identity,fingerprint,allowed,linked,updateMode,events,eventMode,push,sharedFiles,chunks,requests);return;}
   async Task WindowsList()=>await Protocol.Json(stream,gate,new{type="windows",windows=allowed().Where(Native.Valid).Select(w=>new{id=w.Handle.ToString(),title=w.Title})},linked.Token);
   await WindowsList();
   frames=Task.Run(async()=>{
    WindowCapture? previousCapture=null; long last=0;string lastState="";long heartbeat=0;
    try{while(!linked.IsCancellationRequested){
     if(!identity.IsTrusted(fingerprint))throw new AuthenticationException("권한 해제");
     WindowCapture? cap;TerminalWindow? win;
     lock(selectionLock){cap=capture;win=selected;}
     if(!ReferenceEquals(previousCapture,cap)){last=0;previousCapture=cap;}
string state="";
     if(win!=null){
      if(!Native.Valid(win)||!allowed().Any(w=>w.Handle==win.Handle&&w.Pid==win.Pid)){
       state="터미널이 닫혔거나 PC에서 공유를 해제했습니다.";
       lock(selectionLock){capture?.Dispose();capture=null;selected=null;}
       await Protocol.Json(stream,gate,new{type="closed",message=state},linked.Token);
      }else if(Native.IsIconic(new(win.Handle))||!Native.DesktopAvailable()){
       state="PC 창이 최소화되었거나 화면이 잠겨 있습니다.";
       await Protocol.Json(stream,gate,new{type="paused",message=state},linked.Token);
      }else if(cap?.Latest is Frame f&&f.Version!=last){
       await Protocol.Send(stream,gate,2,f.Png,linked.Token);last=f.Version;
      }else if(cap?.Error!=null)state=cap.Error;
     }
     if(state!=""&&state!=lastState)await Protocol.Json(stream,gate,new{type="status",message=state},linked.Token);
     lastState=state;
     if(Environment.TickCount64-heartbeat>4000){await Protocol.Json(stream,gate,new{type="ping"},linked.Token);heartbeat=Environment.TickCount64;}
     await Task.Delay(150,linked.Token);
    }}catch{linked.Cancel();client.Dispose();}
   },linked.Token);
   while(!linked.IsCancellationRequested){
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(linked.Token);timeout.CancelAfter(TimeSpan.FromSeconds(30));
    var msg=await Protocol.ReadJson(stream,timeout.Token);
    if(!identity.IsTrusted(fingerprint))throw new AuthenticationException("권한 해제");
    string op=msg.Str("op");
    if(op=="ping")continue;
    if(op=="list"){await WindowsList();continue;}
    if(op=="select"){
     if(!long.TryParse(msg.Str("id"),out long handle))throw new IOException("Invalid window");
     var win=allowed().FirstOrDefault(w=>w.Handle==handle&&Native.Valid(w));
     if(win==null)throw new IOException("PC에서 공유를 허용한 터미널만 선택할 수 있습니다.");
     lock(selectionLock){capture?.Dispose();capture=null;selected=null;capture=new WindowCapture(new(win.Handle));selected=win;}
     await Protocol.Json(stream,gate,new{type="selected",title=win.Title},linked.Token);
     continue;
    }
    TerminalWindow? target;lock(selectionLock)target=selected;
    if(target==null||!allowed().Any(w=>w.Handle==target.Handle&&w.Pid==target.Pid)||!Native.Valid(target))throw new IOException("공유할 터미널을 먼저 선택하세요.");
    try{
     if(op=="restore")Native.Restore(target);
else if(op=="text")Native.Text(target,msg.Str("text"));
     else if(op=="key")Native.Press(target,msg.Str("key"),msg.Bool("ctrl"),msg.Bool("alt"),msg.Bool("shift"));
     else if(op=="drag")Native.Drag(target,msg.Num("sx"),msg.Num("sy"),msg.Num("x"),msg.Num("y"));
else if(op=="pointer")Native.Pointer(target,msg.Num("x"),msg.Num("y"),msg.Str("action"),(int)msg.Num("delta"));
     else throw new IOException("지원하지 않는 요청");
    }catch(IOException e){await Protocol.Json(stream,gate,new{type="status",message=e.Message},linked.Token);}
   }
  }catch(Exception e){if(!shutdown.IsCancellationRequested&&e is not EndOfStreamException)log("연결 종료: "+e.Message);}
  finally{
   linked.Cancel();client.Dispose();
   if(frames!=null)try{await frames;}catch{}
   lock(selectionLock)capture?.Dispose();
   lock(connectionsLock){clients.Remove(client);if(active==client){active=null;push.Connected(false);}}
   capacity.Release();
  }
 }
 internal void Disconnect(){lock(connectionsLock){foreach(var client in clients)client.Dispose();active=null;push.Connected(false);}}
 public void Dispose(){fileCleanup.Dispose();life?.Cancel();listener?.Stop();Disconnect();push.Dispose();events.Dispose();requests.Dispose();RemoteCodexFeed.Stop();identity.ClosePairing();life?.Dispose();}
}
