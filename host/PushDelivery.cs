using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Threading.Channels;
namespace TerminalLink;

// Only encrypted notification previews leave the direct TLS connection.
internal sealed class PushDelivery : IDisposable {
 internal sealed record Registration(string Peer,string Token,string Key,string Platform,bool Confirmed=false);
 readonly Identity identity;
 readonly EventHub hub;
 readonly string file,credential;
 readonly HttpClient http=new(){Timeout=TimeSpan.FromSeconds(15)};
 readonly CancellationTokenSource life=new();
 readonly Channel<(Registration device,object payload,bool urgent)> queue=Channel.CreateBounded<(Registration,object,bool)>(new BoundedChannelOptions(128){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
 readonly object sync=new();
 readonly Task worker;
 Registration? device;
 string challenge="",accessToken="",project="";
 long challengeUntil,tokenUntil;
 bool connected;
 void Status(string status){try{File.WriteAllText(Path.Combine(identity.DirectoryPath,"push-status.json"),JsonSerializer.Serialize(new{time=DateTimeOffset.UtcNow,status}));}catch{}}
 internal bool Configured=>File.Exists(credential);
 internal PushDelivery(Identity identity,EventHub hub){
  this.identity=identity;this.hub=hub;file=Path.Combine(identity.DirectoryPath,"push-device.dpapi");credential=Path.Combine(identity.DirectoryPath,"push-service-account.dpapi");
  try{if(File.Exists(file))device=JsonSerializer.Deserialize<Registration>(ProtectedData.Unprotect(File.ReadAllBytes(file),null,DataProtectionScope.CurrentUser));}catch{}
  hub.Notification=OnEvent;hub.Monitor=()=>device is {Confirmed:true}&&identity.IsTrusted(device.Peer);worker=Task.Run(Work);
 }
 internal void Connected(bool active){lock(sync)connected=active;}
 internal bool Ready(string peer){lock(sync)return Configured&&device is {Confirmed:true}&&device.Peer==peer&&identity.IsTrusted(peer);}
 void Save(){if(device==null){if(File.Exists(file))File.Delete(file);return;}var bytes=ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(device),null,DataProtectionScope.CurrentUser);File.WriteAllBytes(file+".tmp",bytes);File.Move(file+".tmp",file,true);}
 internal object Register(string peer,JsonElement request){
  if(!Configured)return new{type="pushStatus",ready=false,message="PC 푸시 설정이 필요합니다."};
  string token=request.Str("token"),key=request.Str("key"),platform=request.Str("platform");
  if(!identity.IsTrusted(peer)||token.Length<20||token.Length>4096||platform is not ("android" or "ios"))throw new IOException("잘못된 푸시 등록입니다.");
  byte[] raw;try{raw=Convert.FromBase64String(key);}catch{throw new IOException("잘못된 푸시 키입니다.");}
  if(raw.Length!=32)throw new IOException("잘못된 푸시 키입니다.");CryptographicOperations.ZeroMemory(raw);
  lock(sync){
   if(device is {Confirmed:true}&&device.Peer==peer&&device.Token==token&&device.Key==key&&device.Platform==platform)return new{type="pushStatus",ready=true};
   device=new(peer,token,key,platform);challenge=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));challengeUntil=Environment.TickCount64+300000;Save();
   Enqueue(device,new{kind="probe",challenge,pc=identity.Fingerprint,expires=DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()},false);
   return new{type="pushStatus",ready=false,probing=true};
  }
 }
 internal object Confirm(string peer,string proof){lock(sync){bool ok=device?.Peer==peer&&identity.IsTrusted(peer)&&challenge.Length>0&&challenge==proof&&Environment.TickCount64<challengeUntil;if(ok){device=device! with{Confirmed=true};challenge="";Save();}return new{type="pushStatus",ready=ok||Ready(peer)};}}
 internal void Disable(string peer){lock(sync){if(device?.Peer==peer){device=null;challenge="";Save();}}}
 internal void Park(string peer,long after,string epoch){
  lock(sync){if(!Ready(peer))throw new IOException("푸시 수신 확인을 마친 뒤 절전 대기로 전환할 수 있습니다.");connected=false;}
  // Replay events not yet received before handing off the socket.
  foreach(var item in hub.Pending(epoch,after))OnEvent(item);
 }
 void Enqueue(Registration target,object payload,bool urgent){queue.Writer.TryWrite((target,payload,urgent));}
 internal static string AnswerTitle(string title)=>string.Concat(title.EnumerateRunes().Take(120).Select(r=>r.ToString()));
 void OnEvent(EventHub.Notice notice){
  Registration? target;lock(sync){target=device;if(connected||target==null||!target.Confirmed||!identity.IsTrusted(target.Peer))return;}
  long expires=DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds();
  foreach(var answer in notice.conversation.messages){
   string text=answer.text;while(Encoding.UTF8.GetByteCount(text)>1300)text=text[..Math.Max(0,text.Length-50)];if(text.Length<answer.text.Length)text+="…";
   Enqueue(target,new{kind="answer",pc=identity.Fingerprint,id=notice.id,thread=notice.conversation.id,repliedThrough=notice.repliedThrough,sequence=answer.sequence,title=AnswerTitle(notice.title),text,openOnly=!ConversationReader.IsOriginal(notice.conversation),nativeReply=true,expires},true);
  }
  if(notice.active&&hub.Stamp(notice.id)==notice.stamp){
   var prompt=notice.prompt;
   object payload=new{kind="request",pc=identity.Fingerprint,id=notice.id,thread=notice.conversation.id,repliedThrough=notice.repliedThrough,question=prompt?.question??"PC에서 응답을 기다리고 있어요.",choices=prompt?.choices??Array.Empty<string>(),followup=prompt?.followup??false,questionKind=prompt?.questionKind??false,requestKey=notice.requestKey,stamp=notice.stamp,openOnly=!ConversationReader.IsOriginal(notice.conversation),nativeReply=true,expires};
   if(JsonSerializer.SerializeToUtf8Bytes(payload,JsonOptions).Length>2700)payload=new{kind="request",pc=identity.Fingerprint,id=notice.id,thread=notice.conversation.id,repliedThrough=notice.repliedThrough,question="긴 응답 요청이 도착했어요. 앱에서 확인하세요.",openOnly=true,requestKey=notice.requestKey,stamp=notice.stamp,expires};
   Enqueue(target,payload,true);
  }else if(!notice.active&&hub.Stamp(notice.id)==notice.stamp)Enqueue(target,new{kind="clear",pc=identity.Fingerprint,id=notice.id,thread=notice.conversation.id,repliedThrough=notice.repliedThrough,stamp=notice.stamp,expires},false);
 }
 internal static readonly JsonSerializerOptions JsonOptions=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
 internal static string Encrypt(byte[] key,string pc,byte[] plain){
  byte[] nonce=RandomNumberGenerator.GetBytes(12),cipher=new byte[plain.Length],tag=new byte[16];
  using(var aes=new AesGcm(key,16))aes.Encrypt(nonce,plain,cipher,tag,Encoding.UTF8.GetBytes("TerminalLink push v1:"+pc));
  return Convert.ToBase64String(nonce.Concat(cipher).Concat(tag).ToArray());
 }
 async Task<string> Token(CancellationToken ct){
  if(accessToken.Length>0&&DateTimeOffset.UtcNow.ToUnixTimeSeconds()<tokenUntil)return accessToken;
  byte[] bytes=ProtectedData.Unprotect(await File.ReadAllBytesAsync(credential,ct),null,DataProtectionScope.CurrentUser);
  try{
   using var doc=JsonDocument.Parse(bytes);var c=doc.RootElement;project=c.GetProperty("project_id").GetString()!;
   if(!System.Text.RegularExpressions.Regex.IsMatch(project,@"^[a-z][a-z0-9-]{4,62}$"))throw new IOException("Invalid project");
   long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
   static string B64(byte[] b)=>Convert.ToBase64String(b).TrimEnd('=').Replace('+','-').Replace('/','_');
   string header=B64(JsonSerializer.SerializeToUtf8Bytes(new{alg="RS256",typ="JWT"}));
   string claims=B64(JsonSerializer.SerializeToUtf8Bytes(new{iss=c.GetProperty("client_email").GetString(),scope="https://www.googleapis.com/auth/firebase.messaging",aud="https://oauth2.googleapis.com/token",iat=now,exp=now+3600}));
   using var rsa=RSA.Create();rsa.ImportFromPem(c.GetProperty("private_key").GetString());
   string unsigned=header+"."+claims,jwt=unsigned+"."+B64(rsa.SignData(Encoding.ASCII.GetBytes(unsigned),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
   using var response=await http.PostAsync("https://oauth2.googleapis.com/token",new FormUrlEncodedContent(new Dictionary<string,string>{{"grant_type","urn:ietf:params:oauth:grant-type:jwt-bearer"},{"assertion",jwt}}),ct);
   if(!response.IsSuccessStatusCode)Status("OAuth HTTP "+(int)response.StatusCode);response.EnsureSuccessStatusCode();using var token=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
   accessToken=token.RootElement.GetProperty("access_token").GetString()!;tokenUntil=now+token.RootElement.GetProperty("expires_in").GetInt32()-120;return accessToken;
  }finally{CryptographicOperations.ZeroMemory(bytes);}
 }
 async Task Work(){
  try{await foreach(var item in queue.Reader.ReadAllAsync(life.Token)){
   long expires=JsonSerializer.SerializeToElement(item.payload,JsonOptions).GetProperty("expires").GetInt64();
   for(int attempt=0;DateTimeOffset.UtcNow.ToUnixTimeSeconds()<expires;attempt++)try{
    lock(sync)if(device?.Peer!=item.device.Peer||device.Token!=item.device.Token||device.Key!=item.device.Key||!identity.IsTrusted(item.device.Peer))break;
    string bearer=await Token(life.Token);
    byte[] key=Convert.FromBase64String(item.device.Key);string encrypted;try{encrypted=Encrypt(key,identity.Fingerprint,JsonSerializer.SerializeToUtf8Bytes(item.payload,JsonOptions));}finally{CryptographicOperations.ZeroMemory(key);}
    var data=new Dictionary<string,string>{{"tl",encrypted},{"pc",identity.Fingerprint}};
    object message=item.device.Platform=="ios"?
     new{token=item.device.Token,data,apns=new{headers=new Dictionary<string,string>{{"apns-push-type","alert"},{"apns-priority","10"}},payload=new{aps=new Dictionary<string,object>{{"alert",new{title="TerminalLink",body="새 알림이 도착했습니다."}},{"mutable-content",1},{"category","TERMINAL_LINK_REPLY"}}}}}:
     (object)new{token=item.device.Token,data,android=new{priority=item.urgent?"high":"normal",ttl="43200s"}};
    using var request=new HttpRequestMessage(HttpMethod.Post,"https://fcm.googleapis.com/v1/projects/"+project+"/messages:send");
    request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);request.Content=new StringContent(JsonSerializer.Serialize(new{message},JsonOptions),Encoding.UTF8,"application/json");
    using var response=await http.SendAsync(request,life.Token);
    if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized){accessToken="";throw new IOException("Push credentials expired");}
    if(response.IsSuccessStatusCode){Status("FCM accepted");break;}
    Status("FCM HTTP "+(int)response.StatusCode);
    if((int)response.StatusCode<500&&(int)response.StatusCode!=429)break;
    throw new IOException("Push temporarily unavailable");
   }catch(OperationCanceledException)when(life.IsCancellationRequested){return;}catch(Exception e){Status(e.GetType().Name);await Task.Delay(TimeSpan.FromSeconds(Math.Min(300,2<<Math.Min(attempt,8))),life.Token);}
  }}catch(OperationCanceledException){}
 }
 public void Dispose(){hub.Notification=null;hub.Monitor=null;life.Cancel();queue.Writer.TryComplete();try{worker.Wait(2000);}catch{}http.Dispose();life.Dispose();}
}
