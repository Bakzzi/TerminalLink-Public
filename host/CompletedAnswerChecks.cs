#if DESKTOP || TESTING
using System.Text.Json;
namespace TerminalLink;
internal static class CompletedAnswerChecks {
 internal static void Run(Action<bool,string> check){
  const string id="00000000-0000-4000-8000-000000000987";
  const string answer="The independent workshop completed the orange drawing and verified every original measurement before delivering the finished design.\n\nThe saved preferences and all unfinished drafts were preserved across the installation and the verified previous release remains available for recovery.";
  Conversation Chat(string text)=>new(id,new[]{new ConversationMessage("user","","An old question outside the viewport",1),new ConversationMessage("assistant","final",text,2)},false,readOnly:true);
  ConsoleFrame Frame(string body,string marker="• "){
   string text="  + 9000 lines (ctrl+t to expand)\n\n"+marker+body+"\n\n  Worked for 44m 0s\n\n› Ask Codex to do anything\n? for shortcuts";
   var lines=text.Split('\n');return new("fixture",text,180,lines.Length,2,lines.Length-2,Array.Empty<int>());
  }
  var chat=Chat(answer);var frame=Frame(answer);var candidate=new LocalDaemonFeed.Candidate("fixture",chat);
  check(CompletedAnswerEvidence.Match(frame,new[]{chat})==chat,"final answer uniquely identifies original after every question scrolls away");
  check(LocalDaemonFeed.Match(frame,new[]{candidate})==chat,"new cold reader discovers full original from completed answer evidence");
#if DESKTOP
  var catalog=new LocalDaemonFeed.Catalog("fixture",new[]{candidate},true);
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,id)!=null,"desktop read and send accept the same final answer evidence");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,Guid.NewGuid().ToString())==null,"completed answer does not authorize a different requested target");
#endif
  check(CompletedAnswerEvidence.Match(frame,new[]{chat,chat with{id="duplicate"}})==null,"duplicate final answers remain ambiguous");
  check(CompletedAnswerEvidence.Match(frame,new[]{chat,Chat("Different original answer") with{id="other"}})==chat,"other live tabs do not block distinctive completed answer");
  check(CompletedAnswerEvidence.Match(Frame("Done."),new[]{Chat("Done.")})==null,"short generic completion cannot authorize input");
  check(CompletedAnswerEvidence.Match(Frame(answer.Replace("orange","purple")),new[]{chat})==null,"conflicting final answer is rejected");
  check(CompletedAnswerEvidence.Match(Frame(answer,"• Ran "),new[]{chat})==null,"tool output cannot stand in for a final answer");
  check(CompletedAnswerEvidence.Match(Frame(answer,"  "),new[]{chat})==null,"unlabelled clipped output is not authority");
  check(CompletedAnswerEvidence.Match(frame with{text=frame.text.Replace("Worked for 44m 0s","Working (44m 0s • esc to interrupt)")},new[]{chat})==null,"unfinished output is not completed answer evidence");
  check(CompletedAnswerEvidence.Match(frame with{text=frame.text.Replace("› Ask Codex to do anything","› unsent draft"),cursorX=14},new[]{chat})==null,"native composer draft is preserved");
  check(CompletedAnswerEvidence.Match(frame,new[]{chat with{messages=chat.messages.Append(new("user","","A newer question",3)).ToArray()}})==null,"stale final cannot bind a record with a newer question");
  check(CompletedAnswerEvidence.Match(frame,new[]{chat with{messages=chat.messages.Select(m=>m.role=="assistant"?m with{phase="commentary"}:m).ToArray()}})==null,"commentary cannot authorize completed answer send");
  check(CompletedAnswerEvidence.Match(frame,new[]{chat with{requests=new[]{new QueuedQuestion("q","Pending request",Array.Empty<string>())}}})==null,"pending request cannot become ordinary message authority");
  check(CompletedAnswerEvidence.Match(frame,new[]{chat with{source="terminal"}})==null,"visible screen history is never an original record");
  check(CompletedAnswerEvidence.Match(frame with{text="› Ask Codex to do anything\n? for shortcuts",cursorY=0},new[]{chat})==null,"empty viewport cannot authorize input");
  string link="[orange drawing](https://example.invalid/orange)";
  var linked=Chat(answer.Replace("orange drawing",link));
  check(CompletedAnswerEvidence.Match(frame,new[]{linked})==linked,"rendered Markdown links preserve complete answer matching");
  check(CompletedAnswerEvidence.Match(frame,new[]{linked,linked with{id="duplicate",messages=linked.messages.Select(m=>m with{text=m.text.Replace("example.invalid","different.invalid")}).ToArray()}})==null,"hidden link destinations cannot break visible ambiguity");
  string wrapped=answer.Replace("verified every", "verified\n  every");
  check(CompletedAnswerEvidence.Match(Frame(wrapped),new[]{chat})==chat,"terminal wrapping retains ordered answer evidence");
  string shortLine=answer.Split('\n')[0];
  check(CompletedAnswerEvidence.Match(Frame(shortLine),new[]{Chat(shortLine)})==null,"one medium line does not meet the stronger unanchored threshold");
  int enters=0;string pasted="";
  var message=JsonSerializer.SerializeToElement(new{request="fixture",kind="answer",sequence=chat.messages[^1].sequence,thread=id,text="Next question"});
  ConsoleFrame Editing()=>Frame(answer) with{text=frame.text.Replace("Ask Codex to do anything",pasted.Length==0?"Ask Codex to do anything":pasted),cursorX=pasted.Length==0?2:pasted.Length+2};
  var result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Editing,()=>chat,t=>pasted=t,(key,alt)=>{if(key=="ENTER")enters++;},message));
  check(result.Bool("ok")&&pasted=="Next question"&&enters==1,"verified completed target submits exactly once in isolated input fixture");
 }
}
#endif
