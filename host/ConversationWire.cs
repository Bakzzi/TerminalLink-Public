using System.Security.Cryptography;
using System.Text.Json;
namespace TerminalLink;
internal static class ConversationWire {
 // Fragment the encoded update, so UTF-8, escaped JSON and message IDs round-trip exactly.
 internal static IEnumerable<object> Fragments(byte[] payload){
  string transfer=Guid.NewGuid().ToString("N"),hash=Convert.ToHexString(SHA256.HashData(payload));
  for(int offset=0,index=0;offset<payload.Length;index++){
   int count=Math.Min(65536,payload.Length-offset);bool last=offset+count==payload.Length;
   yield return new{type="conversationChunk",transfer,index,data=Convert.ToBase64String(payload,offset,count),last,sha256=last?hash:""};offset+=count;
  }
 }
 internal static async Task Send(Stream stream,SemaphoreSlim gate,object update,bool chunks,CancellationToken token){
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(update);
  if(bytes.Length<4*1024*1024){await Protocol.Send(stream,gate,1,bytes,token);return;}
  if(!chunks){await Protocol.Json(stream,gate,new{type="status",message="긴 대화를 받으려면 휴대폰 앱을 업데이트해 주세요. PC의 대화 기록은 유지됩니다."},token);return;}
  foreach(var fragment in Fragments(bytes))await Protocol.Json(stream,gate,fragment,token);
 }


 internal static object? ForClient(Conversation? before,Conversation? after,bool events,bool chunks){
  // Older mobile builds do not know readOnly. Keep their established terminal
  // authority guard while still giving them every recovered message.
  if(!chunks&&after?.readOnly==true)return DisplayText.ForDisplay(after) with{source="terminal"};
  return events?Delta(before,after):DisplayText.ForDisplay(after);
 }
 internal static object? Delta(Conversation? before,Conversation? after){
  if(after==null||before==null||after.id!=before.id||!ConversationReader.IsOriginal(after)||!ConversationReader.IsOriginal(before)||after.source!=before.source||after.readState!=before.readState)return DisplayText.ForDisplay(after);
  long last=before.messages.Select(m=>m.sequence).DefaultIfEmpty().Max();
  if(after.messages.Length==0||after.messages[^1].sequence<last)return DisplayText.ForDisplay(after);
  return new{after.id,after.earlierOmitted,after.requests,after.source,after.readOnly,after.readState,after.readError,after.weeklyUsage,delta=true,baseSequence=last,
   firstSequence=after.messages[0].sequence,messages=after.messages.Where(m=>m.sequence>last).Select(DisplayText.Message).ToArray()};
 }
}
