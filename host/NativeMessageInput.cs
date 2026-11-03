using System.Text.Json;
namespace TerminalLink;

// The authenticated caller selects a native console by PID/start time. Ordinary
// text is the same ordered text + Enter operation as the terminal view. Screen
// parsing is not delivery authority. Requests/attachments retain their own route.
internal static class NativeMessageInput {
 internal static bool Handles(string kind)=>kind=="message";
 internal static object Execute(Action<JsonElement> send,JsonElement command){
  string text=command.Str("text").Replace("\r\n","\n").Replace('\r','\n');
  if(string.IsNullOrWhiteSpace(text)||text.Length>4096||text.Any(c=>char.IsControl(c)&&c!='\n'))throw new IOException("보낼 글을 확인하세요. 한 번에 4096자까지 입력할 수 있습니다.");
  send(JsonSerializer.SerializeToElement(new{op="text",text,literalNewlines=true}));
  send(JsonSerializer.SerializeToElement(new{op="key",key="ENTER"}));
  return new{notificationResult=command.Str("request"),ok=true,message="선택한 터미널로 전송했습니다."};
 }
}
