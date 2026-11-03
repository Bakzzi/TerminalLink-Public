namespace TerminalLink;
// Presentation-only input receipts: "sent" means written to the terminal, NOT
// accepted by Codex. ChatRow labels it pending until an original record arrives.
// Original records and input authority are never modified.
internal sealed class SentEchoes {
 internal sealed record Receipt(string Thread,long After,string Text,string[] Files,long Id,string[]? ImagePaths=null){
  internal ConversationMessage Message=>new("user","sent",Files.Length==0?Text:AttachmentMessage.Compose(Text,Files.Select(Path.GetFileName)!),Id);
 }
 readonly List<Receipt> pending=new();long serial;string currentThread="";
 readonly Dictionary<long,(ConversationMessage Original,ConversationMessage Display)> matched=new();
 internal void Add(string thread,long after,string text,string[] files,string[]? imagePaths=null)=>pending.Add(new(thread,after,text,files,--serial,imagePaths));
 internal static string Normalize(string text)=>text.Replace("\r\n","\n").Replace('\r','\n').Trim();
 internal static bool Matches(string original,Receipt receipt){
  string body=Normalize(original),text=Normalize(receipt.Text);if(receipt.Files.Length==0)return body==text;
  int images=receipt.Files.Count(AttachmentMessage.IsImage);var attachment=AttachmentMessage.Read(body,images);
  if(attachment.ImageCount!=images)return false;
  if(attachment.ImagePaths.Length>0&&receipt.ImagePaths is {Length:>0} expected&&(expected.Length!=attachment.ImagePaths.Length||expected.Where((p,i)=>!AttachmentMessage.SamePath(p,attachment.ImagePaths[i])).Any()))return false;
  body=Normalize(attachment.Caption);
  var files=receipt.Files.Where(f=>!AttachmentMessage.IsImage(f)).ToArray();
  if(files.Length==0)return body==text;
  string prefix=(text.Length>0?text:"첨부한 파일을 확인해 줘.")+"\n\n첨부 파일 (";
  return files.Length>0&&(body.StartsWith(prefix+"PC 저장 경로):\n",StringComparison.Ordinal)||body.StartsWith(prefix+"원격 저장 경로):\n",StringComparison.Ordinal))&&files.All(f=>body.Contains("- "+Path.GetFileName(f)+": \"",StringComparison.Ordinal));
 }
 internal ConversationMessage[] Project(Conversation original){
  if(currentThread!=original.id){currentThread=original.id;matched.Clear();}
  pending.RemoveAll(p=>p.Thread!=original.id);
  foreach(var p in pending.ToArray()){
   var found=original.messages.FirstOrDefault(m=>m.role=="user"&&m.sequence>p.After&&!matched.ContainsKey(m.sequence)&&Matches(m.text,p));
   if(found!=null){matched[found.sequence]=(found,p.Files.Length==0?found:found with{text=p.Message.text,phase="attachment-display"});pending.Remove(p);}
  }
  var result=new List<ConversationMessage>();var remaining=new List<Receipt>(pending);
  foreach(var m in original.messages){foreach(var p in remaining.Where(p=>p.After<m.sequence).ToArray()){result.Add(p.Message);remaining.Remove(p);}result.Add(matched.TryGetValue(m.sequence,out var match)&&match.Original==m?match.Display:m);}
  result.AddRange(remaining.Select(p=>p.Message));return result.ToArray();
 }
}
