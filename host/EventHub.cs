using System.Text.Json;
using System.Text.RegularExpressions;
namespace TerminalLink;

// One PC reader per terminal, shared by the view and notification path.
internal sealed class EventHub : IDisposable {
 internal sealed class Terminal : IDisposable {
  internal readonly ConsoleBridge Bridge;
  readonly ConversationReader feed=new();int retry;
  internal void Retry()=>Interlocked.Exchange(ref retry,1);
  internal volatile Conversation? Conversation;internal Conversation? LastOriginal;
  internal string Thread="",RequestKey="",StateKey="";
  internal long Sequence,MissingSince,Stamp,RepliedThrough;
  internal NotificationInput.Prompt? Prompt;
  internal bool Active;
  internal Terminal(ConsoleTarget target){Bridge=new(target);}
  public void Dispose()=>Bridge.Dispose();
  internal Conversation? Read(){if(Interlocked.Exchange(ref retry,0)!=0)feed.Retry();return Bridge.Latest is ConsoleFrame frame?feed.Read(frame):null;}
 }
 internal sealed record Notice(string type,string id,string title,Conversation conversation,long baselineSequence,NotificationInput.Prompt? prompt,bool active,string requestKey,long stamp,long repliedThrough=0);
 internal sealed record Entry(long sequence,object value);
 readonly object sync=new();
 readonly Dictionary<string,Terminal> terminals=new();
 readonly List<Entry> journal=new();
 readonly Func<List<TerminalWindow>> allowed;
 readonly CancellationTokenSource life=new();
 readonly Task worker;
 long sequence,stamp;int journalBytes;long lastDiscovery;
 internal Func<bool>? Monitor;
 internal readonly string Epoch=Guid.NewGuid().ToString("N");
 internal Action<Notice>? Notification;
 internal Action<string,Conversation?>? RequestObservation;
 internal EventHub(Func<List<TerminalWindow>> allowed){this.allowed=allowed;worker=Task.Run(Loop);}
 internal Terminal Track(ConsoleTarget target){lock(sync){if(terminals.TryGetValue(target.Id,out var old))return old;if(terminals.Count>=32)throw new IOException("공유 탭은 최대 32개입니다.");var item=new Terminal(target);terminals.Add(target.Id,item);return item;}}
 internal Notice[] Pending(string epoch,long after){lock(sync)return epoch==Epoch?journal.Where(x=>x.sequence>after).Select(x=>x.value).OfType<Notice>().ToArray():Array.Empty<Notice>();}
 internal long Head{get{lock(sync)return sequence;}}
 internal long Stamp(string id){lock(sync)return terminals.TryGetValue(id,out var t)?t.Stamp:0;}
 internal object Snapshot(){lock(sync)return new{type="eventSnapshot",epoch=Epoch,sequence,states=terminals.Values.Where(t=>t.LastOriginal!=null).Select(t=>State(t,t.LastOriginal!.messages.Where(m=>m.role=="assistant"&&(m.phase=="final"||m.phase=="final_answer")).TakeLast(1).ToArray(),t.Sequence)).ToArray()};}
 internal object? Since(ref long after){lock(sync){if(after>sequence||journal.Count>0&&after<journal[0].sequence-1){after=sequence;return Snapshot();}long cursor=after;var items=journal.Where(x=>x.sequence>cursor).Take(32).ToArray();if(items.Length==0)return null;after=items[^1].sequence;return new{type="eventBatch",epoch=Epoch,sequence=after,events=items};}}
 internal static long ReplySequence(Conversation? conversation)=>ConversationReader.IsOriginal(conversation)?conversation!.messages.Where(m=>m.role=="user").Select(m=>m.sequence).DefaultIfEmpty().Max():0;
 static Notice State(Terminal t,ConversationMessage[] answers,long baseline)=>new("replyEvent",t.Bridge.Target.Id,t.Bridge.Latest?.title??t.Bridge.Target.Name,new(t.Thread,answers.Select(DisplayText.Preview).ToArray(),false,t.LastOriginal?.requests??Array.Empty<QueuedQuestion>(),t.LastOriginal?.source??"record",t.LastOriginal?.readOnly??false),baseline,t.Prompt,t.Active,t.RequestKey,t.Stamp,t.RepliedThrough);
 void Add(object item){journal.Add(new(++sequence,item));journalBytes+=JsonSerializer.SerializeToUtf8Bytes(item).Length;while(journal.Count>1&&(journal.Count>256||journalBytes>2*1024*1024)){journalBytes-=JsonSerializer.SerializeToUtf8Bytes(journal[0].value).Length;journal.RemoveAt(0);}}
 async Task Loop(){try{while(!life.IsCancellationRequested){var alerts=new List<Notice>();
  var permitted=allowed();
  if(Monitor?.Invoke()==true&&Environment.TickCount64-lastDiscovery>5000){lastDiscovery=Environment.TickCount64;foreach(var target in ConsoleNative.Targets(permitted).Take(32))try{Track(target);}catch(Exception)when(!life.IsCancellationRequested){}}
  KeyValuePair<string,Terminal>[] tracked;lock(sync)tracked=terminals.ToArray();
  foreach(var pair in tracked){
   var t=pair.Value;try{
   bool closed=t.Bridge.Closed||!t.Bridge.Target.Valid||!permitted.Any(w=>w.Handle==t.Bridge.Target.Window.Handle&&w.Pid==t.Bridge.Target.Window.Pid);
   var frame=t.Bridge.Latest;
   // Disk/owner discovery can be slow. Never hold the connection's event-state
   // lock while reading an original record; publish the result atomically below.
   var conversation=!closed&&frame!=null?t.Read():null;
   RequestObservation?.Invoke(pair.Key,conversation);
   lock(sync){
   if(life.IsCancellationRequested||!terminals.TryGetValue(pair.Key,out var current)||!ReferenceEquals(t,current))continue;
   if(closed){
    terminals.Remove(pair.Key);t.Dispose();Add(new{type="replyWatchClosed",id=pair.Key});continue;
   }
   if(frame==null)continue;
   // Only verified original records may drive conversation notifications.
   // A temporary read miss is not a provider completion. Retain request and
   // notification identity until an authoritative record or closed console arrives.
   if(conversation==null||!ConversationReader.IsOriginal(conversation)){
    t.Conversation=conversation;
    continue;
   }
   long baseline=t.Thread==conversation.id?t.Sequence:conversation.messages.Select(m=>m.sequence).DefaultIfEmpty().Max();
   var answers=t.Thread==conversation.id?conversation.messages.Where(m=>m.sequence>baseline&&m.role=="assistant"&&(m.phase=="final"||m.phase=="final_answer")).ToArray():Array.Empty<ConversationMessage>();
   var prompt=NotificationInput.Parse(frame.text);
   var pending=conversation.requests??Array.Empty<QueuedQuestion>();
   if(prompt?.followup==true){var q=pending.FirstOrDefault(q=>NotificationInput.Compact(q.title)==NotificationInput.Compact(prompt.question));if(q==null)prompt=null;else if(prompt.choices.Length==0)prompt=NotificationInput.Parse(frame.text,q.title)??prompt;}
   if(prompt==null&&pending.Length>0){var q=pending[0];var choices=q.options.Length>0?q.options.Concat(new[]{"직접 입력"}).ToArray():Array.Empty<string>();prompt=new(q.title,choices,choices.Length>0?0:-1,true,true,choices.Length==0,"");}
   string requestKey=prompt?.followup==true?pending.FirstOrDefault(q=>NotificationInput.Compact(q.title)==NotificationInput.Compact(prompt.question))?.id??"":"";
   bool active=prompt!=null||Regex.IsMatch(frame.title,@"(?i)^\s*(?:\[[^\]]{1,12}\]\s*)?Action Required");
   if(!active&&t.Active){if(t.MissingSince==0)t.MissingSince=Environment.TickCount64;if(Environment.TickCount64-t.MissingSince<1800){active=true;prompt=t.Prompt;requestKey=t.RequestKey;}}else t.MissingSince=0;
   // A recorded user reply changes notification state even before the next answer exists.
   // Original sequences preserve the boundary when replies and answers arrive in one batch.
   long repliedThrough=ReplySequence(conversation);
   string stateKey=JsonSerializer.Serialize(new{conversation.id,repliedThrough,active,requestKey,question=prompt?.question,choices=prompt?.choices,followup=prompt?.followup,questionKind=prompt?.questionKind,pending});
   bool changed=stateKey!=t.StateKey;
   t.Thread=conversation.id;t.Sequence=conversation.messages.Select(m=>m.sequence).DefaultIfEmpty().Max();t.Conversation=conversation;t.LastOriginal=conversation;t.Prompt=prompt;t.Active=active;t.RequestKey=requestKey;t.RepliedThrough=repliedThrough;
   if(changed||answers.Length>0){t.StateKey=stateKey;t.Stamp=stamp=Math.Max(stamp+1,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());var notice=State(t,answers,baseline);Add(notice);alerts.Add(notice);}
   }}catch(Exception)when(!life.IsCancellationRequested){}
  }
 foreach(var alert in alerts)try{Notification?.Invoke(alert);}catch{}await Task.Delay(1000,life.Token);}}catch(OperationCanceledException){} }
 public void Dispose(){life.Cancel();try{worker.Wait(3000);}catch{}lock(sync){foreach(var t in terminals.Values)t.Dispose();terminals.Clear();journal.Clear();}life.Dispose();}
}
