using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TerminalLink;

internal sealed record SharedFile(string id,string name,long size,string sha256,long created,long expires);
internal sealed record IncomingMarker(string kind,string id,long created);
internal sealed class SharedFiles {
 internal const long MaxFile=512L*1024*1024,MaxTotal=2L*1024*1024*1024;
 internal const int MaxCount=32;
 readonly string root;
 internal SharedFiles(string? directory=null){root=Path.GetFullPath(directory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","shared-files"));}
 static bool Id(string id)=>Regex.IsMatch(id??"","^[a-f0-9]{32}$");
 static void Plain(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("연결된 폴더나 파일은 사용할 수 없습니다.");}
 string Folder(string id){if(!Id(id))throw new IOException("잘못된 파일 ID입니다.");return Path.Combine(root,id);}
 internal string Data(string id)=>Path.Combine(Folder(id),"content.bin");
 internal string Incoming(string id){if(!Id(id))throw new IOException("잘못된 업로드 ID입니다.");Directory.CreateDirectory(root);Plain(root);return Path.Combine(root,".incoming-"+id);}
 internal void DiscardIncoming(string id){
  if(!Id(id)||!Directory.Exists(root))return;Plain(root);string dir=Path.Combine(root,".incoming-"+id);if(!Directory.Exists(dir))return;Plain(dir);
  string[] names={"content.bin","metadata.json","owner.json","lease.lock"};
  foreach(string entry in Directory.EnumerateFileSystemEntries(dir)){Plain(entry);if((File.GetAttributes(entry)&FileAttributes.Directory)!=0||!names.Contains(Path.GetFileName(entry),StringComparer.Ordinal))throw new IOException("임시 업로드 폴더에 예상하지 못한 파일이 있습니다.");}
  foreach(string name in names){string file=Path.Combine(dir,name);if(File.Exists(file))File.Delete(file);}
  Directory.Delete(dir,false);
 }
 internal SharedFile CommitIncoming(string id,string name,long size,string digest){
  string dir=Incoming(id);Plain(dir);string path=Path.Combine(dir,"content.bin");Plain(path);
  using var publishing=new FileStream(Path.Combine(root,"publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var items=List();if(items.Length>=MaxCount)throw new IOException("임시 파일함은 32개까지 보관할 수 있습니다.");
  if(size<0||size>MaxFile||new FileInfo(path).Length!=size||items.Sum(x=>x.size)+size>MaxTotal)throw new IOException("파일함 용량 또는 파일 크기를 확인해 주세요.");
  long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();var item=new SharedFile(id,name,size,digest,now,now+86400000);
  File.WriteAllText(Path.Combine(dir,"metadata.json"),JsonSerializer.Serialize(item));Directory.Move(dir,Folder(id));return item;
 }
 internal SharedFile? Find(string id,long? at=null) {
  string dir=Folder(id);if(!Directory.Exists(dir))return null;
  try {
   Plain(root);Plain(dir);string manifest=Path.Combine(dir,"metadata.json");Plain(manifest);
   if(new FileInfo(manifest).Length>8192)return null;
   var item=JsonSerializer.Deserialize<SharedFile>(File.ReadAllText(manifest));
   long now=at??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
   if(item==null||item.id!=id||item.size<0||item.size>MaxFile||item.created<=0||item.expires!=item.created+86400000
      ||item.expires<=now||string.IsNullOrWhiteSpace(item.name)||item.name.Length>200
      ||item.name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||item.name is "." or ".."
      ||!Regex.IsMatch(item.sha256??"","^[A-F0-9]{64}$"))return null;
   string data=Data(id);Plain(data);if(new FileInfo(data).Length!=item.size)return null;return item;
  }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException){return null;}
 }
 internal SharedFile[] List(){
  Cleanup();if(!Directory.Exists(root))return Array.Empty<SharedFile>();Plain(root);
  return Directory.EnumerateDirectories(root).Select(Path.GetFileName).Where(x=>Id(x!)).Take(128).Select(x=>Find(x!)).OfType<SharedFile>().OrderByDescending(x=>x.created).Take(MaxCount).ToArray();
 }
 internal void Delete(string id){
  string dir=Folder(id);if(!Directory.Exists(dir))return;Plain(root);Plain(dir);
  // Only the two files created by Publish are removable. Never touch the original source.
  foreach(string name in new[]{"content.bin","metadata.json"}){string path=Path.Combine(dir,name);if(File.Exists(path)){Plain(path);File.Delete(path);}}
  Directory.Delete(dir,false);
 }
 internal void Cleanup(long? at=null){
  if(!Directory.Exists(root))return;
  try{
   Plain(root);long now=at??DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
   foreach(string dir in Directory.EnumerateDirectories(root).Take(128)){
    string id=Path.GetFileName(dir);if(!Id(id))continue;
    try {if(Find(id,now)==null)Delete(id);}catch(IOException){}catch(UnauthorizedAccessException){}
   }
   CleanupIncoming(now);
  }catch(IOException){}catch(UnauthorizedAccessException){}
 }
 internal void CleanupIncoming(long now){
  foreach(string dir in Directory.EnumerateDirectories(root,".incoming-*").Take(128))try{
   string id=Path.GetFileName(dir)[".incoming-".Length..];if(!Id(id))continue;Plain(dir);
   string markerPath=Path.Combine(dir,"owner.json"),lockPath=Path.Combine(dir,"lease.lock"),data=Path.Combine(dir,"content.bin");
   if(!File.Exists(markerPath)||!File.Exists(lockPath)||!File.Exists(data))continue;Plain(markerPath);Plain(lockPath);Plain(data);
   if(new FileInfo(markerPath).Length>2048)continue;var marker=JsonSerializer.Deserialize<IncomingMarker>(File.ReadAllText(markerPath));
   if(marker==null||marker.kind!="TerminalLinkFileBoxUpload"||marker.id!=id||marker.created<=0||now-marker.created<86400000||now-new DateTimeOffset(File.GetLastWriteTimeUtc(data)).ToUnixTimeMilliseconds()<86400000)continue;
   using(var lease=new FileStream(lockPath,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}
   DiscardIncoming(id);
  }catch(IOException){}catch(UnauthorizedAccessException){}catch(JsonException){}
 }
 internal SharedFile Publish(string source){
  source=Path.GetFullPath(source);
  if(!Regex.IsMatch(source,@"^[A-Za-z]:\\"))throw new IOException("PC의 로컬 파일만 올릴 수 있습니다.");
  if(!File.Exists(source))throw new IOException("올릴 파일을 찾지 못했습니다. 폴더는 ZIP으로 압축해 주세요.");
  Directory.CreateDirectory(root);Plain(root);
  using var publishing=new FileStream(Path.Combine(root,"publish.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var items=List();if(items.Length>=MaxCount)throw new IOException("임시 파일함은 32개까지 보관할 수 있습니다.");
  using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read,192*1024,FileOptions.SequentialScan);
  if(input.Length>MaxFile)throw new IOException("파일 하나는 최대 512MB까지 올릴 수 있습니다.");
  if(items.Sum(x=>x.size)+input.Length>MaxTotal)throw new IOException("임시 파일함의 2GB 한도를 넘었습니다.");
  string id=Guid.NewGuid().ToString("N"),staging=Path.Combine(root,".stage-"+id);
  Directory.CreateDirectory(staging);
  try{
   using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
   long size=0;byte[] buffer=new byte[192*1024];
   using(var output=new FileStream(Path.Combine(staging,"content.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){
    int count;while((count=input.Read(buffer))>0){output.Write(buffer,0,count);hash.AppendData(buffer,0,count);size+=count;}
    output.Flush(true);
   }
   long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
   string name=Path.GetFileName(source);if(name.Length>200)throw new IOException("파일 이름이 너무 깁니다.");
   var item=new SharedFile(id,name,size,Convert.ToHexString(hash.GetHashAndReset()),now,now+86400000);
   File.WriteAllText(Path.Combine(staging,"metadata.json"),JsonSerializer.Serialize(item));
   Directory.Move(staging,Folder(id));return item;
  }finally{
   if(Directory.Exists(staging)){foreach(string name in new[]{"content.bin","metadata.json"}){string path=Path.Combine(staging,name);if(File.Exists(path))File.Delete(path);}Directory.Delete(staging,false);}
  }
 }
 internal static void PublishCommand(string inputFile,string outputFile){
  try {
   using var doc=JsonDocument.Parse(File.ReadAllText(inputFile));
   var paths=doc.RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
   if(paths.Length is <1 or >32)throw new IOException("한 번에 1~32개 파일을 올릴 수 있습니다.");
   var store=new SharedFiles();var result=new List<object>();
   foreach(string path in paths){try{result.Add(new{ok=true,file=store.Publish(path),message=""});}catch(Exception e){result.Add(new{ok=false,file=(SharedFile?)null,message=e.Message});}}
   File.WriteAllText(outputFile,JsonSerializer.Serialize(result));
  }catch(Exception e){File.WriteAllText(outputFile,JsonSerializer.Serialize(new[]{new{ok=false,message=e.Message}}));Environment.ExitCode=1;}
 }
}

// One authenticated connection owns one bounded upload. Staging is never listed.
internal sealed class SharedUpload : IDisposable {
 readonly SharedFiles store;FileStream? output,lease;IncrementalHash? hash;string id="",token="",dir="",name="",expected="";long size,offset;
 internal SharedUpload(SharedFiles store){this.store=store;}
 void Abort(){output?.Dispose();output=null;hash?.Dispose();hash=null;lease?.Dispose();lease=null;if(dir.Length>0)store.DiscardIncoming(id);id=token=dir=name=expected="";size=offset=0;}
 internal object Process(JsonElement m){
  string op=m.Str("op"),request=m.Str("request"),key=m.Str("upload");
  if(!Guid.TryParseExact(request,"D",out _)||!Guid.TryParseExact(key,"D",out _))return new{type="sharedUploadResult",request,ok=false,message="잘못된 업로드 요청입니다."};
  try{
   if(op=="sharedUploadBegin"){
    if(output!=null)return new{type="sharedUploadResult",request,ok=false,message="다른 파일을 올리는 중입니다."};
    if(!m.TryGetProperty("size",out var n)||!n.TryGetInt64(out size)||size<0||size>SharedFiles.MaxFile)throw new IOException("파일 하나는 최대 512MB까지 올릴 수 있습니다.");
    expected=m.Str("sha256").ToUpperInvariant();if(!Regex.IsMatch(expected,"^[A-F0-9]{64}$"))throw new IOException("파일 검증값이 올바르지 않습니다.");
    name=FileUploads.SafeName(m.Str("name"));token=key;id=Guid.NewGuid().ToString("N");string candidate=store.Incoming(id);if(Directory.Exists(candidate))throw new IOException("파일함 저장 공간을 준비하지 못했습니다.");Directory.CreateDirectory(candidate);dir=candidate;
    lease=new FileStream(Path.Combine(dir,"lease.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);
    File.WriteAllText(Path.Combine(dir,"owner.json"),JsonSerializer.Serialize(new IncomingMarker("TerminalLinkFileBoxUpload",id,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
    output=new FileStream(Path.Combine(dir,"content.bin"),FileMode.CreateNew,FileAccess.Write,FileShare.None,192*1024,FileOptions.SequentialScan);hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);offset=0;
    return new{type="sharedUploadResult",request,ok=true,offset};
   }
   if(output==null||!string.Equals(token,key,StringComparison.OrdinalIgnoreCase))return new{type="sharedUploadResult",request,ok=false,message="업로드가 종료됐습니다. 다시 올려주세요."};
   if(op=="sharedUploadCancel"){Abort();return new{type="sharedUploadResult",request,ok=true,cancelled=true};}
   if(op=="sharedUploadChunk"){
    if(!m.TryGetProperty("offset",out var n)||!n.TryGetInt64(out long asked)||asked!=offset)throw new IOException("파일 전송 순서가 맞지 않습니다.");
    string encoded=m.Str("data");if(encoded.Length>16384)throw new IOException("파일 조각이 너무 큽니다.");byte[] bytes=Convert.FromBase64String(encoded);
    if(bytes.Length<1||bytes.Length>12288||offset+bytes.Length>size)throw new IOException("파일 크기가 올바르지 않습니다.");
    output.Write(bytes);hash!.AppendData(bytes);offset+=bytes.Length;return new{type="sharedUploadResult",request,ok=true,offset};
   }
   if(op!="sharedUploadEnd"||offset!=size)throw new IOException("파일 전송이 완료되지 않았습니다.");
   output.Flush(true);output.Dispose();output=null;string actual=Convert.ToHexString(hash!.GetHashAndReset());hash.Dispose();hash=null;
   if(actual!=expected)throw new IOException("파일 검증에 실패했습니다. 다시 올려주세요.");
   lease!.Dispose();lease=null;File.Delete(Path.Combine(dir,"lease.lock"));File.Delete(Path.Combine(dir,"owner.json"));
   var item=store.CommitIncoming(id,name,size,actual);dir=id=token=name=expected="";size=offset=0;return new{type="sharedUploadResult",request,ok=true,file=item};
  }catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or FormatException or InvalidOperationException){try{Abort();}catch{}return new{type="sharedUploadResult",request,ok=false,message=e is IOException?e.Message:"파일을 올리지 못했습니다."};}
 }
 public void Dispose(){try{Abort();}catch{}}
}

internal sealed class SharedDownload : IDisposable {
 internal const int Chunk=192*1024;
 readonly SharedFiles store;readonly object sync=new();FileStream? input;IncrementalHash? hash;SharedFile? file;
 string request="";long offset,last;
 internal SharedDownload(SharedFiles store){this.store=store;}
 void Close(){input?.Dispose();input=null;hash?.Dispose();hash=null;file=null;request="";offset=0;}
 internal void ExpireIdle(){lock(sync)if(input!=null&&Environment.TickCount64-last>45000)Close();}
 internal object Process(JsonElement message){
  lock(sync){
   string op=message.Str("op"),req=message.Str("request");
   if(!Guid.TryParseExact(req,"D",out _))return new{type="sharedError",request=req,message="잘못된 파일 요청입니다."};
   try {
    if(op=="sharedList")return new{type="sharedList",request=req,files=store.List()};
    if(op=="sharedDelete"){
     string id=message.Str("file");
     if(!message.Bool("confirmed"))throw new IOException("삭제 확인이 필요합니다.");
     if(file?.id==id)Close();store.Delete(id);return new{type="sharedDeleted",request=req,id};
    }
    if(op=="sharedCancel"){if(request==req)Close();return new{type="sharedCancelled",request=req};}
    if(op=="sharedOpen"){
     Close();file=store.Find(message.Str("file"))??throw new IOException("파일이 만료되었거나 삭제되었습니다.");
     input=new FileStream(store.Data(file.id),FileMode.Open,FileAccess.Read,FileShare.Read,Chunk,FileOptions.SequentialScan);
     if(input.Length!=file.size)throw new IOException("파일 정보가 변경되었습니다.");
     request=req;last=Environment.TickCount64;hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
     return new{type="sharedStart",request=req,file};
    }
    if(op!="sharedRead")throw new IOException("지원하지 않는 파일 요청입니다.");
    if(input==null||file==null||request!=req)throw new IOException("다운로드가 끝났거나 취소되었습니다. 다시 받기를 누르세요.");
    if(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()>=file.expires)throw new IOException("파일의 24시간 보관 기간이 끝났습니다.");
    if(!message.TryGetProperty("offset",out var number)||!number.TryGetInt64(out long asked)||asked!=offset)throw new IOException("파일 전송 순서가 맞지 않습니다.");
    last=Environment.TickCount64;
    if(offset==file.size){
     string digest=Convert.ToHexString(hash!.GetHashAndReset());
     if(digest!=file.sha256)throw new IOException("PC 파일 검증에 실패했습니다.");
     Close();return new{type="sharedEnd",request=req,sha256=digest};
    }
    int count=(int)Math.Min(Chunk,file.size-offset);byte[] bytes=new byte[count];input.ReadExactly(bytes);hash!.AppendData(bytes);
    long start=offset;offset+=count;return new{type="sharedChunk",request=req,offset=start,data=Convert.ToBase64String(bytes)};
   }catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException){
    if(op=="sharedOpen"||request==req)Close();
    return new{type="sharedError",request=req,message=e is IOException?e.Message:"파일을 처리할 수 없습니다."};
   }
  }
 }
 public void Dispose(){lock(sync)Close();}
}
