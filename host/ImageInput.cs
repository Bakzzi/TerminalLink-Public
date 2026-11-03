using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TerminalLink;
internal static class ImageInput {
 static (string text,int start,int end) Draft(ConsoleFrame frame){
  frame=NativeComposer.Normalize(frame);
  string[] lines=frame.text.Split('\n');int end=Math.Min(frame.cursorY,lines.Length-1),start=end;
  while(start>=0&&!lines[start].TrimStart().StartsWith("›"))start--;
  if(start<0)throw new IOException("Codex 입력칸을 확인할 수 없습니다.");
  return (Regex.Replace(lines[start].TrimStart()[1..]+string.Join("",lines.Skip(start+1).Take(end-start)),@"[\u2800-\u28ff]",""),start,end);
 }
 internal static void CheckDraft(IntPtr read,int count,string text)=>CheckDraft(ConsoleNative.Read(read),count,text);
 internal static void CheckDraft(ConsoleFrame frame,int count,string text){
  var visible=Draft(frame);
  string draft=NotificationInput.Compact(visible.text);
  string expected=NotificationInput.Compact(text);
  string projected=string.Concat(expected.EnumerateRunes().Select(r=>r.IsBmp?r.ToString():"\ufffd"));
  // Attachments were checked before adding text; a full-height composer can hide its beginning.
  if(text.Length>0&&visible.start<=2&&visible.end-visible.start>=8&&draft.Length>=128&&draft.Length<projected.Length&&projected.EndsWith(draft,StringComparison.Ordinal))return;
  // Match the exact caption/file references first. A filename may itself contain
  // "[Image #1]"; only native attachment markers preceding the text count as images.
  string prefix=draft;
  if(text.Length>0){
   if(draft.EndsWith(expected,StringComparison.Ordinal))prefix=draft[..^expected.Length];
   else if(draft.EndsWith(projected,StringComparison.Ordinal))prefix=draft[..^projected.Length];
   else{
    var pasted=Regex.Match(draft,@"\[PastedContent"+text.EnumerateRunes().Count()+@"chars\](?:#\d+)?$");
    if(!pasted.Success)throw new IOException("PC 입력 내용이 바뀌어 전송을 중단했습니다. 입력칸을 확인하세요.");
    prefix=draft[..pasted.Index];
   }
  }
  if(Regex.Matches(prefix,@"\[Image#\d+\]").Count!=count||Regex.Replace(prefix,@"\[Image#\d+\]","").Length!=0)
   throw new IOException("첨부 입력을 확인하지 못했습니다. PC 입력칸을 확인하세요.");
 }
 internal static string FilePrompt(string caption,FileUploads.Attachment[] files,string location="PC"){
  string text=string.IsNullOrWhiteSpace(caption)?"첨부한 파일을 확인해 줘.":caption;
  return text+"\n\n첨부 파일 ("+location+" 저장 경로):\n"+string.Join("\n",files.Select(f=>"- "+FileUploads.SafeName(f.name)+": \""+f.path+"\""));
 }
 internal static void WaitDraft(Func<ConsoleFrame> read,int count,string text,Action verify,Action? wait=null){
  wait??=()=>Thread.Sleep(100);
  if(count==0&&text.Length==0){NativeComposer.WaitEmpty(read,_=>verify(),wait);return;}
  for(int attempt=0;;attempt++){
   verify();
   try{CheckDraft(read(),count,text);return;}
   catch(IOException)when(attempt<20){wait();}
  }
 }
 // Shared by local and SSH delivery, including the first pre-write read. Only
 // observations retry: each file/caption is pasted once and Enter is sent once.
 internal static void Deliver(string[] paths,string text,Action verify,Action<int,string> draft,Action<string> paste,Action enter){
  int count=0;
  foreach(string path in paths){verify();draft(count,"");paste(path);draft(++count,"");}
  if(text.Length>0){verify();draft(count,"");paste(text);}
  draft(count,text);verify();draft(count,text);enter();
 }
 internal static object Execute(IntPtr read,IntPtr write,JsonElement message){
  string request=message.Str("request");bool touched=false;
  try{
   string text=message.Str("text").Replace("\r\n","\n").Replace("\r","\n");
   if(text.Length>4096||text.Any(c=>char.IsControl(c)&&c!='\n'))throw new IOException("설명은 4096자 이내로 입력하세요.");
   string[] paths=message.GetProperty("paths").EnumerateArray().Select(p=>p.GetString()??"").ToArray();
   var files=message.TryGetProperty("files",out var docs)?docs.EnumerateArray().Select(f=>new FileUploads.Attachment(f.Str("path"),f.Str("name"))).ToArray():Array.Empty<FileUploads.Attachment>();
   if(paths.Length+files.Length is <1 or >4||paths.Concat(files.Select(f=>f.path)).Any(p=>!File.Exists(p)||p.Contains('\n')||p.Contains('\r')))throw new IOException("첨부 파일을 확인할 수 없습니다.");
   if(files.Length>0)text=FilePrompt(text,files);
   var frame=ConsoleNative.Read(read);int[] peers=NotificationInput.CodexPeers(frame);
   if(peers.Length==0)throw new IOException("첨부는 Codex가 실행 중인 탭에서 보낼 수 있습니다.");
   string thread=message.Str("thread");LocalImageTarget? daemonTarget=null;
   if(thread.Length>0){
    var feed=new CodexFeed();Conversation? c=null;
    for(int i=0;i<5&&c==null;i++){c=feed.Read(frame.peers,thread);if(c==null)Thread.Sleep(350);}
    if(c?.id!=thread){
     daemonTarget=LocalImageTarget.Connect(()=>ConsoleNative.Read(read),thread);
    }
   }
   void StillTarget(){NotificationInput.StillCodex(read,peers);daemonTarget?.Check(ConsoleNative.Read(read));}
   Deliver(paths,text,StillTarget,(n,t)=>WaitDraft(()=>ConsoleNative.Read(read),n,t,StillTarget),value=>{
    touched=true;
    ConsoleNative.Write(write,JsonSerializer.SerializeToElement(new{op="text",text=value,literalNewlines=true}));
    ConsoleNative.SettleInput(write);
   },()=>ConsoleNative.Write(write,JsonSerializer.SerializeToElement(new{op="key",key="ENTER"})));
   return new{notificationResult=request,ok=true,message="첨부와 메시지를 보냈습니다."};
  }catch(Exception e){return new{notificationResult=request,ok=false,message=e.Message+(touched?" 자동으로 다시 보내지 않았습니다. 터미널 입력칸을 확인하세요.":"")};}
 }
}
