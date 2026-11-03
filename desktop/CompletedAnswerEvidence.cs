using System.Text.RegularExpressions;

namespace TerminalLink;

// Identity evidence only. The viewport is never returned as conversation history.
// A long turn can scroll every question out of CONOUT$ while its final answer remains.
internal static class CompletedAnswerEvidence {
 internal static Conversation? Match(ConsoleFrame frame,IEnumerable<Conversation> verified){
  if(!ScreenConversation.Recognizes(frame))return null;
  try{NotificationInput.EmptyMain(frame);}catch(IOException){return null;}
  string[] lines=frame.text.Replace("\r","").Split('\n');
  int composer=frame.cursorY;
  if(composer<=0||composer>=lines.Length||!lines[composer].StartsWith("› "))return null;
  int completed=Array.FindLastIndex(lines,composer-1,composer,l=>Regex.IsMatch(l.Trim(),@"^(?:[─━]\s*)?Worked for\s+\d",RegexOptions.IgnoreCase));
  if(completed<0||lines.Skip(completed+1).Take(composer-completed-1).Any(l=>!string.IsNullOrWhiteSpace(l)))return null;
  int start=completed-1;
  while(start>=0&&!Regex.IsMatch(lines[start],@"^[•›◦✦✧](?:\s|$)"))start--;
  if(start<0||!lines[start].StartsWith("• "))return null;
  var block=frame with{text=string.Join("\n",lines.Skip(start).Take(completed-start)),cursorY=-1};
  var extracted=ScreenConversation.Extract(block);
  // An explicit assistant block immediately before completion; never a tool result,
  // clipped unlabelled text, an earlier commentary, or the text of an open request.
  if(extracted.Length!=1||extracted[0].role!="assistant")return null;
  string[] proof=extracted[0].text.Split('\n').Select(Normal).Where(s=>s.Length>=24).Distinct().ToArray();
  int length=proof.Sum(s=>s.Length);
  if(length<120||proof.Length==1&&length<160)return null;
  var matches=new List<Conversation>();
  foreach(var candidate in verified){
   if(!ConversationReader.IsOriginal(candidate)||candidate.requests?.Length>0)continue;
   var last=candidate.messages.LastOrDefault();
   if(last?.role!="assistant"||last.phase is not ("final" or "final_answer"))continue;
   string original=Normal(EvidenceInlineLinks.Render(last.text));int at=0;bool all=true;
   foreach(string part in proof){int found=original.IndexOf(part,at,StringComparison.Ordinal);if(found<0){all=false;break;}at=found+part.Length;}
   if(all)matches.Add(candidate);
  }
  // Even duplicate terminal renderings with different hidden Markdown URLs are ambiguous.
  return matches.Count==1?matches[0]:null;
 }
 static string Normal(string text)=>Regex.Replace(Regex.Replace(Regex.Replace(text,@"(?m)^\s*(?:[-*•]|\d+[.)])\s+",""),@"[*`_]",""),@"\s+","");
}
