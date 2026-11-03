using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
namespace TerminalLink;
// Presentation only. Never feed this projection back into original-record matching or storage.
internal static class DisplayText {
 const string Open="<oai-mem-citation>",Close="</oai-mem-citation>";
 static readonly ConditionalWeakTable<ConversationMessage,ConversationMessage> messages=new();
 internal static ConversationMessage Message(ConversationMessage message)=>messages.GetValue(message,m=>{
  string text=m.role switch {"assistant"=>Assistant(m.text),"user"=>User(m.text),_=>m.text};
  return text==m.text?m:m with{text=text};
 });
 internal static Conversation? ForDisplay(Conversation? value){
  if(value==null)return null;
  var clean=value.messages.Select(Message).ToArray();
  return clean.Where((m,i)=>!ReferenceEquals(m,value.messages[i])).Any()?value with{messages=clean}:value;
 }
 // Decode only a complete, well-formed async reply envelope at the presentation boundary.
 // Keep original whitespace in both fields; neither record identity nor request handling uses this view.
 internal static string User(string text){
  const string open="<send_user_message_question_reply>",close="</send_user_message_question_reply>";
  string value=text.Trim();
  if(!value.StartsWith(open,StringComparison.Ordinal)||!value.EndsWith(close,StringComparison.Ordinal))return text;
  try{
   using var doc=JsonDocument.Parse(value[open.Length..^close.Length]);
   if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()==0)return text;
   var replies=new List<string>();
   foreach(var item in doc.RootElement.EnumerateArray()){
    if(item.ValueKind!=JsonValueKind.Object||!item.TryGetProperty("question",out var question)||question.ValueKind!=JsonValueKind.String||!item.TryGetProperty("answer",out var answer)||answer.ValueKind!=JsonValueKind.String||!item.TryGetProperty("questionItemId",out var key)||key.ValueKind!=JsonValueKind.String)return text;
    using var identity=JsonDocument.Parse(key.GetString()??"");var id=identity.RootElement;
    if(id.ValueKind!=JsonValueKind.Array||id.GetArrayLength()!=3||id[0].ValueKind!=JsonValueKind.String||id[0].GetString()!="request_user_input_async"||id[1].ValueKind!=JsonValueKind.String||string.IsNullOrEmpty(id[1].GetString())||id[2].ValueKind!=JsonValueKind.Number||!id[2].TryGetInt32(out int index)||index<0)return text;
    replies.Add("질문: "+question.GetString()+"\n\n답변: "+answer.GetString());
   }
   return string.Join("\n\n",replies);
  }catch(JsonException){return text;}
 }
 internal static string Assistant(string text){
  if(!text.Contains(Open,StringComparison.Ordinal))return text;
  var output=new StringBuilder();int keep=0,pos=0;char fence='\0';int fenceSize=0;bool metadata=false,changed=false;
  while(pos<text.Length){
   int newline=text.IndexOf('\n',pos),next=newline<0?text.Length:newline+1;
   string line=text[pos..(newline<0?text.Length:newline)].TrimEnd('\r');string trim=line.Trim();
   if(metadata){
    int end=line.IndexOf(Close,StringComparison.Ordinal);
    if(end<0){pos=next;continue;}
    metadata=false;keep=line[(end+Close.Length)..].Trim().Length==0?next:pos+end+Close.Length;
    pos=keep;continue;
   }
   int indent=0;while(indent<line.Length&&line[indent]==' ')indent++;
   if(indent<=3&&indent<line.Length&&line[indent]!='\t'){
    char marker=line[indent];int count=0;while(indent+count<line.Length&&line[indent+count]==marker)count++;
    if(fence!='\0'){if(marker==fence&&count>=fenceSize&&line[(indent+count)..].Trim().Length==0)fence='\0';}
    else if((marker=='`'||marker=='~')&&count>=3){fence=marker;fenceSize=count;}
    else if(FooterStart(line) is int start&&start>=0){
     output.Append(text,keep,pos+(trim==Open?0:start)-keep);metadata=true;changed=true;
     pos+=start+Open.Length;continue;
    }
   }
   pos=next;
  }
  if(!changed)return text;
  if(!metadata)output.Append(text,keep,text.Length-keep);
  return output.ToString();
 }
 // Metadata may be attached directly to the final sentence. Preserve Markdown examples.
 static int FooterStart(string line){
  if(line.TrimStart().StartsWith('>'))return -1;
  int start=line.IndexOf(Open,StringComparison.Ordinal);if(start<0)return -1;
  string suffix=line[(start+Open.Length)..].TrimStart();
  if(suffix.Length>0&&!suffix.StartsWith("<citation_entries>",StringComparison.Ordinal))return -1;
  int code=0;
  for(int i=0;i<start;i++)if(line[i]=='`'){
   int count=1;while(i+count<start&&line[i+count]=='`')count++;
   if(code==0)code=count;else if(count==code)code=0;i+=count-1;
  }
  return code==0?start:-1;
 }
 internal static ConversationMessage Preview(ConversationMessage message){
  var clean=Message(message);return clean.text.Length>4000?clean with{text=clean.text[..4000]+"…"}:clean;
 }
}
