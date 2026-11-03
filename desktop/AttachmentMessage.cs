using System.Text.RegularExpressions;

namespace TerminalLink;

// Display/echo projection only. Never use this text for original-record identity,
// input validation, or storage, and never modify Conversation.messages.
internal sealed record AttachmentMessage(string Caption,int ImageCount,string[] ImagePaths){
 static readonly Regex Envelope=new("\\G<image name=\\[Image #(?<index>[1-9][0-9]*)\\] path=\"(?<path>[^\"\\r\\n]+)\">[ \\t\\r\\n]*</image>[ \\t\\r\\n]*",RegexOptions.Compiled|RegexOptions.CultureInvariant);
 static readonly Regex Marker=new(@"\G\[Image #(?<index>[1-9][0-9]*)\]",RegexOptions.Compiled|RegexOptions.CultureInvariant);
 internal static bool IsImage(string file)=>Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";
 static bool AbsolutePath(string path)=>path.StartsWith('/')||path.StartsWith(@"\\",StringComparison.Ordinal)||path.Length>2&&char.IsAsciiLetter(path[0])&&path[1]==':'&&path[2] is '\\' or '/';
 internal static AttachmentMessage Read(string original,int expectedImages=0){
  AttachmentMessage Plain()=>new(original,0,Array.Empty<string>());
  int at=0;var paths=new List<string>();
  while(true){
   var block=Envelope.Match(original,at);if(!block.Success)break;
   if(!int.TryParse(block.Groups["index"].Value,out int index)||index!=paths.Count+1||!AbsolutePath(block.Groups["path"].Value))return Plain();
   paths.Add(block.Groups["path"].Value);at+=block.Length;
  }
  if(paths.Count>0&&expectedImages>0&&paths.Count!=expectedImages)return Plain();
  int count=0,limit=paths.Count>0?paths.Count:expectedImages>0?expectedImages:int.MaxValue;
  while(true){
   var marker=Marker.Match(original,at);if(!marker.Success)break;
   if(!int.TryParse(marker.Groups["index"].Value,out int index)||index!=count+1)return Plain();
   count++;at+=marker.Length;
   int next=at;while(next<original.Length&&char.IsWhiteSpace(original[next]))next++;
   var following=Marker.Match(original,next);
   if(count<limit&&following.Success&&int.TryParse(following.Groups["index"].Value,out int nextIndex)&&nextIndex==count+1){at=next;continue;}
   // Remove the transport separator, not whitespace within the submitted caption.
   if(at<original.Length&&original[at]==' ')at++;
   else if(original.AsSpan(at).StartsWith("\r\n"))at+=2;
   else if(at<original.Length&&original[at]=='\n')at++;
   break;
  }
  if(count==0||paths.Count>0&&paths.Count!=count)return Plain();
  return new(original[at..],count,paths.ToArray());
 }
 internal string Display=>ImageCount==0?Caption:Compose(Caption,Enumerable.Range(1,ImageCount).Select(i=>"이미지 "+i));
 internal static string Compose(string caption,IEnumerable<string> names)=>caption+(caption.Length>0?"\n\n":"")+string.Join("\n",names.Select(n=>"📎 "+n));
 internal static bool SamePath(string a,string b){
  // Remote Linux paths are case-sensitive; only Windows drive/UNC paths fold case.
  bool windows=a.Length>1&&a[1]==':'||a.StartsWith(@"\\",StringComparison.Ordinal);
  return windows?string.Equals(a.Replace('\\','/'),b.Replace('\\','/'),StringComparison.OrdinalIgnoreCase):a==b;
 }
}
