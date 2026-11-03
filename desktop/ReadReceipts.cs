using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TerminalLink;
internal sealed record AnswerMark(long Sequence,string Digest){
 internal static AnswerMark? From(Conversation? chat){
  var last=chat?.messages.LastOrDefault(m=>m.role=="assistant"&&m.phase is "final" or "final_answer");
  return last==null?null:new(last.sequence,ReadReceipts.Hash(last.text));
 }
}
// Only hashes and read watermarks are persisted; no messages, titles or terminal IDs.
internal sealed class ReadReceipts {
 readonly string? path;Dictionary<string,AnswerMark> seen=new();
 internal ReadReceipts(string? path){this.path=path;try{if(path!=null&&File.Exists(path)&&new FileInfo(path).Length<=4194304)seen=JsonSerializer.Deserialize<Dictionary<string,AnswerMark>>(File.ReadAllText(path))??new();}catch(IOException){}catch(UnauthorizedAccessException){}catch(JsonException){}}
 internal static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
 internal bool Unread(string thread,AnswerMark? latest)=>latest!=null&&(!seen.TryGetValue(Hash(thread),out var read)||read==null||latest.Sequence>read.Sequence||latest.Sequence==read.Sequence&&latest.Digest!=read.Digest);
 internal void Mark(string thread,AnswerMark mark){
  string key=Hash(thread);if(seen.TryGetValue(key,out var old)&&old!=null&&(old==mark||old.Sequence>mark.Sequence))return;
  seen[key]=mark;if(path==null)return;
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(seen));File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
internal sealed class UnreadReplies(ReadReceipts receipts){
 string thread="";AnswerMark? latest;Conversation? lastObserved;
 internal bool HasUnread=>receipts.Unread(thread,latest);
 internal void Observe(Conversation? chat){
  if(!ConversationReader.IsOriginal(chat)||ReferenceEquals(lastObserved,chat))return;lastObserved=chat;
  var next=AnswerMark.From(chat);if(thread!=chat!.id){thread=chat.id;latest=next;}
  else if(next!=null&&(latest==null||next.Sequence>=latest.Sequence))latest=next;
 }
 internal void MarkDisplayed(Conversation? displayed){if(ConversationReader.IsOriginal(displayed)&&displayed!.id==thread&&AnswerMark.From(displayed) is AnswerMark mark)receipts.Mark(thread,mark);}
 internal static bool Visible(bool active,bool minimized,bool terminal,bool popup,bool ready,bool atBottom)=>active&&!minimized&&!terminal&&!popup&&ready&&atBottom;
}
