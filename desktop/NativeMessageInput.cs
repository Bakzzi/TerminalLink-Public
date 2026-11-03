using System.Text.Json;
namespace TerminalLink;

// The authenticated caller selects a native console by PID/start time. Ordinary
// text is the same ordered text + Enter operation as the terminal view. Screen
// parsing is not delivery authority. Requests/attachments retain their own route.
internal static class NativeMessageInput {
 internal static string Identity(ConsoleFrame frame){
  string identity=LocalDaemonFeed.Identity(frame,includeSsh:true);
  return identity.Length==0?"":(LocalDaemonFeed.Identity(frame).Length>0?"local:":"ssh:")+identity;
 }
 internal static bool CanCompose(ConsoleFrame frame)=>ScreenConversation.Recognizes(frame)&&!LocalDaemonFeed.ShellActive(frame);
 internal static bool Handles(string kind)=>kind=="nativeMessage";
 internal static object Execute(Action<JsonElement> send,JsonElement command,bool remote=false){
  string text=command.Str("text").Replace("\r\n","\n").Replace('\r','\n');
  if(string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(c=>char.IsControl(c)&&c!='\n'))throw new IOException("보낼 글을 확인하세요. 한 번에 4096자까지 입력할 수 있습니다.");
  // SSH can deliver text and Enter in the same remote read even when the local
  // console queue was drained. Explicit paste framing keeps Enter out of the
  // receiver's burst-paste heuristic. Local Windows input stays in key mode.
  send(JsonSerializer.SerializeToElement(new{op="text",text,literalNewlines=true,bracketedPaste=remote}));
  send(JsonSerializer.SerializeToElement(new{op="key",key="ENTER"}));
  return new{notificationResult=command.Str("request"),ok=true,delivery="InputWritten",message="터미널에 입력을 전달했습니다. Codex 접수는 아직 확인되지 않았습니다."};
 }
}
