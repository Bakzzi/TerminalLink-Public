using System.Text.RegularExpressions;

namespace TerminalLink;

// One turn can contain several short progress messages separated by long tool
// output. Evidence belongs to that turn, not to one arbitrarily long bubble.
internal static class TurnAnswerEvidence {
 internal static bool Matches(ConsoleFrame transcript,Conversation original,int userIndex){
  var shown=ScreenConversation.Extract(transcript);
  int question=Array.FindLastIndex(shown,m=>m.role=="user");
  if(question<0||userIndex<0||userIndex>=original.messages.Length||original.messages[userIndex].role!="user")return false;
  var answers=original.messages.Skip(userIndex+1).TakeWhile(m=>m.role!="user")
   .Where(m=>m.role=="assistant"&&m.phase is "commentary" or "final" or "final_answer")
   .Select(m=>Normal(DesktopEvidenceText.RenderInlineLinks(m.text))).ToArray();
  int source=0,offset=0,hits=0,total=0;
  var seen=new HashSet<string>(StringComparer.Ordinal);
  foreach(var block in shown.Skip(question+1).Where(m=>m.role=="assistant")){
   foreach(string part in block.text.Split('\n').Select(Normal).Where(p=>p.Length>=24)){
    if(!seen.Add(part))continue;
    // Match in source order and within an actual assistant message. Tool text,
    // the input editor, previous turns and concatenated fragments are not proof.
    int next=source,at=-1;
    for(;next<answers.Length;next++){
     at=answers[next].IndexOf(part,next==source?offset:0,StringComparison.Ordinal);
     if(at>=0)break;
    }
    if(at<0)continue;
    source=next;offset=at+part.Length;hits++;total+=part.Length;
   }
  }
  return hits>=2&&total>=64||hits==1&&total>=80;
 }
 static string Normal(string text)=>Regex.Replace(Regex.Replace(text,@"[*`_]",""),@"\s+","");
}
