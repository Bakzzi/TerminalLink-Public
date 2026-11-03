using System.Text.Json;
namespace TerminalLink;

internal static class ShortFollowupChecks {
 internal static void Run(Action<bool,string> check){
  const string id="00000000-0000-4000-8000-000000000712";
  const string earlier="Review the previous independent workshop deliverables and preserve the saved preferences before moving to the next task.";
  const string answer="I will continue the independent workshop integration and verify the original measurement records before connecting the new drawing processor and presenting its current activity.";
  var requests=new[]{new QueuedQuestion("first","Which label should appear when both processors are active?",new[]{"Combined label","Generic label"}),new QueuedQuestion("second","Which type of processor should the new integration read?",new[]{"Command line processor","Desktop processor"})};
  var chat=new Conversation(id,new[]{new ConversationMessage("user","",earlier,1),new ConversationMessage("assistant","final","An older answer no longer visible after recap.",2),new ConversationMessage("user","","Next",3),new ConversationMessage("assistant","commentary",answer,4)},false,requests,readOnly:true);
  const string prefix="› Review the previous independent workshop deliverables and preserve…\n  ↳ Recap: Previous work is complete.\n\n› Next\n\n• ";
  string transcript=prefix+answer;
  ConsoleFrame Frame(string body)=>new("fixture",body,180,30,2,28,Array.Empty<int>());
  string collapsed="\n\n• Queued follow-up inputs\n  ? 2 questions\n    shift+← to answer\n\n› Ask Codex to do anything\n? for shortcuts";
  var frame=Frame(transcript+collapsed);var candidate=new LocalDaemonFeed.Candidate("owned-fixture.jsonl",chat);
  var catalog=new LocalDaemonFeed.Catalog("verified-fixture",new[]{candidate},true);
  check(LocalDaemonFeed.Match(frame,catalog.candidates)==chat,"short complete follow-up with its substantial answer identifies original after clipped historical recap");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,id)!=null,"same short follow-up evidence authorizes request guard as well as conversation discovery");
  check(LocalDaemonFeed.Match(Frame(prefix+"Working."+collapsed),catalog.candidates)==null,"short follow-up without substantial answer does not authorize input");
  check(LocalDaemonFeed.Match(Frame(prefix+answer.Replace("workshop","greenhouse")+collapsed),catalog.candidates)==null,"different current answer cannot repair an unproved historical prefix");
  check(LocalDaemonFeed.Match(frame with{text=frame.text.Replace("Review the previous","Review a conflicting")},catalog.candidates)==null,"all older visible anchors must still match the same original in order");
  var duplicate=catalog with{candidates=new[]{candidate,new LocalDaemonFeed.Candidate("other.jsonl",chat with{id="other"})}};
  check(LocalDaemonFeed.Match(frame,duplicate.candidates)==null&&LocalDaemonFeed.DesktopInputEvidence(duplicate,frame,id)==null,"identical short questions and answer bodies across originals remain ambiguous");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,"wrong-thread")==null,"current evidence cannot authorize a different requested thread");
  var unrelated=chat with{id="other",messages=chat.messages.Select(m=>m.sequence==4?m with{text="A different substantial answer about weather stations and measurements unrelated to the workshop drawing integration or processor."}:m).ToArray()};
  check(LocalDaemonFeed.Match(frame,new[]{candidate,new LocalDaemonFeed.Candidate("other.jsonl",unrelated)})==chat,"ordered answer evidence distinguishes otherwise identical short follow-ups across tabs");
  var misplaced=chat with{messages=new[]{chat.messages[0],chat.messages[3],chat.messages[1],chat.messages[2]}};
  check(LocalDaemonFeed.Match(frame,new[]{candidate with{conversation=misplaced}})==null,"an answer before the latest question cannot corroborate the latest turn");
  var pending=requests.ToList();bool opened=false;int selected=0,enters=0,opens=0;string draft="";
  ConsoleFrame Read(){
   if(!opened||pending.Count==0)return Frame(transcript+collapsed);
   var q=pending[0];string options=string.Join("\n",q.options.Append(draft.Length>0?draft:"Type your answer").Select((s,i)=>(i==selected?"› ":"  ")+(i+1)+". "+s));
   return Frame(transcript+"\n\n• Queued follow-up inputs\n\n1 of "+pending.Count+"\n"+q.title+"\n\n"+options+"\n\nenter submit   ctrl + ] skip   shift + → main prompt   shift + ← next question");
  }
  Conversation Current(){
   var current=chat with{requests=pending.ToArray()};var live=catalog with{candidates=new[]{candidate with{conversation=current}}};
   if(LocalDaemonFeed.DesktopInputEvidence(live,Read(),id)==null)throw new IOException("Synthetic original verification failed");return current;
  }
  JsonElement Command(QueuedQuestion q,int choice,string text)=>JsonSerializer.SerializeToElement(new{request="fixture",kind="request",thread=id,requestKey=q.id,question=q.title,choices=q.options.Append("직접 입력"),choice,text});
  void Key(string key,bool alt){if(key=="SHIFT_LEFT"){opened=true;opens++;}else if(key=="DOWN")selected++;else if(key=="UP")selected--;else if(key=="ENTER"){enters++;pending.RemoveAt(0);opened=false;selected=0;draft="";}}
  var first=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,Current,t=>draft=t,Key,Command(requests[0],1,"")));
  check(first.Bool("ok")&&enters==1&&opens==1&&pending.Count==1&&pending[0].id==requests[1].id,"first of two queued questions submits once after current original guard and preserves second request");
  var second=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,Current,t=>draft=t,Key,Command(requests[1],-1,"Use the command line processor")));
  check(second.Bool("ok")&&enters==2&&opens==2&&pending.Count==0,"second queued question independently verifies its own identity and accepts typed reply once");
  var stale=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,Current,t=>draft=t,Key,Command(requests[0],0,"")));
  check(!stale.Bool("ok")&&enters==2&&opens==2,"answered question cannot reopen or submit another queued request");
 }
}
