using System.Security.Cryptography;
using System.Text.Json;
namespace TerminalLink;

// Raw files stay in private storage with server-generated names; they are never executed.
internal sealed class FileUploads : IDisposable {
 internal const int MaxBytes=32*1024*1024,MaxFiles=4;
 internal record Attachment(string path,string name);
 sealed class Upload : IDisposable {
  internal readonly string Path,Name,Hash;internal readonly long Size;internal readonly FileStream File;internal bool Ready,Sent;
  internal Upload(string path,string name,long size,string hash){Path=path;Name=name;Size=size;Hash=hash;File=new(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);}
  public void Dispose(){File.Dispose();if(!Sent)try{System.IO.File.Delete(Path);}catch{}}
 }
 readonly Dictionary<string,Upload> items=new();readonly string folder,storage;
 internal FileUploads(string? testFolder=null){storage=testFolder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","attachments");folder=Path.Combine(storage,"files");}
 internal static string SafeName(string name){
  var invalid=Path.GetInvalidFileNameChars().ToHashSet();
  string value=new(name.Where(c=>!invalid.Contains(c)&&!char.IsControl(c)&&System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)!=System.Globalization.UnicodeCategory.Format).ToArray());
  value=value.Trim().TrimEnd('.');if(value.Length==0||value is "." or "..")value="attachment.bin";
  if(value.Length>100){string extension=Path.GetExtension(value);if(extension.Length>16)extension="";int cut=100-extension.Length;if(char.IsHighSurrogate(value[cut-1]))cut--;value=value[..cut]+extension;}
  return value;
 }
 internal object Process(JsonElement m){
  string op=m.Str("op"),key=m.Str("file");
  if(!Guid.TryParseExact(key,"D",out _))throw new IOException("잘못된 파일 전송입니다.");
  if(op=="fileBegin"){
   if(items.ContainsKey(key)||items.Count>=MaxFiles)throw new IOException("파일은 한 번에 4개까지 보낼 수 있습니다.");
   long size=m.GetProperty("size").GetInt64();string hash=m.Str("sha256"),name=SafeName(m.Str("name"));
   if(size<0||size>MaxBytes||!System.Text.RegularExpressions.Regex.IsMatch(hash,"^[A-Fa-f0-9]{64}$"))throw new IOException("파일은 개당 32MB까지 보낼 수 있습니다.");
   Directory.CreateDirectory(folder);
   foreach(var f in new DirectoryInfo(folder).EnumerateFiles())if(f.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-7))try{f.Delete();}catch{}
   if(new DirectoryInfo(storage).EnumerateFiles("*",SearchOption.AllDirectories).Sum(f=>f.Length)+size>512L*1024*1024)throw new IOException("PC 첨부 보관 공간이 가득 찼습니다.");
   items.Add(key,new(Path.Combine(folder,Guid.NewGuid().ToString("N")+"-"+name),name,size,hash));return new{offset=0};
  }
  if(!items.TryGetValue(key,out var u))throw new IOException("파일 전송을 다시 시작하세요.");
  if(op=="fileCancel"){items.Remove(key);u.Dispose();return new{cancelled=true};}
  if(u.Ready)throw new IOException("이미 전송한 파일입니다.");
  if(op=="fileChunk"){
   if(m.GetProperty("offset").GetInt64()!=u.File.Length)throw new IOException("파일 전송 순서가 바뀌었습니다.");
   string encoded=m.Str("data");if(encoded.Length>16384)throw new IOException("파일 조각이 너무 큽니다.");
   byte[] bytes;try{bytes=Convert.FromBase64String(encoded);}catch(FormatException){throw new IOException("파일 데이터가 올바르지 않습니다.");}
   if(bytes.Length<1||bytes.Length>12288||u.File.Length+bytes.Length>u.Size)throw new IOException("파일 크기가 올바르지 않습니다.");
   u.File.Write(bytes);return new{offset=u.File.Length};
  }
  if(op!="fileEnd")throw new IOException("지원하지 않는 파일 요청입니다.");
  if(u.File.Length!=u.Size)throw new IOException("파일 전송이 완료되지 않았습니다.");
  u.File.Flush();u.File.Position=0;
  if(!Convert.ToHexString(SHA256.HashData(u.File)).Equals(u.Hash,StringComparison.OrdinalIgnoreCase))throw new IOException("파일 검증에 실패했습니다.");
  u.File.Dispose();u.Ready=true;return new{ready=true};
 }
 internal Attachment[] Paths(JsonElement m){
  if(!m.TryGetProperty("files",out var list)||list.ValueKind!=JsonValueKind.Array)throw new IOException("파일을 선택하세요.");
  var ids=list.EnumerateArray().Select(v=>v.GetString()??"").ToArray();
  if(ids.Length<1||ids.Length>MaxFiles||ids.Distinct().Count()!=ids.Length)throw new IOException("파일은 한 번에 4개까지 보낼 수 있습니다.");
  return ids.Select(id=>items.TryGetValue(id,out var u)&&u.Ready?new Attachment(u.Path,u.Name):throw new IOException("파일 전송이 끝나지 않았습니다.")).ToArray();
 }
 internal void Retain(JsonElement m){if(m.TryGetProperty("files",out var list))foreach(var v in list.EnumerateArray())if(items.TryGetValue(v.GetString()??"",out var u))u.Sent=true;}
 internal void Complete(JsonElement m){if(m.TryGetProperty("files",out var list))foreach(var v in list.EnumerateArray())if(items.Remove(v.GetString()??"",out var u))u.Dispose();}
 public void Dispose(){foreach(var u in items.Values)u.Dispose();items.Clear();}
}
