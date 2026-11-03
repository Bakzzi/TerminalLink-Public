using System.Buffers.Binary;
using System.Text.Json;
namespace TerminalLink;
internal static class Protocol {
 internal const int MaxJson=32768,MaxFrame=32*1024*1024;
 internal static async Task<JsonElement> ReadJson(Stream stream,CancellationToken token) {
  byte[] header=new byte[5];await stream.ReadExactlyAsync(header,token);
  int length=BinaryPrimitives.ReadInt32BigEndian(header);
  if(length<1||length>MaxJson||header[4]!=1)throw new IOException("Invalid message");
  byte[] payload=new byte[length-1];await stream.ReadExactlyAsync(payload,token);
  using var doc=JsonDocument.Parse(payload,new JsonDocumentOptions{MaxDepth=8});return doc.RootElement.Clone();
 }
 internal static async Task Send(Stream stream,SemaphoreSlim gate,byte type,byte[] payload,CancellationToken token) {
  if(payload.Length>MaxFrame)throw new IOException("Frame too large");
  await gate.WaitAsync(token);
  try{byte[] header=new byte[5];BinaryPrimitives.WriteInt32BigEndian(header,payload.Length+1);header[4]=type;await stream.WriteAsync(header,token);await stream.WriteAsync(payload,token);}
  finally{gate.Release();}
 }
 internal static Task Json(Stream s,SemaphoreSlim g,object value,CancellationToken t)=>Send(s,g,1,JsonSerializer.SerializeToUtf8Bytes(value),t);
 internal static string Str(this JsonElement e,string name,string fallback="")=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
 internal static bool Bool(this JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.True;
 internal static double Num(this JsonElement e,string name)=>e.TryGetProperty(name,out var v)&&v.TryGetDouble(out double d)?d:0;
}