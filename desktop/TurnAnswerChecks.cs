using System.Text.Json;

namespace TerminalLink;

internal static class TurnAnswerChecks {
 internal static void Run(Action<bool,string> check){
  const string id="00000000-0000-4000-8000-000000000729";
  const string question="Keep the workshop control simple and show one button with its short explanation, preserving the saved preferences.";
  const string first="The workshop button and its short explanation now appear correctly.";
  const string second="The updated package is ready and the saved preferences remain intact.";
  var original=new Conversation(id,new[]{new ConversationMessage("user","",question,1),new ConversationMessage("assistant","commentary",first,2),new ConversationMessage("assistant","commentary",second,3)},false,readOnly:true);
  var candidate=new LocalDaemonFeed.Candidate("owned-fixture.jsonl",original);
  var catalog=new LocalDaemonFeed.Catalog("fixture-owner",new[]{candidate},true);
  string tools=string.Join('\n',Enumerable.Repeat("• Ran synthetic work\n  └ ignored tool payload",600));
  string body="› Keep the workshop control simple and show one button…\n• "+first+"\n"+tools+"\n• "+second;
  string draft="";
  ConsoleFrame Frame(string prefix,string input=""){
   string text=prefix+"\n\n› "+(input.Length==0?"Ask Codex to do anything":input)+"\n? for shortcuts";
   return new("fixture",text,180,text.Count(c=>c=='\n')+1,input.Length+2,text.Split('\n').Length-2,Array.Empty<int>());
  }
  var frame=Frame(body);
  check(TurnAnswerEvidence.Matches(frame,original,0),"separate short progress messages form ordered evidence for the same turn despite long tool output");
  check(LocalDaemonFeed.Match(frame,catalog.candidates)?.id==id&&LocalDaemonFeed.DesktopInputEvidence(catalog,frame,id)!=null,"conversation read and pre-send guard accept the same multi-message turn");
  check(!TurnAnswerEvidence.Matches(Frame(body.Replace("• "+first,"• Ran "+first).Replace("• "+second,"• Ran "+second)),original,0),"tool results containing answer text cannot become turn evidence");
  check(!TurnAnswerEvidence.Matches(Frame("› Question\n• "+first+"\n• "+first),original,0),"repeated short progress does not count twice toward proof strength");
  check(!TurnAnswerEvidence.Matches(Frame("› Question\n• "+second+"\n• "+first),original,0),"progress messages in the wrong source order do not corroborate a turn");
  check(!TurnAnswerEvidence.Matches(Frame("› Question\n• "+first,second),original,0),"unsent composer text cannot supply missing answer evidence");
  var otherTurn=original with{messages=new[]{original.messages[0],original.messages[1],new ConversationMessage("user","","Another question",3),original.messages[2]}};
  check(!TurnAnswerEvidence.Matches(frame,otherTurn,0),"answers from separate submitted questions are never pooled");
  var duplicate=catalog with{candidates=new[]{candidate,candidate with{path="other.jsonl",conversation=original with{id="other"}}}};
  check(LocalDaemonFeed.DesktopInputEvidence(duplicate,frame,id)==null,"matching multi-message evidence in two originals remains ambiguous");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,"different-thread")==null,"the selected thread must match the verified original");
  int pastes=0,enters=0,checks=0;
  ConsoleFrame Read()=>Frame(body,draft);
  Conversation Current(){checks++;if(LocalDaemonFeed.DesktopInputEvidence(catalog,Read(),id)==null)throw new IOException("Original verification failed");return original;}
  var command=JsonSerializer.SerializeToElement(new{request="fixture",kind="message",thread=id,text="Continue with the next task"});
  var result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,Current,t=>{draft=t;pastes++;},(key,alt)=>{if(key=="ENTER")enters++;},command));
  check(result.Bool("ok")&&pastes==1&&enters==1&&checks==3,"ordinary conversation send verifies the real evidence before and after typing and submits exactly once");
  draft="An existing native draft";pastes=0;enters=0;
  result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,Current,t=>pastes++,(key,alt)=>enters++,command));
  check(!result.Bool("ok")&&pastes==0&&enters==0&&draft=="An existing native draft","conversation send preserves an existing terminal draft without keys");
 }
}
