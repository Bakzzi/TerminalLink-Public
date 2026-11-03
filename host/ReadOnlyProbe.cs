#if TESTING
using System.Text.Json;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace TerminalLink;
// Explicit developer-only probe. No input, session restart, or production binding writes.
internal static class ReadOnlyProbe {
 internal static void Attachment(string folder,string thread){
  Environment.SetEnvironmentVariable("TERMINALLINK_TEST_BINDING_STORE",Path.Combine(folder,"isolated-bindings"));
  var frame=JsonSerializer.Deserialize<ConsoleFrame>(File.ReadAllText(Path.Combine(folder,"frame.json")))!;
  var results=new List<object>();
  for(int attempt=1;attempt<=2;attempt++){
   var clock=Stopwatch.StartNew();string error="";bool connected=false;
   try{LocalImageTarget.Connect(()=>frame,thread);connected=true;}catch(Exception e){error=e.Message;}
   results.Add(new{attempt,connected,error,elapsedMs=clock.ElapsedMilliseconds,diagnostic=LocalDaemonFeed.Diagnostic,inputsSent=0});
  }
  File.WriteAllText(Path.Combine(folder,"attachment-read-result.json"),JsonSerializer.Serialize(results));
 }
 internal static async Task Delivery(string folder){
  Directory.CreateDirectory(folder);
  Environment.SetEnvironmentVariable("TERMINALLINK_TEST_BINDING_STORE",Path.Combine(folder,"isolated-bindings"));
  RemoteCodexFeed.BindingCacheFile=Path.Combine(folder,"ssh-bindings.dpapi");
  try{
   var clock=Stopwatch.StartNew();
   var windows=TerminalWindows.Read(true);
   using var identity=new Identity(Path.Combine(folder,"identity"));
   using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
   var request=new CertificateRequest("CN=Isolated read-only client",key,HashAlgorithmName.SHA256);
   using var ephemeral=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow.AddHours(1));
   using var certificate=X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.UserKeySet);
   identity.Config.PeerFingerprint=Identity.Hash(certificate);
   using var server=new RemoteServer(identity,()=>windows,(_,_)=>Task.FromResult(false),_=>{},new SharedFiles(Path.Combine(folder,"shared")));
   server.Start(0,IPAddress.Loopback);
   using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,server.Port);
   using var stream=new SslStream(client.GetStream(),false,(_,cert,_,_)=>cert!=null&&Identity.Hash(cert)==identity.Fingerprint);
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(55));using var gate=new SemaphoreSlim(1,1);
   await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="TerminalLink",ClientCertificates=new(){certificate},EnabledSslProtocols=SslProtocols.Tls12,CertificateRevocationCheckMode=X509RevocationMode.NoCheck},deadline.Token);
   async Task Send(object value)=>await Protocol.Json(stream,gate,value,deadline.Token);
   using var chunks=new MemoryStream();string transfer="";int index=0;
   async Task<JsonElement> Read(){while(true){
    byte[] header=new byte[5];await stream.ReadExactlyAsync(header,deadline.Token);int length=BinaryPrimitives.ReadInt32BigEndian(header);
    if(length<1||length>Protocol.MaxFrame||header[4]!=1)throw new IOException("Invalid server frame");
    byte[] bytes=new byte[length-1];await stream.ReadExactlyAsync(bytes,deadline.Token);using var parsed=JsonDocument.Parse(bytes);var packet=parsed.RootElement;
    if(packet.Str("type")!="conversationChunk")return packet.Clone();
    if(packet.Num("index")==0){chunks.SetLength(0);transfer=packet.Str("transfer");index=0;}
    if(packet.Str("transfer")!=transfer||packet.Num("index")!=index++)throw new IOException("Out-of-order chunk");
    chunks.Write(Convert.FromBase64String(packet.Str("data")));
    if(packet.Bool("last")){byte[] data=chunks.ToArray();if(Convert.ToHexString(SHA256.HashData(data))!=packet.Str("sha256"))throw new IOException("Chunk digest mismatch");using var result=JsonDocument.Parse(data);return result.RootElement.Clone();}
   }}
   await Send(new{op="hello",view="text",events=1,conversationChunks=1});
   string[] ids=Array.Empty<string>();
   while(ids.Length==0){var packet=await Read();if(packet.Str("type")=="windows")ids=packet.GetProperty("windows").EnumerateArray().Select(w=>w.Str("id")).ToArray();}
   if(Environment.GetEnvironmentVariable("TERMINALLINK_TEST_PROBE_PIDS") is string selectedPids){
    var pids=selectedPids.Split(',').Select(int.Parse).ToHashSet();ids=ids.Where(id=>id.Split(':') is var parts&&parts.Length==3&&int.TryParse(parts[1],out int pid)&&pids.Contains(pid)).ToArray();
    if(ids.Length!=pids.Count)throw new IOException("Requested probe targets are missing");
   }
   long listMs=clock.ElapsedMilliseconds;
   foreach(string id in ids)await Send(new{op="watchReplies",id});
   await Send(new{op="viewState",visible=true});await Send(new{op="eventSync",epoch="",after=0});
   var results=new List<object>();
   foreach(string id in ids.Concat(ids.Reverse().Skip(1))){
    long selected=clock.ElapsedMilliseconds;
    await Send(new{op="select",id});
    while(true){var packet=await Read();
     if(packet.Str("type")!="terminalText"||packet.Str("id")!=id||!packet.TryGetProperty("conversation",out var conversation)||conversation.ValueKind!=JsonValueKind.Object||conversation.Str("readState")!="ready")continue;
     if(conversation.Bool("delta"))throw new IOException("Tab selection did not receive a full conversation");
     int count=conversation.GetProperty("messages").GetArrayLength();if(count==0)throw new IOException("Empty original conversation");
     results.Add(new{session=id,messages=count,source=conversation.Str("source"),ready=true,readyMs=clock.ElapsedMilliseconds-selected});
     File.WriteAllText(Path.Combine(folder,"delivery-progress.json"),JsonSerializer.Serialize(results));break;
    }
   }
   File.WriteAllText(Path.Combine(folder,"delivery-result.json"),JsonSerializer.Serialize(new{success=true,inputsSent=0,productionIdentityUsed=false,listMs,elapsedMs=clock.ElapsedMilliseconds,results}));
  }catch(Exception e){File.WriteAllText(Path.Combine(folder,"probe-error.txt"),e.ToString());Environment.ExitCode=1;}
 }
 internal static void Hub(string folder){
  Directory.CreateDirectory(folder);
  Environment.SetEnvironmentVariable("TERMINALLINK_TEST_BINDING_STORE",Path.Combine(folder,"isolated-bindings"));
  RemoteCodexFeed.BindingCacheFile=Path.Combine(folder,"ssh-bindings.dpapi");
  try{
   var windows=TerminalWindows.Read(true);var targets=ConsoleNative.Targets(windows);
   using var hub=new EventHub(()=>windows);
   var tracked=targets.Select(t=>(target:t,terminal:hub.Track(t))).ToArray();
   var samples=new List<object>();
   for(int i=0;i<30;i++){
    Thread.Sleep(1000);
    var access=Stopwatch.StartNew();long head=hub.Head;long eventAccessMs=access.ElapsedMilliseconds;
    samples.Add(new{second=i+1,eventAccessMs,diagnostic=LocalDaemonFeed.Diagnostic,states=tracked.Select(t=>new{pid=t.target.Pid,frame=t.terminal.Bridge.Latest!=null,state=t.terminal.Conversation?.readState,source=t.terminal.Conversation?.source,messages=t.terminal.Conversation?.messages.Length??0}).ToArray()});
   }
   File.WriteAllText(Path.Combine(folder,"hub-result.json"),JsonSerializer.Serialize(samples));
   foreach(var item in tracked)if(item.terminal.Bridge.Latest is ConsoleFrame f)File.WriteAllText(Path.Combine(folder,"frame-"+item.target.Pid+".json"),JsonSerializer.Serialize(f));
  }catch(Exception e){File.WriteAllText(Path.Combine(folder,"probe-error.txt"),e.ToString());Environment.ExitCode=1;}
 }
 internal static void Run(string folder,int pid){
  Directory.CreateDirectory(folder);
  Environment.SetEnvironmentVariable("TERMINALLINK_TEST_BINDING_STORE",Path.Combine(folder,"isolated-bindings"));
  RemoteCodexFeed.BindingCacheFile=Path.Combine(folder,"ssh-bindings.dpapi");
  try {
   string captured=Path.Combine(folder,"frame.json"),recordPath=Path.Combine(folder,"record-path.txt");
   if(File.Exists(captured)&&File.Exists(recordPath)){
    var frame=JsonSerializer.Deserialize<ConsoleFrame>(File.ReadAllText(captured))!;
    var original=CodexFeed.Parse(File.ReadAllText(recordPath).Trim());
    var candidate=new LocalDaemonFeed.Candidate("fixture",original);
    File.WriteAllText(Path.Combine(folder,"offline-result.json"),JsonSerializer.Serialize(new{
     compatible=LocalDaemonFeed.Compatible(frame,original),
     uniquelyMatched=LocalDaemonFeed.Match(frame,new[]{candidate})?.id==original.id,
     duplicateRejected=LocalDaemonFeed.Match(frame,new[]{candidate,candidate with{conversation=original with{id="duplicate"}}})==null,
     messages=original.messages.Length,inputsSent=0}));
   }
   if(pid==0&&File.Exists(captured)&&!File.Exists(recordPath)){
    var frame=JsonSerializer.Deserialize<ConsoleFrame>(File.ReadAllText(captured))!;Conversation? remote=null;
    for(int i=0;i<30;i++){remote=RemoteCodexFeed.Read(frame);if(ConversationReader.IsOriginal(remote))break;Thread.Sleep(400);}
    File.WriteAllText(Path.Combine(folder,"remote-frame-result.json"),JsonSerializer.Serialize(new{success=ConversationReader.IsOriginal(remote),messages=remote?.messages.Length??0,remoteDiagnostic=RemoteCodexFeed.Diagnostic,answerAnchorLengths=RemoteCodexFeed.AnswerAnchors(frame).Select(a=>a.Select(x=>x.Length).ToArray()).ToArray(),inputsSent=0}));
    RemoteCodexFeed.Stop();if(!ConversationReader.IsOriginal(remote))Environment.ExitCode=2;
   }
   if(pid<=0)return;
   var target=ConsoleNative.Targets(TerminalWindows.Read(true)).Single(t=>t.Pid==pid);
   using var bridge=new ConsoleBridge(target);var reader=new ConversationReader();Conversation? value=null;ConsoleFrame? latest=null;
   for(int i=0;i<30;i++){
    Thread.Sleep(400);latest=bridge.Latest;
    if(latest!=null){value=reader.Read(latest);if(ConversationReader.IsOriginal(value))break;}
   }
   if(latest!=null)File.WriteAllText(Path.Combine(folder,"live-frame.json"),JsonSerializer.Serialize(latest));
   File.WriteAllText(Path.Combine(folder,"live-result.json"),JsonSerializer.Serialize(new{
    success=ConversationReader.IsOriginal(value),messages=value?.messages.Length??0,requests=value?.requests?.Length??0,
    source=value?.source,readState=value?.readState,diagnostic=LocalDaemonFeed.Diagnostic,remoteDiagnostic=RemoteCodexFeed.Diagnostic,answerAnchorLengths=latest==null?Array.Empty<int[]>():RemoteCodexFeed.AnswerAnchors(latest).Select(a=>a.Select(x=>x.Length).ToArray()).ToArray(),inputsSent=0}));
   if(!ConversationReader.IsOriginal(value))Environment.ExitCode=2;
  }catch(Exception e){File.WriteAllText(Path.Combine(folder,"probe-error.txt"),e.ToString());Environment.ExitCode=1;}
 }
}
#endif
