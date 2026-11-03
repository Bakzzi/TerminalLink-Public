using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TerminalLink;
internal record ConversationMessage(string role,string phase,string text,long sequence=0);
internal record QueuedQuestion(string id,string title,string[] options,string turn="");
internal record WeeklyUsage(double usedPercent,long resetsAt,long observedAt);
internal record Conversation(string id,ConversationMessage[] messages,bool earlierOmitted,QueuedQuestion[]? requests=null,string source="record",bool readOnly=false,string readState="ready",string readError="",WeeklyUsage? weeklyUsage=null);
internal sealed class CodexFeed {
 string? path;long nextFind;DateTime lastWrite;long lastLength;Conversation? latest;
 readonly Dictionary<string,DateTime> inspected=new(StringComparer.OrdinalIgnoreCase);
 string discoveryPeers="";
 readonly List<QueuedQuestion> questions=new();
 readonly Dictionary<string,string> questionTurns=new(StringComparer.Ordinal);string activeTurn="";
 long offset,sequence;readonly MemoryStream partial=new();readonly List<ConversationMessage> history=new();string threadId="";
 WeeklyUsage? weeklyUsage;
 static readonly string root=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex","sessions"));
 internal Conversation? Read(int[] peers,string expectedThread=""){
  // Console agents also attach to this console and read rollout files. Only the
  // actual Codex process may establish ownership of a conversation.
  peers=peers.Where(p=>{try{using var process=Process.GetProcessById(p);return process.ProcessName.Equals("codex",StringComparison.OrdinalIgnoreCase);}catch{return false;}}).ToArray();

  string peerKey=string.Join(",",peers.Order());
  if(peerKey!=discoveryPeers){discoveryPeers=peerKey;inspected.Clear();nextFind=0;}
  if(Environment.TickCount64>=nextFind){
   nextFind=Environment.TickCount64+(path==null?1000:15000);
   if(path!=null&&!Owners(path).Any(peers.Contains)){path=null;latest=null;lastLength=-1;Reset();inspected.Clear();}
   if(path==null&&peers.Length>0&&Directory.Exists(root)){
    // Bound work per poll, not the age/rank of a discoverable conversation.
    // Revisit changed files and discover new files first; continue through older
    // unchanged records on later polls instead of restarting at the newest 40.
    bool hint=Guid.TryParse(expectedThread,out _);
    var files=Directory.EnumerateFiles(root,"rollout-*.jsonl",SearchOption.AllDirectories)
     .Select(p=>new FileInfo(p)).OrderByDescending(f=>hint&&f.Name.EndsWith("-"+expectedThread+".jsonl",StringComparison.OrdinalIgnoreCase))
     .ThenByDescending(f=>f.LastWriteTimeUtc).ToArray();
    // A temporary ownership miss must not blacklist the explicitly requested chat
    // for the entire retry window merely because its content has not changed.
    var pending=files.Where(f=>hint&&f.Name.EndsWith("-"+expectedThread+".jsonl",StringComparison.OrdinalIgnoreCase)||!inspected.TryGetValue(f.FullName,out var stamp)||stamp!=f.LastWriteTimeUtc).ToArray();
    if(pending.Length==0){inspected.Clear();pending=files;}
    foreach(var file in pending.Take(40)){
     inspected[file.FullName]=file.LastWriteTimeUtc;
     try{
      using var reader=Open(file.FullName);using var first=JsonDocument.Parse(reader.ReadLine()??"{}");var meta=first.RootElement;
      if(meta.Str("type")!="session_meta"||!meta.TryGetProperty("payload",out var payload)||!InteractiveOrigin(payload))continue;
      if(Owners(file.FullName).Any(peers.Contains)){path=file.FullName;lastLength=-1;Reset();inspected.Clear();break;}
     }catch{}
    }
   }
  }
  if(path==null)return null;
  try{var info=new FileInfo(path);if(info.Length==lastLength&&info.LastWriteTimeUtc==lastWrite)return latest;
   ReadAdded(path);lastLength=info.Length;lastWrite=info.LastWriteTimeUtc;
  }catch{}return latest;
 }
 // Origin labels describe record creation, not its current terminal owner. A
 // resumed/current codex-tui record may retain the vscode label. Callers still
 // require the live CLI/daemon writer and unique console evidence before use.
 internal static bool InteractiveOrigin(JsonElement payload)=>payload.Str("source")=="cli"||payload.Str("source")=="vscode"&&payload.Str("originator")=="codex-tui";
 static StreamReader Open(string file)=>new(new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete),Encoding.UTF8);
 void Reset(){offset=0;sequence=0;partial.SetLength(0);history.Clear();questions.Clear();questionTurns.Clear();activeTurn="";threadId="";latest=null;weeklyUsage=null;}
 internal Conversation? Snapshot=>latest;
 internal void ReadAdded(string file){
  using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
  if(input.Length<offset)Reset();input.Position=offset;var buffer=new byte[65536];int length;bool changed=false;
  while((length=input.Read(buffer))>0){offset+=length;int start=0;
   for(int i=0;i<length;i++)if(buffer[i]==10){
    partial.Write(buffer,start,i-start);changed|=AddRecord(partial.GetBuffer().AsMemory(0,checked((int)partial.Length)));
    partial.SetLength(0);start=i+1;
   }
   if(start<length)partial.Write(buffer,start,length-start);
  }
  if(changed||latest==null)Publish();
 }
 void RemoveQuestions(Func<QueuedQuestion,bool> remove){
  foreach(var q in questions.Where(remove).ToArray()){questions.Remove(q);questionTurns.Remove(q.id);}
 }
 internal void RetireQuestionsForUserMessage(string text){
  const string start="<send_user_message_question_reply>",end="</send_user_message_question_reply>";
  string normalized=text.Replace("\r\n","\n"),trimmed=normalized.Trim();
  if(trimmed.StartsWith(start,StringComparison.Ordinal)&&trimmed.EndsWith(end,StringComparison.Ordinal)){
   try{
    using var doc=JsonDocument.Parse(trimmed[start.Length..^end.Length]);
    if(doc.RootElement.ValueKind!=JsonValueKind.Array)return;
    foreach(var reply in doc.RootElement.EnumerateArray()){
     if(reply.ValueKind!=JsonValueKind.Object||!reply.TryGetProperty("answer",out var answer)||answer.ValueKind!=JsonValueKind.String)continue;
     using var key=JsonDocument.Parse(reply.Str("questionItemId"));var id=key.RootElement;
     if(id.ValueKind!=JsonValueKind.Array||id.GetArrayLength()!=3||id[0].ValueKind!=JsonValueKind.String||id[0].GetString()!="request_user_input_async"||id[1].ValueKind!=JsonValueKind.String||id[2].ValueKind!=JsonValueKind.Number||!id[2].TryGetInt32(out int index)||index<0)continue;
     string requestId=id[1].GetString()+":"+index,title=Regex.Replace(reply.Str("question"),@"\s+"," ").Trim();
     // Some Codex reply surfaces flatten paragraph breaks into spaces. The
     // original call ID and question index must still match exactly.
     RemoveQuestions(q=>q.id==requestId&&Regex.Replace(q.title,@"\s+"," ").Trim()==title);
    }
   }catch(JsonException){}
   return;
  }
  int before=questions.Count;
  RemoveQuestions(q=>normalized.StartsWith("> "+q.title.Replace("\r\n","\n")+"\n",StringComparison.Ordinal));
  if(questions.Count!=before)return; // A legacy answer resolves only its question.
  // Ordinary accepted input replaces the queued follow-ups, including a steer
  // inside the SAME active turn. Waiting for task_complete leaves stale request
  // banners until the eventual final answer. This is original-record evidence,
  // never a send attempt, empty terminal frame, title animation or read failure.
  RemoveQuestions(_=>true);
 }
 internal void Publish()=>latest=new(threadId,history.ToArray(),false,questions.ToArray(),weeklyUsage:weeklyUsage);
 internal bool AddRecord(ReadOnlyMemory<byte> data){
  try{using var doc=JsonDocument.Parse(data);var item=doc.RootElement;if(!item.TryGetProperty("payload",out var payload))return false;
   if(item.Str("type")=="session_meta"){threadId=payload.Str("id");return true;}
   if(item.Str("type")=="event_msg"&&payload.Str("type")=="token_count"&&payload.TryGetProperty("rate_limits",out var limits)&&limits.ValueKind==JsonValueKind.Object&&limits.Str("limit_id")=="codex"){
    foreach(string field in new[]{"primary","secondary"})if(limits.TryGetProperty(field,out var window)&&window.ValueKind==JsonValueKind.Object&&window.TryGetProperty("window_minutes",out var minutes)&&minutes.ValueKind==JsonValueKind.Number&&minutes.TryGetInt32(out int n)&&n==10080&&window.TryGetProperty("used_percent",out var percent)&&percent.ValueKind==JsonValueKind.Number&&percent.TryGetDouble(out double used)&&used>=0&&used<=100&&window.TryGetProperty("resets_at",out var resets)&&resets.ValueKind==JsonValueKind.Number&&resets.TryGetInt64(out long reset)&&reset>0&&DateTimeOffset.TryParse(item.Str("timestamp"),out var observed)){
     var value=new WeeklyUsage(used,reset,observed.ToUnixTimeSeconds());if(weeklyUsage==null||value.observedAt>=weeklyUsage.observedAt){weeklyUsage=value;return true;}
    }
    return false;
   }
   // Async questions belong to a turn. Plain user replies and skipped questions
   // may never produce a quoted response message, so also retire them at boundaries.
   if(item.Str("type")=="event_msg"&&payload.Str("type")=="task_started"){
    activeTurn=payload.Str("turn_id");
    if(activeTurn.Length==0)return false;
    int before=questions.Count;RemoveQuestions(q=>!questionTurns.TryGetValue(q.id,out var turn)||turn!=activeTurn);return before!=questions.Count;
   }
   if(item.Str("type")=="event_msg"&&payload.Str("type")=="task_complete"){
    string ended=payload.Str("turn_id");int before=questions.Count;
    RemoveQuestions(q=>ended.Length==0||!questionTurns.TryGetValue(q.id,out var turn)||turn==ended);
    if(ended==activeTurn)activeTurn="";return before!=questions.Count;
   }
   if(item.Str("type")=="event_msg"&&payload.Str("type")=="item_completed"&&payload.TryGetProperty("item",out var completed)&&completed.Str("delivery")=="async"&&completed.TryGetProperty("questions",out var pending)&&pending.ValueKind==JsonValueKind.Array){
    int i=0;foreach(var q in pending.EnumerateArray()){string id=completed.Str("id")+":"+(i++);if(questions.Any(x=>x.id==id))continue;var options=q.TryGetProperty("options",out var opts)&&opts.ValueKind==JsonValueKind.Array?opts.EnumerateArray().Select(x=>x.GetString()??"").ToArray():Array.Empty<string>();string requestTurn=payload.Str("turn_id") is {Length:>0} turn?turn:activeTurn;questions.Add(new(id,q.Str("title"),options,requestTurn));questionTurns[id]=requestTurn;}
    return true;
   }
   if(item.Str("type")!="response_item"||payload.Str("type")!="message")return false;
   string role=payload.Str("role"),phase=payload.Str("phase");
   if(role!="user"&&(role!="assistant"||phase is not ("commentary" or "final_answer" or "final")))return false;
   if(!payload.TryGetProperty("content",out var content)||content.ValueKind!=JsonValueKind.Array)return false;
   var parts=content.EnumerateArray().ToArray();
   if(role=="user")parts=parts.Where((c,i)=>!(c.Str("type")=="input_text"&&((i+1<parts.Length&&parts[i+1].Str("type")=="input_image"&&c.Str("text").StartsWith("<image name=[Image #",StringComparison.Ordinal))||(i>0&&parts[i-1].Str("type")=="input_image"&&c.Str("text")=="</image>")))).ToArray();
   string text=string.Join("\n",parts.Where(c=>c.Str("type") is "input_text" or "output_text" or "text").Select(c=>c.Str("text")));
   if(string.IsNullOrWhiteSpace(text)){
    if(role=="user"&&parts.Any(p=>p.Str("type")=="input_image")){int before=questions.Count;RemoveQuestions(_=>true);return questions.Count!=before;}
    return false;
   }
   if(role=="user"&&(text.StartsWith("<environment_context>")||text.StartsWith("# AGENTS.md instructions")||text.StartsWith("<permissions instructions>")))return false;
   if(role=="user")RetireQuestionsForUserMessage(text);
   history.Add(new(role,phase,text,++sequence));return true;
  }catch(JsonException){return false;}
 }
 internal static Conversation Parse(string file){var feed=new CodexFeed();feed.ReadAdded(file);return feed.latest!;}
 [StructLayout(LayoutKind.Sequential)]struct UniqueProcess {internal uint Pid;internal System.Runtime.InteropServices.ComTypes.FILETIME Start;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct ProcessInfo {internal UniqueProcess Process;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]internal string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]internal string Service;internal uint Type,Status,Session;[MarshalAs(UnmanagedType.Bool)]internal bool Restartable;}
 [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]static extern int RmStartSession(out uint handle,int flags,StringBuilder key);
 [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)]static extern int RmRegisterResources(uint handle,uint fileCount,string[] files,uint appCount,UniqueProcess[]? apps,uint serviceCount,string[]? services);
 [DllImport("rstrtmgr.dll")]static extern int RmGetList(uint handle,out uint needed,ref uint count,[In,Out] ProcessInfo[]? info,out uint reasons);
 [DllImport("rstrtmgr.dll")]static extern int RmEndSession(uint handle);
 internal static int[] Owners(string file){
  if(RmStartSession(out uint handle,0,new StringBuilder(33))!=0)return Array.Empty<int>();
  try{
   if(RmRegisterResources(handle,1,new[]{file},0,null,0,null)!=0)return Array.Empty<int>();
   uint count=0;int error=RmGetList(handle,out uint needed,ref count,null,out _);
   for(int attempt=0;attempt<4;attempt++){
    if(error!=234&&error!=0||needed>256)return Array.Empty<int>();
    var info=new ProcessInfo[needed];count=needed;
    error=RmGetList(handle,out needed,ref count,info,out _);
    if(error==0)return info.Take((int)count).Select(p=>(int)p.Process.Pid).ToArray();
    // Console readers can open/close the same file between the size and data calls.
    if(error!=234)return Array.Empty<int>();
   }
   return Array.Empty<int>();
  }finally{RmEndSession(handle);}
 }
}
