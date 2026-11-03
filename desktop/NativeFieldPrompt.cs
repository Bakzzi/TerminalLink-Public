using System.Text.RegularExpressions;
namespace TerminalLink;

// Native tool permission dialogs are not asynchronous user questions in the rollout.
// Parse the complete visible field, including origin and permission-scope descriptions.
internal static class NativeFieldPrompt {
 static readonly Regex Footer=new(@"(?m)^[ \t]*enter to submit[ \t]*\|[ \t]*esc to cancel[ \t]*\s*\z");
 static readonly Regex Header=new(@"(?m)^[ \t]*Field[ \t]+(\d+)/(\d+)(?:[ \t]+\((\d+) required unanswered\))?[ \t]*$");
 static readonly Regex Option=new(@"^[ \t]*([›>])?[ \t]*(\d+)\.[ \t]+(.+)$");
 internal static NotificationInput.Prompt? Parse(string text)=>Read(text).prompt;
 internal static string Transcript(string text){string clean=text.Replace("\r","");var read=Read(clean);return read.prompt==null?text:clean[..read.start];}
 static (NotificationInput.Prompt? prompt,int start) Read(string text){
  text=text.Replace("\r","");var footer=Footer.Match(text);if(!footer.Success)return(null,0);
  var headers=Header.Matches(text[..footer.Index]);if(headers.Count==0)return(null,0);var header=headers[^1];
  if(!int.TryParse(header.Groups[1].Value,out int field)||!int.TryParse(header.Groups[2].Value,out int count)||field<1||field>count||count>32)return(null,0);
  // Codex updates this counter while navigating; it is not part of request identity.
  if(header.Groups[3].Success&&(!int.TryParse(header.Groups[3].Value,out int unanswered)||unanswered>count))return(null,0);
  string body=text[(header.Index+header.Length)..footer.Index];var title=new List<string>();var choices=new List<string>();int selected=-1;
  foreach(string line in body.Split('\n')){
   var option=Option.Match(line);
   if(option.Success){
    if(!int.TryParse(option.Groups[2].Value,out int number)||number!=choices.Count+1||number>12)return(null,0);
    choices.Add(Regex.Replace(option.Groups[3].Value.Trim(),@"[ \t]+"," "));
    if(option.Groups[1].Success){if(selected>=0)return(null,0);selected=number-1;}
   }else if(choices.Count==0)title.Add(line.Trim());
   else if(!string.IsNullOrWhiteSpace(line)){
    if(!char.IsWhiteSpace(line[0])||Regex.IsMatch(line.TrimStart(),@"^[›>•]"))return(null,0);
    choices[^1]+=" "+line.Trim();
   }
  }
  string question=string.Join("\n",title).Trim();
  if(question.Length==0||choices.Count<2||selected<0)return(null,0);
  return(new($"Field {field}/{count}\n\n"+question,choices.ToArray(),selected,false,false,false,""),header.Index);
 }
}
