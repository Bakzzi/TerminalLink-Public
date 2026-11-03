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
   using var server=new RemoteServer(id,()=>new(),(_,_)=>Task.FromResult(true),m=>lines.Add("SERVER "+m));
   server.Start(0,IPAddress.Loopback);
   async Task<bool> Connect(X509Certificate2? certificate,string? token=null,bool wrongPin=false){
    try{
     using var tcp=new TcpClient();await tcp.ConnectAsync(IPAddress.Loopback,server.Port);
     using var ssl=new SslStream(tcp.GetStream(),false,(_,remote,_,_)=>!wrongPin&&remote!=null&&Identity.Hash(remote)==id.Fingerprint);
     using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(5));
     await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="TerminalLink",EnabledSslProtocols=SslProtocols.Tls12,ClientCertificates=certificate==null?new():new X509CertificateCollection{certificate},CertificateRevocationCheckMode=X509RevocationMode.NoCheck},deadline.Token);
     using var gate=new SemaphoreSlim(1);
     await Protocol.Json(ssl,gate,new{op="hello",token=token??"",name="SelfTest"},deadline.Token);
     for(int i=0;i<3;i++){var msg=await Protocol.ReadJson(ssl,deadline.Token);if(msg.Str("type")=="ready")return true;}
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
   Check(!await Connect(cert,wrongPin:true),"Incorrect server certificate pin rejected");
   id.Revoke();server.Disconnect();
   Check(!await Connect(cert),"Revoked certificate rejected");
   using var bytes=new MemoryStream(new byte[]{0x7f,0xff,0xff,0xff,1});
   bool rejected=false;try{await Protocol.ReadJson(bytes,CancellationToken.None);}catch(IOException){rejected=true;}
   Check(rejected,"Oversized protocol message rejected before allocation");
   Check(File.ReadAllBytes(Path.Combine(directory,"identity.dpapi"))[0]!=0x30,"Stored PC private key protected by DPAPI");
  }catch(Exception e){lines.Add("FAIL "+e);failures++;}
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"self-test.txt"),lines);
  Environment.ExitCode=failures==0?0:1;
 }

}
