using System.Text;

namespace TerminalLink;

// Comparison-only text. Never rewrite the original message or its identity.
internal static class EvidenceInlineLinks {
 internal static string Render(string text){
  var result=new StringBuilder(text.Length);
  for(int i=0;i<text.Length;){
   if(text[i]=='\\'&&i+1<text.Length){result.Append(text,i,2);i+=2;continue;}
   if(text[i]=='`'){
    int count=Run(text,i),end=i+count;
    while(end<text.Length){
     end=text.IndexOf('`',end);if(end<0){end=text.Length;break;}
     int next=Run(text,end);end+=next;if(next==count)break;
    }
    result.Append(text,i,end-i);i=end;continue;
   }
   if(text[i]=='['&&(i==0||text[i-1]!='!')&&LinkEnd(text,i,out int labelEnd,out int endAt)){
    result.Append(text,i+1,labelEnd-i-1);i=endAt+1;continue;
   }
   result.Append(text[i++]);
  }
  return result.ToString();
 }
 static int Run(string text,int at){int end=at;while(end<text.Length&&text[end]=='`')end++;return end-at;}
 static bool LinkEnd(string text,int start,out int labelEnd,out int end){
  labelEnd=end=-1;int depth=1;
  for(int at=start+1;at<text.Length;at++){
   if(text[at]=='\\'){at++;continue;}
   if(text[at]=='[')depth++;
   if(text[at]!=']'||--depth!=0)continue;
   labelEnd=at;break;
  }
  if(labelEnd<=start+1||labelEnd+1>=text.Length||text[labelEnd+1]!='(')return false;
  // Match only complete inline destinations, including balanced URL parentheses.
  // Unknown/malformed syntax stays literal and cannot add corroborating evidence.
  depth=1;
  for(int at=labelEnd+2;at<text.Length;at++){
   char c=text[at];if(c=='\\'){at++;continue;}
   if(char.IsWhiteSpace(c)||c=='<'||c=='>')return false;
   if(c=='(')depth++;
   if(c==')'&&--depth==0){end=at;return at>labelEnd+2;}
  }
  return false;
 }
}
