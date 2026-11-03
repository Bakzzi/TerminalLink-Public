namespace TerminalLink;

internal static class InputEvidenceChecks {
 internal static void Run(Action<bool,string> check){
  CompletedAnswerChecks.Run(check);
  ShortFollowupChecks.Run(check);
  TurnAnswerChecks.Run(check);
  const string thread="00000000-0000-4000-8000-000000000123",other="00000000-0000-4000-8000-000000000456";
  const string answer="The orange workshop finished its independent drawing and checked all of its original measurements before delivering the finished design.";
  const string different="The green greenhouse collected its independent weather samples and checked all of its original readings before delivering the finished report.";
  Conversation Chat(string id,string body)=>new(id,new[]{new ConversationMessage("user","","Yes",1),new ConversationMessage("assistant","final",body,2)},false,readOnly:true);
  ConsoleFrame Frame(string body)=>new("fixture","› Yes\n• "+body+"\n\nWorked for 1m\n\n› Ask Codex to do anything\n? for shortcuts",160,10,2,6,Array.Empty<int>());
  var first=Chat(thread,answer);var second=Chat(other,different);var frame=Frame(answer);
  var catalog=new LocalDaemonFeed.Catalog("verified-fixture",new[]{new LocalDaemonFeed.Candidate("first.jsonl",first),new LocalDaemonFeed.Candidate("second.jsonl",second)},true);
  check(catalog.candidates.Count(c=>LocalDaemonFeed.Compatible(frame,c.conversation))==2&&LocalDaemonFeed.Match(frame,catalog.candidates)?.id==thread,"reproduces readable history but ambiguous question-only send after a short acknowledgement");
  var evidence=LocalDaemonFeed.DesktopInputEvidence(catalog,frame,thread);
  check(evidence?.candidates.Length==1&&evidence.candidates[0].conversation==first&&catalog.candidates.Length==2,"ordered original answer evidence identifies the correct send target without mutating the catalog");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,other)==null,"matching another live conversation cannot authorize the requested target");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,Frame(different),other)?.candidates[0].conversation==second,"the same rule applies to the other tab rather than a named or pinned conversation");
  var duplicate=catalog with{candidates=new[]{catalog.candidates[0],new LocalDaemonFeed.Candidate("duplicate.jsonl",first with{id=other})}};
  check(LocalDaemonFeed.DesktopInputEvidence(duplicate,frame,thread)==null,"identical question and answer evidence across threads stays ambiguous");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,Frame("Still working"),thread)==null,"a short question alone cannot authorize input");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame with{text="› Ask Codex to do anything\n? for shortcuts"},thread)==null,"an empty composer without transcript evidence cannot authorize input");
  var wrong=catalog with{candidates=new[]{catalog.candidates[1]}};
  check(LocalDaemonFeed.DesktopInputEvidence(wrong,frame,thread)==null,"a missing target cannot inherit another conversation's input authority");
  RenderedLinkChecks(check,thread,other);
 }
 static void RenderedLinkChecks(Action<bool,string> check,string thread,string other){
  const string question="Review the complete deployment procedure";
  const string link="[v2.3](https://example.invalid/releases/v2.3)";
  const string final="Updated the release.\n\n- Keep drafts\n- Verify signatures\n\nRelease "+link+" has passed **all checks** and is ready.\n\nThe saved preferences and drafts are preserved.";
  const string displayed="Updated the release.\n\n  • Keep drafts\n  • Verify signatures\n\n  Release v2.3 has passed all checks and is ready.\n\n  The saved preferences and drafts are preserved.";
  var original=new Conversation(thread,new[]{new ConversationMessage("user","",question+"\nInclude build steps and retained releases.",1),new ConversationMessage("assistant","final_answer",final,2)},false,readOnly:true);
  var frame=new ConsoleFrame("fixture","› "+question+"\n  + 1 line (ctrl+t to expand)\n\n• "+displayed+"\n\nWorked for 2m\n\n› Ask Codex to do anything\n? for shortcuts",160,20,2,18,Array.Empty<int>());
  var catalog=new LocalDaemonFeed.Catalog("verified-fixture",new[]{new LocalDaemonFeed.Candidate("folded.jsonl",original)},true);
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame,thread)?.candidates[0].conversation==original,"folded question and list answer with a rendered Markdown link corroborate the same original");
  check(original.messages[1].text==final,"link evidence leaves the original Markdown and message identity unchanged");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame with{text=frame.text.Replace("v2.3","v9.9")},thread)==null,"a different visible link label cannot authorize a folded question");
  check(LocalDaemonFeed.DesktopInputEvidence(catalog,frame with{text=frame.text.Replace("Release v2.3 has passed all checks and is ready.","Still working")},thread)==null,"short bullets and one small sentence do not lower required evidence strength");
  var duplicate=original with{id=other,messages=original.messages.Select(m=>m.role=="assistant"?m with{text=m.text.Replace("example.invalid","other.invalid")}:m).ToArray()};
  var ambiguous=catalog with{candidates=new[]{catalog.candidates[0],new LocalDaemonFeed.Candidate("duplicate.jsonl",duplicate)}};
  check(LocalDaemonFeed.DesktopInputEvidence(ambiguous,frame,thread)==null,"different hidden link destinations with identical displayed evidence remain ambiguous");
  var wrongAnswer=duplicate with{messages=duplicate.messages.Select(m=>m.role=="assistant"?m with{text=m.text.Replace("v2.3","v9.9")}:m).ToArray()};
  var distinct=catalog with{candidates=new[]{catalog.candidates[0],new LocalDaemonFeed.Candidate("other.jsonl",wrongAnswer)}};
  check(LocalDaemonFeed.DesktopInputEvidence(distinct,frame,thread)?.candidates[0].conversation==original&&LocalDaemonFeed.DesktopInputEvidence(distinct,frame,other)==null,"visible link differences distinguish original records across tabs");
  check(DesktopEvidenceText.RenderInlineLinks("[release](https://example.invalid/a_(b)) and [notes](notes.md)")=="release and notes","balanced URL parentheses and relative links retain only their visible labels");
  check(DesktopEvidenceText.RenderInlineLinks("`"+link+"` and "+link)=="`"+link+"` and v2.3","inline code stays literal while adjacent rendered links are normalized");
  string fenced="```markdown\n"+link+"\n```";
  check(DesktopEvidenceText.RenderInlineLinks(fenced)==fenced,"fenced code examples do not become rendered-link evidence");
  foreach(string literal in new[]{"\\"+link,"!"+link,"[broken](missing","[broken](invalid destination)","`unclosed "+link})
   check(DesktopEvidenceText.RenderInlineLinks(literal)==literal,"escaped, image, malformed and incomplete code syntax stays literal");
 }
}
