using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace TerminalLink;

// Display-only daemon adapter. Original direct-console checks still guard writes.
internal static partial class LocalDaemonFeed {
 internal sealed record Candidate(string path,Conversation conversation);
 internal sealed record Catalog(string daemon,Candidate[] candidates,bool available);
 internal sealed class Record {
  readonly object sync=new();readonly CodexFeed feed=new();long length=-1;DateTime write;
  internal Conversation? Read(string path){lock(sync){var info=new FileInfo(path);if(info.Length!=length||info.LastWriteTimeUtc!=write){feed.ReadAdded(path);length=info.Length;write=info.LastWriteTimeUtc;}return View();}}
  internal Conversation? original,view;
  Conversation? View(){var next=feed.Snapshot;if(next!=original){original=next;view=next is null?null:next with{readOnly=true};}return view;}
 }
 static readonly string home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
 static readonly object gate=new();static readonly System.Collections.Concurrent.ConcurrentDictionary<string,Record> records=new(StringComparer.OrdinalIgnoreCase);
 static readonly Dictionary<string,string> bindings=new();
 static LocalBindingStore? saved;static LocalBindingStore Saved=>saved??=new(Storage());
 static string Storage(){
#if TESTING
  if(Environment.GetEnvironmentVariable("TERMINALLINK_TEST_BINDING_STORE") is string folder&&folder.Length>0)return folder;
#endif
  return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","daemon-record-links-v1");
 }
 internal static string Diagnostic="not-started";
 internal static bool CatalogReady {get{Catalog snapshot;lock(gate){if(refreshing)return false;snapshot=catalog;}return snapshot.available&&Daemon()?.key==snapshot.daemon;}}
 static Catalog catalog=new("",Array.Empty<Candidate>(),false);static bool refreshing;static long nextRefresh;
 internal static string Identity(ConsoleFrame frame,bool includeSsh=false){
  var peers=new List<string>();
  foreach(int pid in frame.peers)try{using var p=Process.GetProcessById(pid);if((p.ProcessName.Equals("codex",StringComparison.OrdinalIgnoreCase)||includeSsh&&p.ProcessName.Equals("ssh",StringComparison.OrdinalIgnoreCase))&&TerminalWindows.SameUser(p))peers.Add(pid+":"+p.StartTime.ToUniversalTime().Ticks);}catch{}
  return peers.Count==1?frame.session+"/"+peers[0]:"";
 }
 internal static void Forget(string identity){if(identity.Length==0)return;lock(gate){bindings.Remove(identity);Saved.Forget(identity);}}
 internal static Conversation? Read(ConsoleFrame frame){
  string identity=Identity(frame);if(identity.Length==0)return null;
  if(ShellActive(frame)||Regex.IsMatch(frame.text,@"(?im)^\s*Side from main thread\b")){Forget(identity);return null;}
  Catalog snapshot;lock(gate){snapshot=catalog;if(!refreshing&&Environment.TickCount64>=nextRefresh){refreshing=true;_=Task.Run(Refresh);}}
  if(!snapshot.available)return null;
  // The daemon must still be exactly the process that verified this catalog.
  if(Daemon()?.key!=snapshot.daemon)return null;
  // A new submitted prompt can precede the next two-second catalog publication.
  // Refresh only the already-verified record before treating that prompt as a different chat.
  Conversation? linkedBefore;lock(gate){
   var found=bindings.TryGetValue(identity,out var bound)?snapshot.candidates.Where(c=>c.conversation.id==bound).ToArray():Array.Empty<Candidate>();
   linkedBefore=found.Length==1?found[0].conversation:found.Length>1?null:Saved.Restore(identity,snapshot.daemon,snapshot.candidates.Select(c=>c.conversation));
  }
  if(linkedBefore!=null){
   var linkedCandidate=snapshot.candidates.Single(c=>c.conversation.id==linkedBefore.id);
   var fresh=FreshForFrame(frame,linkedCandidate,c=>ReloadVerified(c,snapshot.daemon));
   if(fresh==null)return null;
   if(!ReferenceEquals(fresh,linkedCandidate))snapshot=snapshot with{candidates=snapshot.candidates.Select(c=>ReferenceEquals(c,linkedCandidate)?fresh:c).ToArray()};
  }
  var match=ScreenConversation.Recognizes(frame)?Match(frame,snapshot.candidates):null;
  if(match==null&&NativeFieldPrompt.Parse(frame.text)!=null)match=Match(frame,snapshot.candidates);
  lock(gate){
   if(match!=null){bindings[identity]=match.id;Saved.Remember(identity,snapshot.daemon,match);return match;}
   var linked=bindings.TryGetValue(identity,out var thread)?snapshot.candidates.Where(c=>c.conversation.id==thread).ToArray():Array.Empty<Candidate>();
   var prior=linked.Length==1?linked[0].conversation:linked.Length>1?null:Saved.Restore(identity,snapshot.daemon,snapshot.candidates.Select(c=>c.conversation));
   if(prior==null)return null;
   // During redraw there may be no anchors. Conflicting visible questions invalidate this link.
   if(!Compatible(frame,prior)){bindings.Remove(identity);Saved.Forget(identity);return null;}
   bindings[identity]=prior.id;Saved.Remember(identity,snapshot.daemon,prior);return prior;
  }
 }
 // The callback must freshly verify the bound record's owner. No viewport or old-chat fallback.
 internal static Candidate? FreshForFrame(ConsoleFrame frame,Candidate linked,Func<Candidate,Candidate?> reload){
  if(Compatible(frame,linked.conversation))return linked;
  var fresh=reload(linked);
  return fresh?.conversation.id==linked.conversation.id?fresh:null;
 }
 static Candidate? ReloadVerified(Candidate candidate,string daemon){
  try{
   var current=Daemon();if(current?.key!=daemon||!CodexFeed.Owners(candidate.path).Contains(current.Value.pid))return null;
   if(!records.TryGetValue(candidate.path,out var record))return null;
   var latest=record.Read(candidate.path);
   if(Daemon()?.key!=daemon||latest?.id!=candidate.conversation.id)return null;
   return ReferenceEquals(latest,candidate.conversation)?candidate:new(candidate.path,latest);
  }catch{return null;}
 }
 // A write additionally needs a current, distinctive visible question that identifies one live original.
 // A clipped current question may have no final answer yet; the previously verified TUI binding is retained.
 internal static bool CorroboratesPhoto(ConsoleFrame frame,string thread){
  var anchors=Anchors(frame);
  if(anchors.Length==0){
   // Refresh every live owner, not only the saved display link, before sending.
   Refresh();Catalog fresh;lock(gate)fresh=catalog;
   return fresh.available&&Daemon()?.key==fresh.daemon
    &&CompletedAnswerEvidence.Match(frame,fresh.candidates.Select(c=>c.conversation))?.id==thread;
  }
  Catalog current;lock(gate)current=catalog;
  if(!current.available||Daemon()?.key!=current.daemon)return false;
  var matches=current.candidates.Where(c=>Compatible(frame,c.conversation)).ToArray();
  return matches.Length==1&&matches[0].conversation.id==thread;
 }
 internal static bool ShellActive(ConsoleFrame frame){
  var rows=frame.text.Replace("\r","").Split('\n');string line=frame.cursorY>=0&&frame.cursorY<rows.Length?rows[frame.cursorY].TrimStart():"";
  return Regex.IsMatch(line,@"^(?:PS\s+.+>|[A-Za-z]:\\[^>]*>|[\w.@-]+(?::[^\r\n$#]*)?[$#])(?:\s|$)");
 }
 static bool Alive(string identity){
  try{var parts=identity[(identity.LastIndexOf('/')+1)..].Split(':');using var p=Process.GetProcessById(int.Parse(parts[0]));return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==long.Parse(parts[1]);}catch{return false;}
 }
 internal static string Normal(string value)=>Regex.Replace(value,@"\s+","");
 sealed record Anchor(HashSet<string> exact,HashSet<string> prefixes,bool folded,string visible);
 // Exclude an active request editor, including selected options and an unsent answer.
 // Its transcript prefix remains evidence; the editor alone cannot create a binding.
 static string Transcript(string text){
  text=NativeFieldPrompt.Transcript(text);
  if(NotificationInput.Parse(text)==null)return text;
  var headers=Regex.Matches(text,@"(?m)^\s*(?:(?:•\s*)?Queued follow-up inputs|Question\s+\d+/\d+[^\n]*)\s*$");
  return headers.Count==0?text:text[..headers[^1].Index];
 }
 static Anchor[] Anchors(ConsoleFrame frame){
  var evidence=new List<Anchor>();var lines=Transcript(frame.text.Replace("\r","")).Split('\n');
  // An unsent composer is not identity evidence and must not invalidate a live binding.
  int lastPrompt=Array.FindLastIndex(lines,l=>l.StartsWith("› "));
  bool composer=lastPrompt>=0&&!lines.Skip(lastPrompt+1).Any(l=>l.StartsWith("• "))
   &&(frame.cursorY>=lastPrompt&&frame.cursorY<=lastPrompt+6||lines.Skip(lastPrompt+1).Any(l=>Regex.IsMatch(l,@"(?i)(?:\? for shortcuts|gpt-[\w.-]+\s|esc to interrupt)")));
  for(int i=0;i<lines.Length;i++){
   if(!lines[i].StartsWith("› ")||composer&&i==lastPrompt)continue;
   var exact=new HashSet<string>(StringComparer.Ordinal);var prefixes=new HashSet<string>(StringComparer.Ordinal);
   string value=Normal(lines[i][2..]);if(value.StartsWith("AskCodex",StringComparison.Ordinal)||value.StartsWith("Askafollow-up",StringComparison.Ordinal))continue;
   bool folded=false,clippedEnd=false;
   // A pinned/clipped prompt can be followed by unrelated tool output. Stop at
   // its ellipsis; short prefixes require the same strong answer proof as folds.
   void Add(){if(value.Length>0)exact.Add(value);string prefix=value.EndsWith('…')?value[..^1]:value.EndsWith("...",StringComparison.Ordinal)?value[..^3]:"";if(prefix.Length>0){clippedEnd=true;if(prefix.Length>=8){prefixes.Add(prefix);folded|=prefix.Length<40;}}}
   Add();
   for(int j=i+1;!clippedEnd&&j<lines.Length&&!lines[j].StartsWith("› ")&&!lines[j].StartsWith("• ");j++){
    if(Regex.IsMatch(lines[j],@"^(?:◦|✦|✧)\s")||Regex.IsMatch(lines[j],@"^\s*(?:[─━]\s*)?Worked for\s+\d",RegexOptions.IgnoreCase))break;
    if(Regex.IsMatch(lines[j],@"^\s*\+ Show details\s*$"))break;
    if(Regex.IsMatch(lines[j],@"^\s*\+ \d+ lines? \(ctrl\+t to expand\)\s*$")){folded=true;string shown=value.EndsWith('…')?value[..^1]:value.EndsWith("...",StringComparison.Ordinal)?value[..^3]:value;if(shown.Length>=8)prefixes.Add(shown);break;}
    value+=Normal(lines[j]);Add();
   }
   // A pinned question may sit directly above the middle of an older answer.
   // Keep complete original-message matches at each line boundary, as well as
   // multiline forms across blank paragraphs. Multiple matching records remain
   // ambiguous: never select a record merely because its prefix is longer.
   if(exact.Count>0){int end=i+1;while(end<lines.Length&&!lines[end].StartsWith("› "))end++;evidence.Add(new(exact,prefixes,folded,string.Join("\n",lines.Skip(i).Take(end-i))));}
  }
  return evidence.TakeLast(3).ToArray();
 }
 // Only identity evidence: the original stored user message is never rewritten.
 internal static IEnumerable<string> UserForms(string text){
  yield return Normal(text);
  const string open="<send_user_message_question_reply>",close="</send_user_message_question_reply>";
  string value=text.Trim();if(!value.StartsWith(open,StringComparison.Ordinal)||!value.EndsWith(close,StringComparison.Ordinal))yield break;
  string? rendered=null;
  try{
   using var doc=JsonDocument.Parse(value[open.Length..^close.Length]);
   if(doc.RootElement.ValueKind!=JsonValueKind.Array||doc.RootElement.GetArrayLength()!=1)yield break;
   var item=doc.RootElement[0];
   if(item.TryGetProperty("question",out var question)&&question.ValueKind==JsonValueKind.String&&item.TryGetProperty("answer",out var answer)&&answer.ValueKind==JsonValueKind.String&&question.GetString() is {Length:>0} q&&answer.GetString() is {Length:>0} a)
    rendered=Normal("> "+q+"\n\n"+a);
  }catch(JsonException){}
  if(rendered!=null)yield return rendered;
 }
 static bool Accepts(Anchor a,string user,out bool clipped){clipped=false;if(a.exact.Contains(user))return true;clipped=a.prefixes.Any(p=>user.StartsWith(p,StringComparison.Ordinal));return clipped;}
 static string Visible(string value)=>Normal(Regex.Replace(value,@"[*`_]",""));
 static bool FollowingAnswer(ConsoleFrame frame,Conversation candidate,int userIndex,bool strong=false){
  string displayed=ScreenConversation.Extract(frame).LastOrDefault(m=>m.role=="assistant")?.text??"";
  string first=Visible(displayed.Split('\n').FirstOrDefault()??"");
  var lines=frame.text.Replace("\r","").Split('\n');int prompt=Array.FindLastIndex(lines,l=>l.StartsWith("› ")&&!l.StartsWith("› Ask Codex",StringComparison.Ordinal)&&!l.StartsWith("› Ask a follow-up",StringComparison.Ordinal));
  var prose=new List<string>();
  for(int i=prompt+1;prompt>=0&&i<lines.Length;i++){
   string line=lines[i].Trim();if(line.StartsWith("› ")||Regex.IsMatch(line,@"^(?:[─━]\s*)?Worked for\b",RegexOptions.IgnoreCase))break;
   if(line.Contains('|')||line.Any(c=>c>='\u2500'&&c<='\u257f')||line.StartsWith('<')||line.StartsWith("↳"))continue;
   string value=Visible(Regex.Replace(line,@"^[•-]\s+",""));if(value.Length>=24&&!prose.Contains(value))prose.Add(value);
  }
  for(int i=userIndex+1;i<candidate.messages.Length&&candidate.messages[i].role!="user";i++){
   var message=candidate.messages[i];if(message.role!="assistant"||message.phase is not ("final" or "final_answer" or "commentary"))continue;
   string original=Visible(message.text);
   if(message.phase!="commentary"&&!strong&&first.Length>=24&&original.StartsWith(first,StringComparison.Ordinal))return true;
   int at=0,hits=0,total=0;foreach(string part in prose){int found=original.IndexOf(part,at,StringComparison.Ordinal);if(found<0)continue;hits++;total+=part.Length;at=found+part.Length;}
   // Evidence strength must not depend on terminal width: a single unwrapped
   // line needs at least 80 matching characters from the same original answer.
   if(hits>=2&&total>=64||hits==1&&total>=80)return true;
  }
  return false;
 }
 internal static bool Compatible(ConsoleFrame frame,Conversation candidate){
  var anchors=Anchors(frame);int at=0;
  foreach(var message in candidate.messages)if(message.role=="user"&&at<anchors.Length&&UserForms(message.text).Any(user=>Accepts(anchors[at],user,out _)))at++;
  return at==anchors.Length;
 }
 internal static Conversation? Match(ConsoleFrame frame,IEnumerable<Candidate> candidates){
  var anchors=Anchors(frame);if(anchors.Length==0)return CompletedAnswerEvidence.Match(frame,candidates.Select(c=>c.conversation));
  var matched=new List<Conversation>();
  foreach(var candidate in candidates){
   int at=0,last=-1,lastUncorroborated=-1,lastIndependent=-1;bool clipped=false,folded=false,corroborated=true;var hits=new List<string>();
   for(int i=0;i<candidate.conversation.messages.Length;i++){
    var message=candidate.conversation.messages[i];if(message.role!="user"||at>=anchors.Length)continue;
    string? user=UserForms(message.text).FirstOrDefault(form=>Accepts(anchors[at],form,out _));if(user!=null&&Accepts(anchors[at],user,out bool partial)){hits.Add(user);clipped|=partial;folded|=anchors[at].folded;if(partial||anchors[at].folded){bool proved=FollowingAnswer(frame with{text=anchors[at].visible},candidate.conversation,i,anchors[at].folded);corroborated&=proved;if(!proved)lastUncorroborated=i;}else if(user.Length>=20)lastIndependent=i;last=i;at++;}
   }
   // An older pinned prefix may outlive its answer on screen. A later complete,
   // distinctive question independently identifies the record, while every older
   // anchor must still match in order and duplicate candidates remain rejected.
   if(at==anchors.Length&&(hits.Where(h=>h.Length>=8).Distinct().Count()>=2||hits[^1].Length>=20||folded&&hits[^1].Length>=8||FollowingAnswer(frame,candidate.conversation,last,true))&&(!(clipped||folded)||corroborated||lastIndependent>lastUncorroborated))matched.Add(candidate.conversation);
  }
  return matched.Count==1?matched[0]:null;
 }
 static (int pid,long started,string key)? Daemon(){
  try{using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(home,"app-server-daemon","daemon.pid")));
   int pid=doc.RootElement.GetProperty("pid").GetInt32();long started=long.Parse(doc.RootElement.Str("processStartTime"));using var p=Process.GetProcessById(pid);
   if(p.HasExited||p.StartTime.ToUniversalTime().ToFileTimeUtc()!=started||!p.ProcessName.Equals("codex",StringComparison.OrdinalIgnoreCase)||!TerminalWindows.SameUser(p))return null;
   return(pid,started,pid+":"+started);
  }catch{return null;}
 }
 static void Refresh(){
  Catalog result=new("",Array.Empty<Candidate>(),false);
  try{
   var daemon=Daemon();if(daemon==null)return;
   string root=Path.Combine(home,"sessions"),locks=Path.Combine(home,"thread-writer-locks");if(!Directory.Exists(root)||!Directory.Exists(locks))return;
   var ids=Directory.EnumerateFiles(locks,"*.lock").Where(p=>Guid.TryParse(Path.GetFileNameWithoutExtension(p),out _)&&CodexFeed.Owners(p).Contains(daemon.Value.pid)).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
   // Inspect live writer locks, then verify ownership of the actual rollout too.
   var paths=Directory.EnumerateFiles(root,"rollout-*.jsonl",SearchOption.AllDirectories).Where(p=>Path.GetFileNameWithoutExtension(p) is string n&&n.Length>=36&&ids.Contains(n[^36..])).ToArray();
   var live=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var candidates=new List<Candidate>();
   foreach(string path in paths){
    if(!CodexFeed.Owners(path).Contains(daemon.Value.pid))continue;
    using(var input=new StreamReader(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))){using var meta=JsonDocument.Parse(input.ReadLine()??"{}");var payload=meta.RootElement.GetProperty("payload");if(meta.RootElement.Str("type")!="session_meta"||!CodexFeed.InteractiveOrigin(payload)||!ids.Contains(payload.Str("id"))||!Path.GetFileNameWithoutExtension(path).EndsWith(payload.Str("id"),StringComparison.OrdinalIgnoreCase))continue;}
    live.Add(path);var record=records.GetOrAdd(path,_=>new());
    if(record.Read(path) is Conversation c)candidates.Add(new(path,c));
   }
   foreach(string path in records.Keys.Where(p=>!live.Contains(p)).ToArray())records.TryRemove(path,out _);
   if(Daemon()?.key==daemon.Value.key){result=new(daemon.Value.key,candidates.ToArray(),true);Diagnostic="verified-records:"+candidates.Count;}
  }catch(Exception e){Diagnostic=e.GetType().Name; /* A failed catalog is never published as ready history. */ }
  finally{lock(gate){foreach(string key in bindings.Keys.Where(k=>!Alive(k)).ToArray()){bindings.Remove(key);Saved.Forget(key);}catalog=result;nextRefresh=Environment.TickCount64+2000;refreshing=false;}}
 }
}
