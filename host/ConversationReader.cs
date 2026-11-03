using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
namespace TerminalLink;

internal sealed class ConversationReader {
 CodexFeed records=new();Conversation? unavailable,start,starting;long startingSince;
 internal void Retry(){records=new();}
 internal static bool IsOriginal(Conversation? value)=>value!=null&&value.readState=="ready"&&value.source is "record" or "remote";
 internal Conversation? Read(ConsoleFrame frame){
  bool side=Regex.IsMatch(frame.text,@"(?im)^\s*Side from main thread\b");
  if(side||LocalDaemonFeed.ShellActive(frame)){LocalDaemonFeed.Forget(LocalDaemonFeed.Identity(frame));return Missing(frame);}
  string native=CodexStartInput.Identity(frame);
  if(native.Length>0){
   if(start?.id!="start:"+native){LocalDaemonFeed.Forget(native);records=new();start=new("start:"+native,Array.Empty<ConversationMessage>(),false,Array.Empty<QueuedQuestion>(),"start",true,"empty");starting=null;}
   startingSince=0;return start;
  }
  if(LocalDaemonFeed.Read(frame) is Conversation local){start=null;return local;}
  Conversation? record=records.Read(frame.peers);
  if(record!=null&&!side){start=null;return record;}
  if(!side&&RemoteCodexFeed.Read(frame) is Conversation remote)return remote;
  if(start!=null&&start.id=="start:"+LocalDaemonFeed.Identity(frame)&&ScreenConversation.Recognizes(frame)&&!LocalDaemonFeed.ShellActive(frame)){
   if(startingSince==0)startingSince=Environment.TickCount64;
   if(Environment.TickCount64-startingSince<30000)return starting??=start with{readState="pending"};
  }else start=null;
  return Missing(frame);
 }
 internal Conversation? Missing(ConsoleFrame frame){
  if(!ScreenConversation.Recognizes(frame))return null;
  // A console viewport is not a conversation record. Never publish its fragments as chat.
  if(unavailable==null||unavailable.id!="unavailable:"+frame.session)
   unavailable=new("unavailable:"+frame.session,Array.Empty<ConversationMessage>(),false,Array.Empty<QueuedQuestion>(),"unavailable",true,"failed","이 탭의 대화 원본을 확인하지 못했습니다. 다시 시도하거나 터미널에서 내용을 확인해 주세요.");
  return unavailable;
 }
}
internal sealed class ScreenConversation {
 Conversation? latest;string lastKey="";readonly string fallback=Guid.NewGuid().ToString("N");
 const string Fence="\u0060\u0060\u0060";
 static readonly Regex Prompt=new(@"^›(?:\s|$)",RegexOptions.Compiled);
 static readonly Regex Assistant=new(@"^•(?:\s|$)",RegexOptions.Compiled);
 static readonly Regex Evidence=new(@"(?im)(?:Ask Codex to do anything|Ask a follow-up question|\bOpenAI Codex\b|\? for shortcuts|esc to interrupt|ctrl\s*\+\s*t to (?:view|open)|Side from main thread|^\s*(?:[─━]\s*)?Worked for\s+\d|^\s*gpt-[\w.-]+\s+(?:low|medium|high|xhigh|fast|·))",RegexOptions.Compiled);
 static readonly Regex Tool=new(@"^(?:Ran\s|Running\s|Called\s|Calling\s|Explored(?:\s|$)|Exploring(?:\s|$)|Searched(?:\s|$)|Searching(?:\s|$)|Updated (?:the )?[Pp]lan(?:\s|$)|Read \d+ (?:file|line))",RegexOptions.Compiled);
 static readonly Regex Progress=new(@"^[•◦✦✧]\s+.+\((?:\d+(?:\.\d+)?(?:ms|[hms])\s*)+[•·]\s*esc to interrupt\)\s*$",RegexOptions.Compiled|RegexOptions.IgnoreCase);
 // A clipped tool envelope has no speaker marker. Do not label its payload as a chat reply.
 // Explicitly marked user/assistant messages, including JSON examples, are preserved.
 static bool ToolEnvelope(IEnumerable<string> lines){
  string value=string.Join("\n",lines);
  bool Has(string pattern)=>Regex.IsMatch(value,pattern,RegexOptions.CultureInvariant);
  return Has(@"""request_id""\s*:")&&Has(@"""(?:tool|elapsed_ms)""\s*:")
   ||Has(@"""chunk_id""\s*:")&&Has(@"""(?:wall_time_seconds|exit_code)""\s*:")
   ||Has(@"""type""\s*:\s*""function_call_output""")&&Has(@"""(?:call_id|output)""\s*:");
 }
 static readonly Regex ListStart=new(@"^(?:[-*•]\s|\d+[.)]\s|>\s|#{1,6}\s)",RegexOptions.Compiled);
 static string Clean(string value)=>Regex.Replace(value,@"[\u2800-\u28ff]"," ");
 internal static bool Recognizes(ConsoleFrame frame){
  string text=Clean(frame.text);
  var lines=text.Replace("\r","").Split('\n');
  string cursor=frame.cursorY>=0&&frame.cursorY<lines.Length?lines[frame.cursorY].TrimStart():"";
  if(Regex.IsMatch(cursor,@"^(?:PS\s+.+>|[A-Za-z]:\\[^>]*>|[\w.@-]+(?::[^\r\n$#]*)?[$#])(?:\s|$)"))return false;
  return Regex.IsMatch(text,@"(?m)^›(?:\s|$)")&&Evidence.IsMatch(text);
 }
 internal Conversation? Read(ConsoleFrame frame){
  if(!Recognizes(frame)){latest=null;lastKey="";return null;}
  var messages=Extract(frame);
  string identity=frame.session.Length>0?frame.session:fallback;
  string id="screen:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
  string key=id+"\n"+string.Join("\0",messages.Select(m=>m.role+"\n"+m.text));
  if(key==lastKey&&latest!=null)return latest;
  lastKey=key;return latest=new(id,messages,true,Array.Empty<QueuedQuestion>(),"terminal");
 }
 internal static ConversationMessage[] Extract(ConsoleFrame frame){
  string text=Clean(frame.text);string[] lines=text.Replace("\r","").Split('\n');
  int lastPrompt=Array.FindLastIndex(lines,l=>Prompt.IsMatch(l));
  int composer=-1;
  if(lastPrompt>=0){
   bool afterAnswer=!lines.Skip(lastPrompt+1).Any(l=>Assistant.IsMatch(l));
   bool footer=lines.Skip(lastPrompt+1).Any(l=>Regex.IsMatch(l,@"(?i)(?:\? for shortcuts|gpt-[\w.-]+\s|Side from main thread|ctrl\s*\+\s*/|esc to interrupt)"));
   bool placeholder=Regex.IsMatch(lines[lastPrompt],@"^›\s*(?:Ask Codex to do anything|Ask a follow-up question|Type a message|Implement \{feature\}|Find and fix a bug in @filename|Explain this codebase|Write tests for @filename|Improve documentation in @filename|Summarize recent commits|Run /review on my current changes)\s*$");
   if(afterAnswer&&(placeholder||footer||frame.cursorY>=lastPrompt&&frame.cursorY<=lastPrompt+6))composer=lastPrompt;
  }
  int end=composer>=0?composer:lines.Length;
  var output=new List<ConversationMessage>();var block=new List<(string value,int width)>();
  int firstMarker=Array.FindIndex(lines,l=>Prompt.IsMatch(l)||Assistant.IsMatch(l));
  var prefix=lines.Take(firstMarker<0?end:firstMarker).ToArray();
  bool tablePrefix=prefix.Count(l=>Regex.Matches(l,@"[|│┃║]").Count>=2)>=2&&prefix.Any(l=>Regex.IsMatch(l,@"[─━═-]{3,}"));
  bool clipped=prefix.Any(l=>!string.IsNullOrWhiteSpace(l))&&(!prefix.Any(l=>Regex.IsMatch(l,@"^\s*[│└╭╰]|OpenAI Codex|ctrl\s*\+\s*t to view transcript"))||tablePrefix);
  if(ToolEnvelope(prefix))clipped=false;
  string role=clipped?"screen":"";bool tool=false,notice=false;long sequence=0;int total=0;
  void Flush(){
   if(role.Length>0&&!tool){
    string value=Reflow(block,frame.columns);
    if(value.Length>0){output.Add(new(role,role=="screen"?"partial":"screen",value,++sequence));total+=value.Length;}
   }
   block.Clear();role="";tool=false;notice=false;
  }
  for(int i=0;i<end;i++){
   string raw=lines[i],trim=raw.Trim();
   if(Progress.IsMatch(raw)){Flush();continue;}
   bool user=Prompt.IsMatch(raw),assistant=Assistant.IsMatch(raw);
   if(user||assistant){
    Flush();role=user?"user":"assistant";string first=raw.Length>=2?raw[2..]:"";tool=assistant&&Tool.IsMatch(first);
    if(!tool)block.Add((first,Cells(raw)));continue;
   }
   if(role.Length==0||tool)continue;
   if(Regex.IsMatch(raw,@"^(?:◦|✦|✧)\s")||Regex.IsMatch(trim,@"^(?:[─━]\s*)?Worked for\s+\d",RegexOptions.IgnoreCase)){Flush();continue;}
   if(raw.StartsWith("⚠")||Regex.IsMatch(trim,@"^(?:Tip: Press ctrl|Queued follow-up inputs|Type your answer|enter submit|enter to submit|ctrl\s*\+\s*\]\s+skip)",RegexOptions.IgnoreCase)){notice=true;continue;}
   if(notice){if(trim.Length==0)notice=false;continue;}
   if(Regex.IsMatch(trim,@"^\d{1,2}:\d{2}\s*(?:AM|PM)$",RegexOptions.IgnoreCase))continue;
   if(Regex.IsMatch(trim,@"^[─━]{3,}$")){if(block.Any(l=>Regex.IsMatch(l.value,@"[|│┃║]")))block.Add((raw.Length>=2&&raw.StartsWith("  ")?raw[2..]:raw,Cells(raw)));continue;}
   block.Add((raw.StartsWith("  ")?raw[2..]:raw,Cells(raw)));
  }
  Flush();
  return output.ToArray();
 }
 static string Reflow(List<(string value,int width)> lines,int columns){
  var result=new StringBuilder();bool fence=false,previousTable=false,alignedTable=false;string previous="";int previousWidth=0;
  foreach(var item in lines){
   string line=item.value.TrimEnd();string trim=line.TrimStart();
   bool marker=trim.StartsWith(Fence)||trim.StartsWith("~~~");
   bool code=fence||marker||line.StartsWith("    ")&&!ListStart.IsMatch(previous.TrimStart());
   if(line.Length==0)alignedTable=false;
   if(!fence&&Regex.IsMatch(line,@"^\s*[-─━═]{3,}(?: {2,}[-─━═]{3,})+\s*$"))alignedTable=true;
   bool table=alignedTable||trim.Contains('│')||trim.Contains('┃')||trim.Contains('║')||trim.Contains('┼')||trim.Contains('─')||trim.Contains('|')||Regex.IsMatch(line,@"\S\s{3,}\S");
   bool join=result.Length>0&&previous.Length>0&&line.Length>0&&!code&&!table&&!previousTable&&!ListStart.IsMatch(trim)
    &&!previous.TrimStart().StartsWith(Fence)&&previousWidth>=Math.Max(20,columns-2);
   if(result.Length>0){
    if(join){
     char a=previous.TrimEnd()[^1],b=trim[0];
     if(!(Cjk(a)&&Cjk(b)))result.Append(' ');
    }else result.Append('\n');
   }
   result.Append(join?trim:line);
   if(marker)fence=!fence;
   previous=line;previousWidth=item.width;previousTable=table;
  }
  return result.ToString().Trim();
 }
 static bool Cjk(char c)=>c is >= '\u1100' and <= '\u11ff' or >= '\u2e80' and <= '\ua4cf' or >= '\uac00' and <= '\ud7af' or >= '\uf900' and <= '\ufaff';
 internal static int Cells(string value){
  int width=0;foreach(var rune in value.EnumerateRunes()){
   int c=rune.Value;
   if(c<32||c is >=0x300 and <=0x36f||c is >=0xfe00 and <=0xfe0f)continue;
   width+=c>=0x1100&&(c<=0x115f||c==0x2329||c==0x232a||c>=0x2e80&&c<=0xa4cf||c>=0xac00&&c<=0xd7a3||c>=0xf900&&c<=0xfaff||c>=0xfe10&&c<=0xfe6f||c>=0xff00&&c<=0xff60||c>=0xffe0&&c<=0xffe6||c>=0x1f300)?2:1;
  }return width;
 }
}
