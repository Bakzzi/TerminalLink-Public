package com.terminallink.remote;
import java.util.*;

// Recognizes the grid that terminal renderers print, without guessing columns from prose.
final class PrintedTables {
 static final class Match {
  final MarkdownTables.Block block;final int next;
  Match(MarkdownTables.Block b,int n){block=b;next=n;}
 }
 static boolean divider(String value){
  String s=value.trim();int horizontal=0;
  for(int i=0;i<s.length();i++){
   char c=s.charAt(i);
   if("-─━═=".indexOf(c)>=0){horizontal++;continue;}
   if("+| :┬┼┴├┤┌┐└┘╭╮╰╯╔╗╚╝╠╣╦╩╬".indexOf(c)<0)return false;
  }
  return horizontal>=3;
 }
 static boolean bottom(String s){return s.indexOf('└')>=0||s.indexOf('╰')>=0||s.indexOf('╚')>=0;}
 static List<String> cells(String value){
  String s=value.trim();if(s.matches(".*[│┃║].*")){ArrayList<String> cells=new ArrayList<>(Arrays.asList(s.split("[│┃║]",-1)));if(!cells.isEmpty()&&cells.get(0).trim().isEmpty())cells.remove(0);if(!cells.isEmpty()&&cells.get(cells.size()-1).trim().isEmpty())cells.remove(cells.size()-1);for(int i=0;i<cells.size();i++)cells.set(i,cells.get(i).trim());return cells;}return MarkdownTables.cells(s);
 }
 static boolean printed(String s){return s.indexOf('│')>=0||s.indexOf('┃')>=0||s.indexOf('║')>=0||divider(s);}
 static List<String> combine(List<List<String>> lines){
  ArrayList<String> row=new ArrayList<>();int count=lines.get(0).size();
  for(int c=0;c<count;c++){
   StringBuilder value=new StringBuilder();
   for(List<String> line:lines){String piece=line.get(c);if(piece.isEmpty())continue;if(value.length()>0)value.append('\n');value.append(piece);}
   row.add(value.toString());
  }return row;
 }

 static final class Row {
  final List<String> cells;final int next;
  Row(List<String> c,int n){cells=c;next=n;}
 }
 static int width(String s){
  int width=0;for(int i=0;i<s.length();){int cp=s.codePointAt(i);i+=Character.charCount(cp);
   int kind=Character.getType(cp);if(kind==Character.NON_SPACING_MARK||kind==Character.ENCLOSING_MARK||cp<32)continue;
   width+=cp>=0x1100&&(cp<=0x115f||cp==0x2329||cp==0x232a||cp>=0x2e80&&cp<=0xa4cf||cp>=0xac00&&cp<=0xd7a3||cp>=0xf900&&cp<=0xfaff||cp>=0xfe10&&cp<=0xfe6f||cp>=0xff00&&cp<=0xff60||cp>=0xffe0&&cp<=0xffe6||cp>=0x1f300)?2:1;
  }return width;
 }
 static Row row(String[] lines,int start,int count,int columns){
  StringBuilder logical=new StringBuilder(lines[start]);List<String> parsed=cells(logical.toString());int end=start+1;
  while(!parsed.isEmpty()&&parsed.size()<=count&&columns>0&&end<lines.length&&end-start<32
    &&width(lines[end-1])>=Math.max(20,columns-4)){
   String next=lines[end].trim(),previous=lines[end-1].trim();
   if(next.isEmpty()||divider(next)||next.matches("^(?:[-*•>]\\s|\\d+[.)]\\s|#{1,6}\\s|[\u0060~]{3}).*"))break;
   if(parsed.size()==count&&(previous.endsWith("|")||previous.endsWith("│")||previous.endsWith("║")||previous.endsWith("┃")))break;
   List<String> nextCells=cells(next);if(nextCells.size()>=count)break;
   logical.append('\n').append(lines[end++]);parsed=cells(logical.toString());
  }
  return new Row(parsed,end);
 }
 static final class Guide {
  final int[] starts,ends;
  Guide(int[] s,int[] e){starts=s;ends=e;}
  boolean same(Guide other){return other!=null&&Arrays.equals(starts,other.starts)&&Arrays.equals(ends,other.ends);}
 }
 static Guide guide(String line){
  if(line.endsWith("\r"))line=line.substring(0,line.length()-1);
  if(!line.matches(" *[-─━═]{3,}(?: {2,}[-─━═]{3,})+ *"))return null;
  java.util.regex.Matcher m=java.util.regex.Pattern.compile("[-─━═]{3,}").matcher(line);
  ArrayList<Integer> starts=new ArrayList<>(),ends=new ArrayList<>();
  while(m.find()){starts.add(m.start());ends.add(m.end());}
  if(starts.size()<2||starts.size()>32)return null;
  int[] a=new int[starts.size()],b=new int[a.length];for(int i=0;i<a.length;i++){a[i]=starts.get(i);b[i]=ends.get(i);}return new Guide(a,b);
 }
 static List<String> alignedCells(String line,Guide guide){
  StringBuilder[] values=new StringBuilder[guide.starts.length];for(int i=0;i<values.length;i++)values[i]=new StringBuilder();
  int column=0,position=0;
  for(int i=0;i<line.length();){int cp=line.codePointAt(i);String character=new String(Character.toChars(cp));i+=Character.charCount(cp);if(cp=='\t')return null;int size=width(character);
   while(column<values.length&&position>=guide.ends[column])column++;
   if(size==0){if(column<values.length)values[column].append(character);continue;}
   if(column<values.length&&position>=guide.starts[column]&&position+size<=guide.ends[column])values[column].append(character);
   else if(!Character.isWhitespace(cp))return null;
   position+=size;
  }
  ArrayList<String> result=new ArrayList<>();for(StringBuilder value:values)result.add(value.toString().trim());return result;
 }
 static Match aligned(String[] lines,int[] offsets,int start,int textLength){
  int rule=start+1;Guide layout=null;
  while(rule<lines.length&&rule-start<=8&&!lines[rule-1].trim().isEmpty()){
   layout=guide(lines[rule]);if(layout!=null)break;
   if(divider(lines[rule]))return null;rule++;
  }
  if(layout==null||rule>=lines.length)return null;
  ArrayList<List<String>> headings=new ArrayList<>();
  for(int i=start;i<rule;i++){List<String> row=alignedCells(lines[i],layout);if(row==null||lines[i].trim().isEmpty())return null;headings.add(row);}
  List<String> header=combine(headings);for(String cell:header)if(cell.isEmpty())return null;
  ArrayList<List<List<String>>> groups=new ArrayList<>();ArrayList<List<String>> group=new ArrayList<>();boolean ruled=false;int end=rule+1,count=0;
  while(end<lines.length&&!lines[end].trim().isEmpty()){
   Guide next=guide(lines[end]);
   if(next!=null){if(!layout.same(next))return null;if(!group.isEmpty()){groups.add(group);group=new ArrayList<>();ruled=true;}end++;continue;}
   if(divider(lines[end]))break;
   List<String> row=alignedCells(lines[end],layout);if(row==null)break;
   if(++count*header.size()>512)return null;group.add(row);end++;
  }
  if(!group.isEmpty())groups.add(group);if(groups.isEmpty())return null;
  ArrayList<List<String>> rows=new ArrayList<>();rows.add(header);
  for(List<List<String>> physical:groups){if(ruled)rows.add(combine(physical));else rows.addAll(physical);}
  if(rows.size()*header.size()>512)return null;
  return new Match(new MarkdownTables.Block(offsets[start],Math.min(textLength,offsets[end]),rows,new int[header.size()]),end);
 }
 static Match read(String[] lines,int[] offsets,int start,int textLength,int columns){
  Match aligned=aligned(lines,offsets,start,textLength);if(aligned!=null)return aligned;
  int header=start;boolean top=divider(lines[start]);
  if(top){if(bottom(lines[start]))return null;header++;}
  if(header>=lines.length)return null;
  List<String> first=cells(lines[header]);
  if(first.size()<2||first.size()>32)return null;
  int rule=header+1;
  ArrayList<List<String>> headings=new ArrayList<>();headings.add(first);
  if(top)while(rule<lines.length&&!divider(lines[rule])&&rule<header+8){
   List<String> next=cells(lines[rule]);if(next.size()!=first.size())return null;headings.add(next);rule++;
  }
  if(rule>=lines.length||!divider(lines[rule]))return null;
  // Ordinary Markdown uses its own strict parser. Printed tables have box characters
  // or a horizontal rule instead of a Markdown delimiter for every column.
  boolean distinct=top||printed(lines[header])||lines[rule].matches(".*[─━═┼┬┴+].*")||!lines[rule].contains("|");
  if(!distinct)return null;
  ArrayList<List<String>> rows=new ArrayList<>();rows.add(combine(headings));
  ArrayList<List<List<String>>> groups=new ArrayList<>();ArrayList<List<String>> group=new ArrayList<>();
  int end=rule+1;boolean rowRules=false;
  while(end<lines.length&&!lines[end].trim().isEmpty()){
   if(divider(lines[end])){
    if(bottom(lines[end])){if(!group.isEmpty()){groups.add(group);group=new ArrayList<>();}end++;break;}
    List<String> after=end+1<lines.length?cells(lines[end+1]):Collections.emptyList();
    if(after.size()!=first.size()){if(!group.isEmpty()){groups.add(group);group=new ArrayList<>();}end++;break;}
    if(!group.isEmpty()){groups.add(group);group=new ArrayList<>();rowRules=true;}end++;continue;
   }
   Row row=row(lines,end,first.size(),columns);if(row.cells.size()!=first.size())break;
   group.add(row.cells);end=row.next;
  }
  if(!group.isEmpty())groups.add(group);
  if(groups.isEmpty())return null;
  for(List<List<String>> physical:groups){
   // Explicit borders define logical row boundaries; preserve wrapped cell lines.
   if(rowRules)rows.add(combine(physical));else rows.addAll(physical);
  }
  if(rows.size()*first.size()>512)return null;
  return new Match(new MarkdownTables.Block(offsets[start],Math.min(textLength,offsets[end]),rows,new int[first.size()]),end);
 }
}