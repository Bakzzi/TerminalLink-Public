using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
namespace TerminalLink;
internal static class SelfTest {
 internal static async Task Run(){
  var lines=new List<string>();int failures=0;
  void Check(bool value,string name){lines.Add((value?"PASS ":"FAIL ")+name);if(!value)failures++;}
  string directory=Path.Combine(AppContext.BaseDirectory,"test-identity-"+Guid.NewGuid().ToString("N"));
  try{
   using var id=new Identity(directory);
   using var clientKey=ECDsa.Create(ECCurve.NamedCurves.nistP256);
   var req=new CertificateRequest("CN=Test Android",clientKey,HashAlgorithmName.SHA256);
   req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature,true));
   using var ephemeral=req.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1),DateTimeOffset.UtcNow.AddDays(1)); using var cert=X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.UserKeySet);
   var shared=new SharedFiles(Path.Combine(directory,"shared"));
   using var server=new RemoteServer(id,()=>new(),(_,_)=>Task.FromResult(true),m=>lines.Add("SERVER "+m),sharedFiles:shared,isolatedRequests:Path.Combine(directory,"requests"));
   server.Start(0,IPAddress.Loopback);
   Check(server.UpdateIdle,"Update is idle before any mobile connection");bool guarded=false;
   async Task<bool> Connect(X509Certificate2? certificate,string? token=null,bool wrongPin=false){
    try{
     using var tcp=new TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,server.Port);
     using var ssl=new SslStream(tcp.GetStream(),false,(_,remote,_,_)=>!wrongPin&&remote!=null&&Identity.Hash(remote)==id.Fingerprint);
     using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
     await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="TerminalLink",EnabledSslProtocols=SslProtocols.Tls12,ClientCertificates=certificate==null?new():new X509CertificateCollection{certificate},CertificateRevocationCheckMode=X509RevocationMode.NoCheck},deadline.Token);
     using var gate=new SemaphoreSlim(1);
     await Protocol.Json(ssl,gate,new{op="hello",token=token??"",name="SelfTest"},deadline.Token);
     for(int i=0;i<3;i++){var msg=await Protocol.ReadJson(ssl,deadline.Token);if(msg.Str("type")=="ready"){if(!guarded){guarded=true;Check(!server.UpdateIdle,"Active authenticated mobile connection defers replacement");bool refused=false;try{server.PauseForUpdate();}catch(IOException){refused=true;}Check(refused,"Update cannot stop a connected phone");}return true;}}
    }catch(Exception e){lines.Add("CLIENT "+e.Message);}
    return false;
   }
   Check(!await Connect(cert),"Unknown certificate rejected with pairing closed");
   Check(!await Connect(null),"Missing client certificate rejected");
   string code=id.OpenPairing("127.0.0.1",server.Port);
   Check(code.StartsWith("tr1:"),"Pairing export generated");
   Check(!await Connect(cert,new string('0',64)),"Incorrect enrollment token rejected");
   string token=id.PairToken!;id.PairUntil=DateTime.UtcNow.AddSeconds(-1);Check(!await Connect(cert,token),"Expired enrollment rejected");id.OpenPairing("127.0.0.1",server.Port);token=id.PairToken!;
   Check(await Connect(cert,token),"First pairing: mTLS + token + PC approval");
   Check(id.IsTrusted(Identity.Hash(cert))&&!id.PairingOpen,"Only approved certificate saved and enrollment closed");
   Check(await Connect(cert),"Known certificate reconnects without token or password");
   // The file box uses an isolated identity and loopback server, never the user's files or terminal.
   using(var tcp=new TcpClient()){
    await tcp.ConnectAsync(IPAddress.Loopback,server.Port);
    using var ssl=new SslStream(tcp.GetStream(),false,(_,remote,_,_)=>remote!=null&&Identity.Hash(remote)==id.Fingerprint);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));using var gate=new SemaphoreSlim(1);
    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="TerminalLink",EnabledSslProtocols=SslProtocols.Tls12,ClientCertificates=new X509CertificateCollection{cert},CertificateRevocationCheckMode=X509RevocationMode.NoCheck},timeout.Token);
    await Protocol.Json(ssl,gate,new{op="hello",view="text",name="FileBoxFixture"},timeout.Token);
    async Task<JsonElement> Read(string kind,string? request=null){while(true){var x=await Protocol.ReadJson(ssl,timeout.Token);if(x.Str("type")==kind&&(request==null||x.Str("request")==request))return x;}}
    var ready=await Read("ready");Check(ready.Num("sharedUpload")==1,"File box upload is advertised after authenticated handshake");
    var requestEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requestRelease=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int writes=0;
    TextRemote.RequestHandlerFixture=async (message,cancellation)=>{
     if(message.Str("op")=="notificationInput"){Interlocked.Increment(ref writes);requestEntered.TrySetResult();await requestRelease.Task.WaitAsync(cancellation);}
     return JsonSerializer.SerializeToElement(new{type="notificationResult",request=message.Str("request"),ok=false,delivery="Dispatching"});
    };
    try{
     string sendId=Guid.NewGuid().ToString("D"),statusId=Guid.NewGuid().ToString("D");
     await Protocol.Json(ssl,gate,new{op="notificationInput",kind="request",requestKey="fixture:0",request=sendId},timeout.Token);
     await requestEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
     await Protocol.Json(ssl,gate,new{op="requestStatus",request=statusId},timeout.Token);
     var status=await Read("notificationResult",statusId).WaitAsync(TimeSpan.FromSeconds(3));
     Check(status.Str("delivery")=="Dispatching"&&writes==1,"Authenticated status responds while request delivery is pending without a second write");
     requestRelease.TrySetResult();await Read("notificationResult",sendId).WaitAsync(TimeSpan.FromSeconds(3));
    }finally{requestRelease.TrySetResult();TextRemote.RequestHandlerFixture=null;}
    byte[] body=System.Text.Encoding.UTF8.GetBytes("Synthetic phone file\n한글 😀");string digest=Convert.ToHexString(SHA256.HashData(body)),upload=Guid.NewGuid().ToString("D");
    async Task<JsonElement> Upload(string op,string request,object value){await Protocol.Json(ssl,gate,value,timeout.Token);return await Read("sharedUploadResult",request);}
    string r=Guid.NewGuid().ToString("D");var begin=await Upload("sharedUploadBegin",r,new{op="sharedUploadBegin",request=r,upload,name="phone.png",size=body.Length,sha256=digest});
    r=Guid.NewGuid().ToString("D");var chunk=await Upload("sharedUploadChunk",r,new{op="sharedUploadChunk",request=r,upload,offset=0,data=Convert.ToBase64String(body)});
    r=Guid.NewGuid().ToString("D");var end=await Upload("sharedUploadEnd",r,new{op="sharedUploadEnd",request=r,upload});string file=end.GetProperty("file").Str("id");
    Check(begin.Bool("ok")&&chunk.Bool("ok")&&end.Bool("ok")&&shared.Find(file)?.sha256==digest,"Authenticated mobile upload stores verified bytes without terminal input");
    r=Guid.NewGuid().ToString("D");await Protocol.Json(ssl,gate,new{op="sharedList",request=r},timeout.Token);var list=await Read("sharedList",r);
    Check(list.GetProperty("files").EnumerateArray().Any(x=>x.Str("id")==file),"Uploaded file appears in existing file box list");
    string dl=Guid.NewGuid().ToString("D");await Protocol.Json(ssl,gate,new{op="sharedOpen",request=dl,file},timeout.Token);await Read("sharedStart",dl);
    await Protocol.Json(ssl,gate,new{op="sharedRead",request=dl,offset=0},timeout.Token);var received=await Read("sharedChunk",dl);
    await Protocol.Json(ssl,gate,new{op="sharedRead",request=dl,offset=body.Length},timeout.Token);var final=await Read("sharedEnd",dl);
    Check(Convert.FromBase64String(received.Str("data")).SequenceEqual(body)&&final.Str("sha256")==digest,"Existing authenticated download returns the exact uploaded bytes");
   }
   Check(!await Connect(cert,wrongPin:true),"Incorrect server certificate pin rejected");
   id.Revoke();server.Disconnect();
   Check(!await Connect(cert),"Revoked certificate rejected");
   using var bytes=new MemoryStream(new byte[]{0x7f,0xff,0xff,0xff,1});
   bool rejected=false;try{await Protocol.ReadJson(bytes,CancellationToken.None);}catch(IOException){rejected=true;}
   Check(rejected,"Oversized protocol message rejected before allocation");
   Check(File.ReadAllBytes(Path.Combine(directory,"identity.dpapi"))[0]!=0x30,"Stored PC private key protected by DPAPI");
   for(int n=0;n<40&&!server.UpdateIdle;n++)await Task.Delay(50);Check(server.UpdateIdle,"Closed mobile connections release update gate");server.PauseForUpdate();Check(server.UpdateIdle,"Idle update stops accepting without terminal input");
  }catch(Exception e){lines.Add("FAIL "+e);failures++;}
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"self-test.txt"),lines);
  Environment.ExitCode=failures==0?0:1;
 }

}
