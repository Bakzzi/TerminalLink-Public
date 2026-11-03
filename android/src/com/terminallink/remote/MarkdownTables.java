package com.terminallink.remote;
import java.util.*;

// Shared table detection for record text and terminal-rendered text. Ambiguous content stays intact.
final class MarkdownTables {
 static final class Block {
  final int start,end;final List<List<String>> rows;final int[] align;
  Block(int s,int e,List<List<String>> r,int[] a){start=s;end=e;rows=r;align=a;}
 }
 static List<String> cells(String line){
  ArrayList<String> cells=new ArrayList<>();StringBuilder cell=new StringBuilder();int code=0;boolean separator=false;
  String s=line.trim();
  for(int i=0;i<s.length();){
   char c=s.charAt(i);
   if(c=='\\'&&i+1<s.length()&&(s.charAt(i+1)=='|'||s.charAt(i+1)=='\\')){cell.append(s.charAt(i+1));i+=2;continue;}
   if(c=='\u0060'){int end=i+1;while(end<s.length()&&s.charAt(end)==c)end++;int n=end-i;if(code==0)code=n;else if(code==n)code=0;cell.append(s,i,end);i=end;continue;}
   if(c=='|'&&code==0){cells.add(cell.toString().trim());cell.setLength(0);separator=true;}else cell.append(c);
   i++;
  }
  cells.add(cell.toString().trim());
  if(!separator)return Collections.emptyList();
  if(s.startsWith("|"))cells.remove(0);
  // A final escaped pipe belongs to the cell.
  int slashes=0;for(int i=s.length()-2;i>=0&&s.charAt(i)=='\\';i--)slashes++;
  if(s.endsWith("|")&&slashes%2==0&&code==0&&!cells.isEmpty())cells.remove(cells.size()-1);
  return cells;
 }
 static List<Block> parse(String text){return parse(text,0);}
 static List<Block> parse(String text,int terminalColumns){
  String[] lines=text.split("\n",-1);int[] offsets=new int[lines.length+1];
  for(int i=0;i<lines.length;i++)offsets[i+1]=offsets[i]+lines[i].length()+1;
  ArrayList<Block> found=new ArrayList<>();char fence=0;int fenceSize=0;
  for(int i=0;i+1<lines.length;i++){
   String trim=lines[i].trim();
   if(!trim.isEmpty()&&(trim.charAt(0)=='\u0060'||trim.charAt(0)=='~')){
    char mark=trim.charAt(0);int n=0;while(n<trim.length()&&trim.charAt(n)==mark)n++;
    if(n>=3){if(fence==0){fence=mark;fenceSize=n;}else if(fence==mark&&n>=fenceSize&&trim.substring(n).trim().isEmpty())fence=0;continue;}
   }
   if(fence!=0)continue;
   boolean indented=lines[i].startsWith("    ")||lines[i].startsWith("\t");
   if(indented&&terminalColumns<=0)continue;
   PrintedTables.Match printed=indented?PrintedTables.aligned(lines,offsets,i,text.length()):PrintedTables.read(lines,offsets,i,text.length(),terminalColumns);
   if(printed!=null){found.add(printed.block);i=printed.next-1;continue;}
   if(indented)continue;
   List<String> header=cells(lines[i]),rule=cells(lines[i+1]);
   if(header.isEmpty()||header.size()>32||header.size()!=rule.size())continue;
   int[] align=new int[header.size()];boolean valid=true;
   for(int c=0;c<rule.size();c++){String r=rule.get(c);if(!r.matches(":?-{3,}:?")){valid=false;break;}align[c]=r.startsWith(":")&&r.endsWith(":")?1:r.endsWith(":")?2:0;}
   if(!valid)continue;
   ArrayList<List<String>> rows=new ArrayList<>();rows.add(header);int end=i+2;
   while(end<lines.length&&!lines[end].trim().isEmpty()){
    PrintedTables.Row physical=PrintedTables.row(lines,end,header.size(),terminalColumns);List<String> row=physical.cells;if(row.isEmpty())break;
    if(row.size()>header.size())valid=false;
    while(row.size()<header.size())row.add("");
    rows.add(row);end=physical.next;
   }
   // Bound native view creation; retain the complete original text if unusually large.
   if(valid&&rows.size()*header.size()<=512)
    found.add(new Block(offsets[i],Math.min(text.length(),offsets[end]),rows,align));
   i=end-1;
  }
  return found;
 }
}