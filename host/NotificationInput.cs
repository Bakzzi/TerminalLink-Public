using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
namespace TerminalLink;

// Only explicit notification actions use this path. It never selects a Windows tab.
internal static class NotificationInput {
 internal static string Compact(string s)=>Regex.Replace(s,@"\s+","");
 internal sealed record Prompt(string question,string[] choices,int selected,bool followup,bool questionKind,bool notes,string draft);
 internal static string? OpenRequestKey(string text){
  if(!text.Contains("Queued follow-up inputs"))return null;
  if(Regex.IsMatch(text,@"(?im)^\s*shift\s*\+\s*←\s+to answer\s*$"))return "SHIFT_LEFT";
  return Regex.IsMatch(text,@"(?im)^\s*(?:alt|⌥)\s*\+\s*↑\s+to answer\s*$")?"UP":null;
 }
 internal static bool Collapsed(string text)=>OpenRequestKey(text)!=null;
 static string Reflow(string s)=>Regex.Replace(s.Trim(),@"\s+"," ");
 internal static Prompt? Parse(string text,string expectedQuestion=""){
#if DESKTOP
  if(NativeFieldPrompt.Parse(text) is {} field)return field;
#endif
  string tail=text[Math.Max(0,text.Length-16000)..].Replace("\r","");
  var footer=Regex.Match(tail,@"(?im)^\s*enter\s+submit\s+ctrl\s*\+\s*\]\s+skip\s+(?:option\s+\d+/\d+\s+)?(?:(?:alt|⌥)\s*\+\s*↓|shift\s*\+\s*→)\s+(?:main prompt|prev question)(?:\s+(?:(?:alt|⌥)\s*\+\s*↑|shift\s*\+\s*←)\s+(?:next question|queued messages))?\s*\z");
  bool followup=footer.Success,question=false;int start=-1;
  if(followup){var headers=Regex.Matches(tail[..footer.Index],@"(?m)^\s*(?:•\s*)?Queued follow-up inputs\s*$");if(headers.Count>0)start=headers[^1].Index+headers[^1].Length;}
  else {
   question=Regex.IsMatch(tail,@"(?is)enter to submit (?:answer|all)\s*(?:\|\s*(?:esc to interrupt|←/→ to navigate questions|ctrl \+ p / ctrl \+ n change question)\s*)*\z");
   bool approval=Regex.IsMatch(tail,@"(?is)press enter to confirm[^\n]*\s*\z");if(!question&&!approval)return null;
   if(question){var headers=Regex.Matches(tail,@"(?m)^\s*Question\s+\d+/\d+[^\n]*$");if(headers.Count>0)start=headers[^1].Index+headers[^1].Length;}
   else foreach(string marker in new[]{"Would you like to ","Do you want to ","Do you approve ","Submit with unanswered questions?"})start=Math.Max(start,tail.LastIndexOf(marker,StringComparison.Ordinal));
  }
  if(start<0)return null;
  string block=(followup?tail[start..footer.Index]:tail[start..]).Trim();
  if(followup){block=Regex.Replace(block,@"(?s)^↳[^\n]*(?:\n[^\n]+)*?\n\s*\n","");block=Regex.Replace(block,@"^\d+ of \d+\s*\n","");}
  var choices=new List<string>();var body=new List<string>();int selected=-1;bool notes=false;string draft="";
  foreach(string line in block.Split('\n')){
   string clean=line.Trim(),low=clean.ToLowerInvariant();
   if(!followup&&(low.Contains("enter to submit ")||low.StartsWith("tab to ")||low.StartsWith("tab or esc")||low.StartsWith("press enter to confirm"))){notes|=low.Contains("tab or esc")||low.Contains("tab to select")||low.Contains("tab to pick")||low.Contains("tab to switch");break;}
   var option=Regex.Match(line,@"^\s*([›>])?\s*(\d+)\.\s+(.*)$");
   if(option.Success){
    if(!int.TryParse(option.Groups[2].Value,out int n)||n!=choices.Count+1||n>12)return null;
    choices.Add(option.Groups[3].Value.Trim());if(option.Groups[1].Success)selected=n-1;
   }else if(choices.Count==0)body.Add(line);
   else if(clean.Length>0){if(clean.StartsWith("›")){notes=true;draft+=clean[1..].Trim();}else if(notes)draft+=clean;else choices[^1]+=" "+clean;}
  }
  if(followup&&choices.Count==0){
   var gaps=Regex.Matches(block,@"\n[ \t]*\n");if(gaps.Count==0)return null;var gap=gaps[^1];
   if(expectedQuestion.Length>0){var matching=gaps.Cast<Match>().FirstOrDefault(g=>Compact(block[..g.Index])==Compact(expectedQuestion));if(matching!=null)gap=matching;}
   string q=Reflow(block[..gap.Index]);draft=block[(gap.Index+gap.Length)..].Trim();
   if(draft=="Type your answer")draft="";
   return q.Length==0?null:new(q,Array.Empty<string>(),-1,true,true,true,draft);
  }
  if(body.Count==0||choices.Count==0||selected<0)return null;
  if(followup){string other=choices[^1];draft=selected==choices.Count-1&&other!="Other"&&other!="Type your answer"?other:"";choices[^1]="직접 입력";notes=selected==choices.Count-1;}
  return new(Reflow(string.Join("\n",body)),choices.ToArray(),selected,followup,followup||question,notes,draft);
 }
 static void Key(IntPtr write,string key,bool alt=false)=>ConsoleNative.Write(write,JsonSerializer.SerializeToElement(new{op="key",key=key=="SHIFT_LEFT"?"LEFT":key,alt,shift=key=="SHIFT_LEFT"}));
 static Prompt RequirePrompt(Func<ConsoleFrame> read,JsonElement message){
  Prompt? p=null;long deadline=Environment.TickCount64+1500;
  do{p=Parse(read().text,message.Str("question"));if(p!=null)break;Thread.Sleep(75);}while(Environment.TickCount64<deadline);
  if(p==null)throw new IOException("요청이 이미 끝났거나 화면이 바뀌었습니다. 앱에서 확인하세요.");
  if(Compact(p.question)!=Compact(message.Str("question")))throw new IOException("다른 질문으로 바뀌었습니다. 새 요청을 확인하세요.");
  string[] expected=message.GetProperty("choices").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
  if(!p.choices.Select(Compact).SequenceEqual(expected.Select(Compact)))throw new IOException("선택지가 바뀌었습니다. 새 요청을 확인하세요.");
  return p;
 }
 internal static void EmptyMain(ConsoleFrame frame){
#if DESKTOP
  frame=NativeComposer.Normalize(frame);
#endif
  if(Parse(frame.text)!=null||Collapsed(frame.text))throw new IOException("답변할 요청이 있습니다. 요청 알림이나 앱에서 답하세요.");
  string[] lines=frame.text.Split('\n');int y=frame.cursorY;
  if(y<0||y>=lines.Length)throw new IOException("입력 상태를 확인할 수 없습니다. 앱에서 확인하세요.");
  string line=lines[y].TrimStart();int prefix=lines[y].Length-line.Length;
  if(!line.StartsWith("›")||frame.cursorX>prefix+2)throw new IOException("PC 입력칸을 사용 중입니다. 앱에서 확인하세요.");
  string draft=Regex.Replace(line[1..],@"[\u2800-\u28ff]","").Trim();
  // The CLI draws grey example prompts in an empty composer.
  if(draft.Length>0&&!Regex.IsMatch(draft,@"^(?:Ask Codex to do anything|Type a message|Implement \{feature\}|Find and fix a bug in @filename|Explain this codebase|Write tests for @filename|Improve documentation in @filename|Summarize recent commits|Run /review on my current changes)$"))
   throw new IOException("PC에 작성 중인 내용이 있습니다. 앱에서 확인하세요.");
 }
 internal static int[] CodexPeers(ConsoleFrame frame)=>frame.peers.Where(pid=>{try{using var p=System.Diagnostics.Process.GetProcessById(pid);return !p.HasExited&&p.ProcessName.Equals("codex",StringComparison.OrdinalIgnoreCase);}catch{return false;}}).ToArray();
 internal static void StillCodex(IntPtr read,int[] original){
  if(!CodexPeers(ConsoleNative.Read(read)).Any(original.Contains))throw new IOException("Codex가 종료되었습니다. 입력을 중단했습니다.");
 }
 static void SameDraft(ConsoleFrame frame,string expected){
#if DESKTOP
  frame=NativeComposer.Normalize(frame);
#endif
  string[] lines=frame.text.Split('\n');int end=Math.Min(frame.cursorY,lines.Length-1),start=end;
  while(start>=0&&!lines[start].TrimStart().StartsWith("›"))start--;
  if(start<0)throw new IOException("입력칸이 바뀌었습니다. 앱에서 확인하세요.");
  string value=lines[start].TrimStart()[1..]+string.Join("",lines.Skip(start+1).Take(end-start));
  value=Regex.Replace(value,@"[\u2800-\u28ff]","");
  string actual=Compact(value),full=Compact(expected);
  // The legacy console API projects non-BMP glyphs as U+FFFD.
  string projected=string.Concat(full.EnumerateRunes().Select(r=>r.IsBmp?r.ToString():"\ufffd"));
  bool pasted=Regex.IsMatch(actual,@"^\[PastedContent"+expected.EnumerateRunes().Count()+@"chars\](?:#\d+)?$");
  // A full-height composer scrolls its beginning out of the console viewport.
  // Require a substantial exact suffix and a composer occupying the screen.
  bool clipped=start<=2&&end-start>=8&&actual.Length>=128&&projected.EndsWith(actual,StringComparison.Ordinal);
  if(!pasted&&!clipped&&actual!=full&&actual!=projected)throw new IOException("PC 입력칸의 내용이 바뀌어 Enter를 보내지 않았습니다. 앱에서 확인하세요.");
 }
 internal static void CheckCurrent(Conversation conversation,JsonElement message){
  if(!ConversationReader.IsOriginal(conversation)||conversation.id!=message.Str("thread"))throw new IOException("알림의 대화가 바뀌었습니다.");
  if(message.Str("kind")=="answer"){
   var last=conversation.messages.LastOrDefault();
   if(last?.sequence!=message.GetProperty("sequence").GetInt64()||last.role!="assistant"||last.phase is not ("final" or "final_answer"))throw new IOException("새 대화가 진행되었습니다. 최신 알림에서 답장하세요.");
  }else if(message.Str("requestKey").Length>0&&!(conversation.requests?.Any(q=>q.id==message.Str("requestKey"))??false))throw new IOException("이미 끝난 요청입니다.");
 }
 internal static object Execute(IntPtr read,IntPtr write,JsonElement message){
  try{
#if DESKTOP
   if(message.Str("kind")=="startMessage")return DesktopStartInput.Execute(()=>ConsoleNative.Read(read),
    text=>{ConsoleNative.Write(write,JsonSerializer.SerializeToElement(new{op="text",text,literalNewlines=true}));ConsoleNative.SettleInput(write);},
    ()=>Key(write,"ENTER"),message);
#endif
   using var target=NotificationTarget.Connect(()=>ConsoleNative.Read(read),message.Str("thread"));
   return ExecuteCore(()=>ConsoleNative.Read(read),target.Current,
    (text)=>{if(target.Remote)ConsoleNative.WriteRemotePaste(write,text);else ConsoleNative.Write(write,JsonSerializer.SerializeToElement(new{op="text",text,literalNewlines=true}));ConsoleNative.SettleInput(write);},
    (key,alt)=>Key(write,key,alt),message);
  }catch(Exception e){return new{notificationResult=message.Str("request"),ok=false,message=e.Message};}
 }
 internal static object ExecuteCore(Func<ConsoleFrame> read,Func<Conversation> target,Action<string> paste,Action<string,bool> key,JsonElement message){
  string request=message.Str("request");
  try{
   string text=message.Str("text").Replace("\r\n","\n").Replace("\r","\n"),kind=message.Str("kind");int choice=message.TryGetProperty("choice",out var c)?c.GetInt32():-1;
   if(text.Length>4096||text.Any(ch=>char.IsControl(ch)&&ch!='\n'))throw new IOException("알림에서는 4096자 이내로 답장해 주세요.");
   var frame=read();var conversation=target();CheckCurrent(conversation,message);
   if(!ConversationReader.IsOriginal(conversation)||conversation.id!=message.Str("thread"))throw new IOException("대화가 변경되었습니다. 새 알림에서 답장하세요.");
   if(kind=="answer"){
    long seq=message.GetProperty("sequence").GetInt64();
    if(conversation.messages.LastOrDefault()?.sequence!=seq||conversation.messages.LastOrDefault()?.role!="assistant")throw new IOException("새 대화가 진행되었습니다. 최신 답변을 확인하세요.");
    EmptyMain(frame);if(string.IsNullOrWhiteSpace(text))throw new IOException("답장 내용을 입력하세요.");
   }
#if DESKTOP
   else if(kind=="message"){
    EmptyMain(frame);if(string.IsNullOrWhiteSpace(text))throw new IOException("메시지를 입력하세요.");
   }
#endif
   else if(kind=="request"){
    string requestKey=message.Str("requestKey");
    if(requestKey.Length>0&&!(conversation.requests?.Any(q=>q.id==requestKey)??false))throw new IOException("이미 끝난 요청입니다. 새 요청을 확인하세요.");
    if(OpenRequestKey(frame.text) is string openKey){key(openKey,openKey=="UP");Thread.Sleep(450);}
    var prompt=RequirePrompt(read,message);if(prompt.draft.Length>0)throw new IOException("PC에서 답변을 작성 중입니다. 앱에서 확인하세요.");
    int targetChoice=choice;
    if(targetChoice<0){if(!prompt.questionKind)throw new IOException("승인 요청은 선택 버튼을 이용하세요.");if(string.IsNullOrWhiteSpace(text))throw new IOException("답장 내용을 입력하세요.");targetChoice=prompt.followup&&prompt.choices.Length>0?prompt.choices.Length-1:prompt.selected;}
    if(targetChoice>=prompt.choices.Length)throw new IOException("선택지가 변경되었습니다.");
    for(int attempts=0;prompt.selected>=0&&prompt.selected!=targetChoice&&attempts<12;attempts++){
     int before=prompt.selected;key(targetChoice>before?"DOWN":"UP",false);long deadline=Environment.TickCount64+1800;
     do{Thread.Sleep(75);prompt=RequirePrompt(read,message);}while(prompt.selected==before&&Environment.TickCount64<deadline);
     if(prompt.selected==before)throw new IOException("선택 변경을 확인하지 못했습니다. 앱에서 확인하세요.");
    }
    if(prompt.selected>=0&&prompt.selected!=targetChoice)throw new IOException("선택을 확인하지 못했습니다. 앱에서 확인하세요.");
    if(choice<0&&!prompt.followup&&!prompt.notes){key("TAB",false);Thread.Sleep(120);prompt=RequirePrompt(read,message);}
    if(choice>=0)text="";
   }else throw new IOException("잘못된 알림 요청입니다.");
   // Revalidate after navigation, before writing any answer text.
   CheckCurrent(target(),message);
   if(kind=="request")RequirePrompt(read,message);else EmptyMain(read());
   if(text.Length>0)paste(text);
   CheckCurrent(target(),message);
   if(kind=="request"){
    var confirmed=RequirePrompt(read,message);
    if(choice>=0&&confirmed.selected!=choice)throw new IOException("PC의 선택이 바뀌어 전송하지 않았습니다.");
    if(text.Length>0&&Compact(confirmed.draft)!=Compact(text)&&!Regex.IsMatch(Compact(confirmed.draft),@"^\[PastedContent"+text.EnumerateRunes().Count()+@"chars\](?:#\d+)?$"))throw new IOException("요청 답변의 입력을 확인하지 못해 전송하지 않았습니다. 앱에서 확인하세요.");
   }else {
    long deadline=Environment.TickCount64+1500;
    while(true){try{SameDraft(read(),text);break;}catch(IOException)when(Environment.TickCount64<deadline){Thread.Sleep(100);}}
   }
   key("ENTER",false);
   return new{notificationResult=request,ok=true,message="PC로 전송했습니다."};
  }catch(Exception e){return new{notificationResult=request,ok=false,message=e.Message};}
 }
#if DESKTOP
 internal static void DesktopSameDraft(ConsoleFrame frame,string expected)=>SameDraft(frame,expected);
#endif
}
