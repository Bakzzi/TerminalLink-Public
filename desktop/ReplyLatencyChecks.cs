using System.Text.Json;
namespace TerminalLink;
internal static class ReplyLatencyChecks {
 internal static void Run(Action<bool,string> check){
  string path=Path.Combine(AppContext.BaseDirectory,"reply-latency-"+Guid.NewGuid().ToString("N")+".jsonl");
  try{
   string Message(string role,string phase,string text)=>JsonSerializer.Serialize(new{type="response_item",payload=new{type="message",role,phase,content=new[]{new{type="output_text",text}}}})+"\n";
   File.WriteAllText(path,JsonSerializer.Serialize(new{type="session_meta",payload=new{id="latency-fixture"}})+"\n"+Message("user","","A distinctive unchanged question"));
   var record=new LocalDaemonFeed.Record();var before=record.Read(path)!;var linked=new LocalDaemonFeed.Candidate(path,before);int ownerChecks=0;
   LocalDaemonFeed.Candidate? Reload(LocalDaemonFeed.Candidate candidate){ownerChecks++;return candidate with{conversation=record.Read(candidate.path)!};}
   var idle=LocalDaemonFeed.DesktopRefresh(linked,c=>record.Changed(c.path,c.conversation),Reload);
   check(ReferenceEquals(idle,linked)&&ownerChecks==0,"unchanged bound record does not reread history or recheck file ownership");
   File.AppendAllText(path,Message("assistant","final","Completed answer"));
   var watch=System.Diagnostics.Stopwatch.StartNew();var latest=LocalDaemonFeed.DesktopRefresh(linked,c=>record.Changed(c.path,c.conversation),Reload);
   check(ownerChecks==1&&latest?.conversation.messages.LastOrDefault()?.text=="Completed answer"&&before.messages.Length==1,$"changed bound record bypasses catalog wait and preserves original snapshot ({watch.ElapsedMilliseconds} ms)");
   check(LocalDaemonFeed.DesktopRefresh(linked,_=>true,_=>null)==null,"failed owner recheck cannot publish an unverified reply");
   check(LocalDaemonFeed.DesktopRefresh(linked,_=>true,c=>c with{conversation=before with{id="different-thread"}})==null,"changed conversation identity cannot update the old tab badge");
   check(ReferenceEquals(LocalDaemonFeed.DesktopRefresh(latest!,c=>record.Changed(c.path,c.conversation),Reload),latest)&&ownerChecks==1,"unchanged final reply does not repeat parsing or ownership work");
  }finally{if(File.Exists(path))File.Delete(path);}
 }
}
