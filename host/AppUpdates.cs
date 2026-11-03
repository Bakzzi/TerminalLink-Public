using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TerminalLink;

internal sealed record AppRelease(string packageName,int versionCode,string versionName,long size,string sha256,string notes);
internal sealed class AppUpdates {
 internal const int MaxApk=32*1024*1024,Chunk=48*1024;
 readonly string folder;
 internal AppUpdates(string? folder=null){this.folder=folder??Path.Combine(AppContext.BaseDirectory,"updates");}
 internal AppRelease? Current(){
  try{
   string manifest=Path.Combine(folder,"release.json");var f=new FileInfo(manifest);
   if(!f.Exists||f.Length>8192)return null;
   var release=JsonSerializer.Deserialize<AppRelease>(File.ReadAllText(manifest));
   if(release==null||release.packageName!="com.terminallink.remote"||release.versionCode<1
     ||!Regex.IsMatch(release.versionName??"",@"^\d+\.\d+\.\d+$")
     ||!Regex.IsMatch(release.sha256??"","^[A-F0-9]{64}$")
     ||release.size<1||release.size>MaxApk||(release.notes?.Length??0)>1500)return null;
   var apk=new FileInfo(Path.Combine(folder,release.sha256+".apk"));
   return apk.Exists&&apk.Length==release.size?release:null;
  }catch{return null;}
 }
 internal async Task Info(Stream stream,SemaphoreSlim gate,CancellationToken token){
  var release=Current();await Protocol.Json(stream,gate,new{type="updateInfo",release},token);
 }
 internal async Task Download(Stream stream,SemaphoreSlim gate,JsonElement message,Func<bool> trusted,CancellationToken token){
  string request=message.Str("request"),hash=message.Str("sha256");
  if(!Regex.IsMatch(request,"^[a-fA-F0-9-]{36}$"))throw new IOException("잘못된 업데이트 요청");
  try{
   using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(90));
   var release=Current();
   if(release==null||release.sha256!=hash)throw new IOException("새 버전을 다시 확인하세요.");
   // Fixed digest-based filename only. Never accept a client-provided file path.
   byte[] bytes=await File.ReadAllBytesAsync(Path.Combine(folder,release.sha256+".apk"),deadline.Token);
   if(bytes.LongLength!=release.size||Convert.ToHexString(SHA256.HashData(bytes))!=release.sha256)throw new IOException("PC 업데이트 파일 검증에 실패했습니다.");
   if(!trusted())throw new IOException("기기 등록이 해제되었습니다.");
   await Protocol.Json(stream,gate,new{type="updateStart",request,release},deadline.Token);
   for(int offset=0;offset<bytes.Length;offset+=Chunk){
    if(!trusted())throw new IOException("기기 등록이 해제되었습니다.");
    int length=Math.Min(Chunk,bytes.Length-offset);
    await Protocol.Json(stream,gate,new{type="updateChunk",request,offset,data=Convert.ToBase64String(bytes,offset,length)},deadline.Token);
   }
   await Protocol.Json(stream,gate,new{type="updateEnd",request},deadline.Token);
  }catch(Exception e) when(e is not OperationCanceledException){
   await Protocol.Json(stream,gate,new{type="updateError",request,message=e is IOException?e.Message:"PC에서 업데이트 파일을 읽을 수 없습니다."},token);
  }
 }
}