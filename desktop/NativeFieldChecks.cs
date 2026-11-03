#if DESKTOP || TESTING
using System.Text.Json;
namespace TerminalLink;
internal static class NativeFieldChecks {
 internal const string Question="Allow download from https://example.invalid/track?";
 internal static string Text(int selected=0,string field="1/1",int? unanswered=null)=>"› A distinctive original question about choosing a demonstration asset\n• I found the matching asset and am checking permission before downloading it.\n\n  Field "+field+(unanswered is int n?$" ({n} required unanswered)":"")+"\n  "+Question+"\n\n  origin: https://example.invalid\n\n"+string.Join("\n",new[]{"Allow                   Run the tool and continue","Allow for this session  Run the tool and remember this choice for this session","Always allow            Run the tool and remember this choice for future tool calls","Cancel                  Cancel this tool call"}.Select((s,i)=>(i==selected?"  › ":"    ")+(i+1)+". "+s))+"\n  enter to submit | esc to cancel";
 internal static ConsoleFrame Frame(string? text=null)=>new("fixture",text??Text(),120,25,2,8,Array.Empty<int>());
 internal static void Run(Action<bool,string> check){
  var prompt=NotificationInput.Parse(Text());
  check(prompt!=null&&prompt.choices.Length==4&&!prompt.followup&&!prompt.questionKind,"native Field approval is detected as selection-only request");
  check(prompt!.question.Contains(Question)&&prompt.question.Contains("origin: https://example.invalid"),"approval preserves exact requested URL and origin");
  check(prompt.choices[1].Contains("this session")&&prompt.choices[2].Contains("future tool calls")&&prompt.choices[3].StartsWith("Cancel"),"all permission scopes and cancellation remain visible");
  check(NativeFieldPrompt.Transcript(Text()).EndsWith("\n\n")&&!NativeFieldPrompt.Transcript(Text()).Contains("Allow download"),"approval editor is excluded from submitted-question evidence");
  check(NativeFieldPrompt.Transcript(Text().Replace("\n","\r\n"))==NativeFieldPrompt.Transcript(Text()),"CRLF approval boundaries retain the same transcript");
  var next=NotificationInput.Parse(Text(1,"2/2"));
  check(next?.selected==1&&next.question.StartsWith("Field 2/2"),"multiple fields retain current field identity and selection");
  foreach(int remaining in new[]{0,1}){
   var changed=NotificationInput.Parse(Text(1,unanswered:remaining));
   check(changed?.selected==1&&changed.question==prompt.question&&changed.choices.SequenceEqual(prompt.choices),"unanswered counter is presentation state, not a changed permission request");
   check(NativeFieldPrompt.Transcript(Text(1,unanswered:remaining))==NativeFieldPrompt.Transcript(Text()),"validation counter does not leak permission editor into conversation matching");
  }
  check(NotificationInput.Parse(Text(1,"2/3",2))?.question.StartsWith("Field 2/3") == true,"remaining count can cover other required fields without changing the active field");
  foreach(string invalid in new[]{Text(unanswered:2),Text(unanswered:-1),Text().Replace("Field 1/1","Field 1/1 (99999999999999 required unanswered)"),Text().Replace("Field 1/1","Field 1/1 (changed request)")})
   check(NativeFieldPrompt.Parse(invalid)==null,"invalid counters and unknown field status are rejected");
  foreach(string invalid in new[]{Text().Replace("esc to cancel","esc to interrupt"),Text()+"\n› Ask Codex to do anything\n? for shortcuts",Text().Replace("    2.","    3."),Text().Replace("    2.","  › 2."),Text().Replace("  › 1.","    1."),Text().Replace("Field 1/1","Field 2/1"),Text().Replace("Field 1/1","Field 1/99")})
   check(NativeFieldPrompt.Parse(invalid)==null,"incomplete, historical, malformed or ambiguous approval is rejected");
  string wrapped=Text().Replace("Run the tool and remember this choice for future tool calls","Run the tool and remember this choice\n        for future tool calls");
  check(NotificationInput.Parse(wrapped)?.choices[2]==prompt.choices[2],"wrapped permission descriptions preserve their full scope");
  var original=new Conversation("field-fixture",new[]{new ConversationMessage("user","","A distinctive original question about choosing a demonstration asset",1)},false);
  check(LocalDaemonFeed.Match(Frame(),new[]{new LocalDaemonFeed.Candidate("fixture",original)})?.id==original.id,"approval text does not invalidate actual submitted-question identity");
  check(LocalDaemonFeed.Match(Frame(),new[]{new LocalDaemonFeed.Candidate("one",original),new LocalDaemonFeed.Candidate("two",original with{id="duplicate"})})==null,"new approval format never bypasses duplicate original rejection");
  JsonElement Command(int choice,string text="")=>JsonSerializer.SerializeToElement(new{request="fixture",kind="request",thread=original.id,requestKey="",question=prompt.question,choices=prompt.choices,choice,text});
  for(int choice=0;choice<4;choice++){
   int selected=0,enters=0,pastes=0;bool moved=false;
   var result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(()=>Frame(Text(selected,unanswered:moved?1:null)),()=>original,_=>pastes++,(key,alt)=>{if(key=="DOWN"){selected++;moved=true;}if(key=="UP"){selected--;moved=true;}if(key=="ENTER")enters++;},Command(choice)));
   check(result.Bool("ok")&&selected==choice&&enters==1&&pastes==0,"every explicit option submits once when navigation adds the unanswered counter");
  }
  int navigations=0,unexpectedEnter=0;
  var altered=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(()=>Frame(navigations==0?Text():Text(1,unanswered:1).Replace("example.invalid","changed.invalid")),()=>original,_=>{},(key,alt)=>{if(key=="DOWN")navigations++;if(key=="ENTER")unexpectedEnter++;},Command(1)));
  check(!altered.Bool("ok")&&navigations==1&&unexpectedEnter==0,"a real origin change during selection still blocks Enter");
  int keys=0;var refused=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(()=>Frame(),()=>original,_=>keys++,(k,a)=>keys++,Command(-1,"Allow")));
  check(!refused.Bool("ok")&&keys==0,"free text cannot silently approve a permission prompt");
  refused=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(()=>Frame(Text().Replace("example.invalid","changed.invalid")),()=>original,_=>keys++,(k,a)=>keys++,Command(0)));
  check(!refused.Bool("ok")&&keys==0,"changed permission origin rejects the old answer before input");
  refused=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(()=>Frame(Text(0,"2/2")),()=>original,_=>keys++,(k,a)=>keys++,Command(0)));
  check(!refused.Bool("ok")&&keys==0,"a different active field rejects an old field answer");
 }
}
#endif
