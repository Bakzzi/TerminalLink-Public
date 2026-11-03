using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media.Imaging;
namespace TerminalLink;

// One authenticated connection owns its unfinished uploads. Never accept a client file path.
internal sealed class ImageUploads : IDisposable {
 internal const int MaxBytes=8*1024*1024,MaxImages=4;
 sealed class Upload : IDisposable {
  internal readonly string Path;internal readonly FileStream File;internal readonly int Size;internal readonly string Hash;
  internal bool Ready,Sent;
  internal Upload(string path,int size,string hash){Path=path;Size=size;Hash=hash;File=new(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);}
  public void Dispose(){File.Dispose();if(!Sent)try{System.IO.File.Delete(Path);}catch{}}
 }
 readonly Dictionary<string,Upload> items=new();
 readonly string folder;
 internal ImageUploads(string? testFolder=null){
  folder=testFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","attachments");
 }
 internal object Process(JsonElement m){
  string op=m.Str("op"),key=m.Str("image");
  if(!Guid.TryParseExact(key,"D",out _))throw new IOException("잘못된 사진 전송입니다.");
  if(op=="imageBegin"){
   if(items.ContainsKey(key)||items.Count>=MaxImages)throw new IOException("사진은 한 번에 4장까지 보낼 수 있습니다.");
   int size=(int)m.Num("size");string hash=m.Str("sha256");
   if(size<1||size>MaxBytes||!System.Text.RegularExpressions.Regex.IsMatch(hash,"^[A-Fa-f0-9]{64}$"))throw new IOException("사진 크기 또는 검증값이 올바르지 않습니다.");
   Directory.CreateDirectory(folder);
   foreach(var f in new DirectoryInfo(folder).EnumerateFiles("*.png"))if(f.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7))try{f.Delete();}catch{}
   if(new DirectoryInfo(folder).EnumerateFiles("*",SearchOption.AllDirectories).Sum(f=>f.Length)+size>512L*1024*1024)throw new IOException("PC 사진 보관 공간이 가득 찼습니다.");
   items.Add(key,new(Path.Combine(folder,Guid.NewGuid().ToString("N")+".png"),size,hash));
   return new{offset=0};
  }
  if(!items.TryGetValue(key,out var item))throw new IOException("사진 전송을 다시 시작하세요.");
  if(op=="imageCancel"){items.Remove(key);item.Dispose();return new{cancelled=true};}
  if(item.Ready)throw new IOException("이미 전송한 사진입니다.");
  if(op=="imageChunk"){
   if((long)m.Num("offset")!=item.File.Length)throw new IOException("사진 전송 순서가 바뀌었습니다.");
   string encoded=m.Str("data");if(encoded.Length>16384)throw new IOException("사진 조각이 너무 큽니다.");
   byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch(FormatException){throw new IOException("사진 데이터가 올바르지 않습니다.");}
   if(bytes.Length==0||bytes.Length>12288||item.File.Length+bytes.Length>item.Size)throw new IOException("사진 크기가 올바르지 않습니다.");
   item.File.Write(bytes);return new{offset=item.File.Length};
  }
  if(op!="imageEnd")throw new IOException("지원하지 않는 사진 요청입니다.");
  if(item.File.Length!=item.Size)throw new IOException("사진 전송이 완료되지 않았습니다.");
  item.File.Flush();item.File.Position=0;
  if(!Convert.ToHexString(SHA256.HashData(item.File)).Equals(item.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("사진 검증에 실패했습니다.");
  item.File.Position=0;
  try{
   var decoder=BitmapDecoder.Create(item.File,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
   var frame=decoder.Frames[0];
   if(frame.PixelWidth<1||frame.PixelHeight<1||frame.PixelWidth>4096||frame.PixelHeight>4096||(long)frame.PixelWidth*frame.PixelHeight>16_777_216)throw new IOException("사진 해상도가 너무 큽니다.");
   if(decoder is not PngBitmapDecoder && decoder is not JpegBitmapDecoder)throw new IOException("PNG 또는 JPEG 사진만 보낼 수 있습니다.");
  }catch(Exception e)when(e is not IOException){throw new IOException("읽을 수 없는 사진입니다.");}
  item.File.Dispose();item.Ready=true;
  return new{ready=true};
 }
 internal string[] Paths(JsonElement m){
  if(!m.TryGetProperty("images",out var values)||values.ValueKind!=JsonValueKind.Array)throw new IOException("사진을 선택하세요.");
  string[] ids=values.EnumerateArray().Select(v=>v.GetString()??"").ToArray();
  if(ids.Length<1||ids.Length>MaxImages||ids.Distinct().Count()!=ids.Length)throw new IOException("사진은 한 번에 4장까지 보낼 수 있습니다.");
  return ids.Select(id=>items.TryGetValue(id,out var u)&&u.Ready?u.Path:throw new IOException("사진 전송이 끝나지 않았습니다.")).ToArray();
 }
 internal void Retain(JsonElement m){foreach(var v in m.GetProperty("images").EnumerateArray())if(items.TryGetValue(v.GetString()??"",out var u))u.Sent=true;}
 internal void Complete(JsonElement m){foreach(var v in m.GetProperty("images").EnumerateArray())if(items.Remove(v.GetString()??"",out var u))u.Dispose();}
 public void Dispose(){foreach(var i in items.Values)i.Dispose();items.Clear();}
}
