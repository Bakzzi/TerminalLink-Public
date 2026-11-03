using System.Text.Json;
using System.Text.RegularExpressions;

namespace TerminalLink;

// A first message targets the exact native TUI, before there is a chat to bind.
// This path is never a fallback for an unreadable existing conversation.
internal static class DesktopStartInput {
 internal static string Normalize(string text)=>text.Replace("\r\n","\n").Replace('\r','\n').Trim();
 internal static bool Welcome(ConsoleFrame frame){
  frame=NativeComposer.Normalize(frame);
  if(!ScreenConversation.Recognizes(frame)||LocalDaemonFeed.ShellActive(frame))return false;
  string text=frame.text.Replace("\r","");
  var lines=text.Split('\n');
  int prompt=Array.FindIndex(lines,l=>l.StartsWith("› "));
  if(prompt<0||lines.Count(l=>l.StartsWith("› "))!=1||frame.cursorY!=prompt)return false;
  if(!Regex.IsMatch(text,@"(?m)^\s*>_ OpenAI Codex \(v[^\n]+\)\s*$")||!text.Contains("To get started, describe a task or try one of these commands:",StringComparison.Ordinal))return false;
  if(!Regex.IsMatch(text,@"(?m)^\s*/init - create an AGENTS\.md file")||!Regex.IsMatch(text,@"(?m)^\s*/review - review any changes and find issues"))return false;
  if(Regex.IsMatch(text,@"(?im)^\s*(?:•|Worked for\b|Queued follow-up inputs\b|Question \d+/\d+|Side from main thread\b)|esc to interrupt"))return false;
  try{NotificationInput.EmptyMain(frame);return true;}catch(IOException){return false;}
 }
 internal static string Identity(ConsoleFrame frame)=>Welcome(frame)?LocalDaemonFeed.Identity(frame):"";
 internal static void Submit(Func<ConsoleFrame> read,Func<ConsoleFrame,string> identity,string expected,string text,Action beforePaste,Action<string> paste,Action enter){
  if(expected.Length==0||string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(c=>char.IsControl(c)&&c!='\n'))throw new IOException("보낼 메시지를 확인하세요. 한 번에 4096자까지 입력할 수 있습니다.");
  if(text.TrimStart().StartsWith('/'))throw new IOException("첫 대화를 시작하기 전의 / 명령은 터미널 보기에서 실행해 주세요.");
  void Initial(){var frame=read();if(identity(frame)!=expected||!Welcome(frame))throw new IOException("새 Codex의 입력 상태가 바뀌었습니다. 작성한 내용은 유지됩니다.");}
  Initial();beforePaste();Initial();paste(text);
  void Current(ConsoleFrame frame){if(identity(frame)!=expected||LocalDaemonFeed.ShellActive(frame)||NotificationInput.Parse(frame.text)!=null||NotificationInput.Collapsed(frame.text))throw new IOException("Codex 입력 상태가 바뀌어 Enter를 보내지 않았습니다.");}
  long deadline=Environment.TickCount64+1500;
  while(true){
   var frame=read();Current(frame);
   try{NotificationInput.DesktopSameDraft(frame,text);break;}catch(IOException)when(Environment.TickCount64<deadline){Thread.Sleep(100);}
  }
  var last=read();Current(last);
  NotificationInput.DesktopSameDraft(last,text);enter();
 }
 internal static object Execute(Func<ConsoleFrame> read,Action<string> paste,Action enter,JsonElement command){
  string identity=command.Str("identity"),text=command.Str("text").Replace("\r\n","\n").Replace('\r','\n');
  LocalDaemonFeed.Catalog? before=null;
  Submit(read,f=>LocalDaemonFeed.Identity(f),identity,text,()=>before=LocalDaemonFeed.DesktopCaptureStart(),paste,enter);
  // Sending is already complete. Read failures here must never cause a resend.
  string thread="";
  try{
   long deadline=Environment.TickCount64+4000;
   do{
    var frame=read();if(LocalDaemonFeed.Identity(frame)!=identity)break;
    if(LocalDaemonFeed.DesktopBindStart(before!,frame,text) is Conversation bound){thread=bound.id;break;}
    Thread.Sleep(150);
   }while(Environment.TickCount64<deadline);
  }catch{/* Enter was already sent. A record-link failure must not invite a duplicate send. */}
  return new{notificationResult=command.Str("request"),ok=true,thread,message="PC로 전송했습니다."};
 }
}
