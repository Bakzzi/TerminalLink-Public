namespace TerminalLink;

internal static class StartInputChecks {
 internal static ConsoleFrame Welcome()=>new("fixture","  >_ OpenAI Codex (v0.162.1)\n     ~\\fresh-workspace\n\n  To get started, describe a task or try one of these commands:\n\n  /init - create an AGENTS.md file with instructions for Codex\n  /review - review any changes and find issues\n\n› Ask Codex to do anything\n\n  GPT-6.1-Sol xhigh · ~\\fresh-workspace\n  ← for agents · ? for shortcuts",100,12,2,8,Array.Empty<int>(),"fixture-console");
 internal static void Run(Action<bool,string> check){
  var initial=Welcome();
  check(DesktopStartInput.Welcome(initial with{text=initial.text.Replace("› ","» ")}),"current startup composer is recognized with U+00BB");
  check(DesktopStartInput.Welcome(initial)&&DesktopStartInput.Welcome(initial with{text=initial.text.Replace("fresh-workspace","another-project")}),"fresh Codex startup is recognized independently of folder or tab name");
  foreach(var invalid in new[]{initial with{text=initial.text.Replace("Ask Codex to do anything","Existing draft"),cursorX=16},initial with{text=initial.text.Replace("/review - review any changes and find issues","• Existing answer")},initial with{text=initial.text.Replace("/review - review any changes and find issues","› Older question")},initial with{text="› Ask Codex to do anything\n? for shortcuts",cursorY=0},initial with{text=initial.text.Replace("› Ask Codex to do anything","PS C:\\workspace> ")}})
   check(!DesktopStartInput.Welcome(invalid),"drafts, existing conversations, clipped viewports and shells cannot use the fresh-start path");
  string identity="native-fixture",draft="",sent="";int pasted=0,entered=0;
  ConsoleFrame Read()=>draft.Length==0?initial:initial with{text=initial.text.Replace("Ask Codex to do anything",draft),cursorX=draft.Length+2};
  void Paste(string value){pasted++;sent=draft=value;}
  DesktopStartInput.Submit(Read,_=>identity,"native-fixture","First task",()=>{},Paste,()=>entered++);
  check(sent=="First task"&&pasted==1&&entered==1,"first message verifies the same native TUI and pasted draft before exactly one Enter");
  void Reject(Action action,string name){bool rejected=false;try{action();}catch(IOException){rejected=true;}check(rejected&&entered==0,name);}
  draft="";pasted=entered=0;
  Reject(()=>DesktopStartInput.Submit(Read,_=>identity,"native-fixture","First task",()=>identity="replaced",Paste,()=>entered++),"a replaced TUI during ownership discovery cannot receive the first message");
  check(pasted==0,"identity change before pasting writes nothing");
  identity="native-fixture";draft="";pasted=entered=0;
  Reject(()=>DesktopStartInput.Submit(Read,_=>identity,"native-fixture","First task",()=>{},s=>{Paste(s);identity="replaced";},()=>entered++),"a TUI change after pasting never receives Enter");
  identity="native-fixture";draft="PC draft";pasted=entered=0;
  Reject(()=>DesktopStartInput.Submit(Read,_=>identity,"native-fixture","First task",()=>{},Paste,()=>entered++),"an existing terminal draft is preserved without input");
  check(pasted==0&&draft=="PC draft","refused startup preserves the user's existing draft");
  identity="native-fixture";draft="";pasted=entered=0;
  Reject(()=>DesktopStartInput.Submit(Read,_=>identity,"native-fixture","First task",()=>throw new IOException("catalog unavailable"),Paste,()=>entered++),"unverified original-record ownership blocks startup before input");
  check(pasted==0,"failed owner discovery sends no text");
  Reject(()=>DesktopStartInput.Submit(Read,_=>identity,"native-fixture","/status",()=>{},Paste,()=>entered++),"a startup slash command cannot masquerade as a submitted chat message");
  check(pasted==0,"startup command guidance leaves the native terminal untouched");
  const string thread="00000000-0000-4000-8000-000000000123",other="00000000-0000-4000-8000-000000000456";
  Conversation Chat(string id,string text)=>new(id,new[]{new ConversationMessage("user","",text,1)},false,readOnly:true);
  var old=new LocalDaemonFeed.Candidate("old.jsonl",Chat(other,"Hi"));
  var before=new LocalDaemonFeed.Catalog("owned-daemon",new[]{old},true);
  var fresh=new LocalDaemonFeed.Candidate("new.jsonl",Chat(thread,"Hi"));
  var after=before with{candidates=new[]{old,fresh}};
  var transcript=initial with{text="› Hi\n\n› Ask Codex to do anything\n? for shortcuts",cursorY=2};
  check(LocalDaemonFeed.DesktopMatchStart(before,after,transcript,"Hi")==fresh,"first-message binding excludes old conversations even when their text is identical");
  check(LocalDaemonFeed.DesktopMatchStart(before,after with{daemon="changed"},transcript,"Hi")==null,"daemon replacement cannot authorize the new original record");
  check(LocalDaemonFeed.DesktopMatchStart(before,after,transcript,"Different task")==null,"new records must contain exactly the submitted first message");
  check(LocalDaemonFeed.DesktopMatchStart(before,after,initial,"Hi")==null,"the welcome screen alone cannot identify a conversation record");
  var empty=before with{candidates=new[]{fresh with{conversation=fresh.conversation with{messages=Array.Empty<ConversationMessage>()}}}};
  check(LocalDaemonFeed.DesktopMatchStart(empty,empty with{candidates=new[]{fresh}},transcript,"Hi")==fresh,"a verified empty record can acquire its first submitted message");
  var duplicate=fresh with{path="duplicate.jsonl",conversation=fresh.conversation with{id=other}};
  var none=before with{candidates=Array.Empty<LocalDaemonFeed.Candidate>()};
  check(LocalDaemonFeed.DesktopMatchStart(none,none with{candidates=new[]{fresh,duplicate}},transcript,"Hi")==null,"simultaneous identical first messages remain ambiguous");
  var one=none with{candidates=new[]{fresh}};
  check(LocalDaemonFeed.DesktopInputEvidence(one,transcript,thread)?.candidates[0]==fresh,"a short complete unique question remains sendable after its original is bound");
  check(LocalDaemonFeed.DesktopInputEvidence(after,transcript,thread)==null,"a short question shared with another original still cannot authorize input");
  var target=new ConsoleTarget(new(0,0,"fixture","WindowsTerminal"),1,1,"fixture");var tab=new DesktopTab(target);
  tab.Starting=true;tab.Accept(initial,null);check(tab.Error==""&&!tab.Ready,"first-message initialization does not flash a history-read failure");tab.Starting=false;
  tab.ConfirmStart("Hi",thread);check(tab.Messages.Count==1&&tab.Messages[0].Text=="Hi"&&!tab.Ready&&tab.Original==null,"a sent first message is visible without claiming a fabricated original");
  tab.Accept(transcript,fresh.conversation);check(tab.Ready&&tab.Messages.Count==1&&tab.Messages[0].Source.phase!="sent","the verified first message replaces its display receipt without duplication");
 }
}
