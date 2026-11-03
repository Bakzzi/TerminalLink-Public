package com.terminallink.remote;
import java.util.*;
import java.util.regex.*;

// Parse only a currently visible CLI prompt, never historical conversation text.
final class RequestPrompt {
 static final Pattern QUESTION=Pattern.compile("(?m)^\\s*Question\\s+(\\d+)/(\\d+)[^\\n]*$");
 static final Pattern OPTION=Pattern.compile("^\\s*([›>])?\\s*(\\d+)\\.\\s+(.*)$");
 static final Pattern FOLLOWUP_FOOTER=Pattern.compile("(?im)^\\s*enter\\s+submit\\s+ctrl\\s*\\+\\s*\\]\\s+skip\\s+(?:option\\s+\\d+/\\d+\\s+)?(?:alt|\u2325)\\s*\\+\\s*\u2193\\s+(?:main prompt|prev question)(?:\\s+(?:alt|\u2325)\\s*\\+\\s*\u2191\\s+(?:next question|queued messages))?\\s*\\z");
 static final Pattern FOLLOWUP_HEADER=Pattern.compile("(?m)^\\s*(?:\u2022\\s*)?Queued follow-up inputs\\s*$");
 final String heading,question,key,details;final List<String> choices;final int selected;
 final boolean notes,questionKind,followup;
 RequestPrompt(String h,String q,List<String> c,int s,boolean n,boolean k,String d){
  this(h,q,c,s,n,k,d,false);
 }
 RequestPrompt(String h,String q,List<String> c,int s,boolean n,boolean k,String d,boolean f){
  heading=h;question=q;choices=c;selected=s;notes=n;questionKind=k;details=d;followup=f;
  String identity=h+"\n"+q+"\n"+String.join("\n",c);
  key=f?identity.replaceAll("\\s+",""):identity;
 }
 static RequestPrompt parse(String text){
  String tail=text.substring(Math.max(0,text.length()-16000)).replace("\r","");
  RequestPrompt followup=parseFollowup(tail);if(followup!=null)return followup;
  String lower=tail.toLowerCase(Locale.ROOT).trim();
  boolean question=lower.matches("(?s).*enter to submit (?:answer|all)\\s*(?:\\|\\s*(?:esc to interrupt|←/→ to navigate questions|ctrl \\+ p / ctrl \\+ n change question)\\s*)*$");
  boolean approval=lower.matches("(?s).*press enter to confirm[^\\n]*$");
  if(!question&&!approval)return null;
  int start=-1;String heading="승인 요청";
  if(question){Matcher m=QUESTION.matcher(tail);while(m.find()){start=m.end();heading="질문 "+m.group(1)+" / "+m.group(2);}}
  else{
   for(String marker:new String[]{"Would you like to ","Do you want to ","Do you approve ","Submit with unanswered questions?"}){
    int at=tail.lastIndexOf(marker);if(at>start)start=at;
   }
  }
  if(start<0)return null;
  String[] lines=tail.substring(start).split("\n");
  List<String> options=new ArrayList<>();StringBuilder body=new StringBuilder(),details=new StringBuilder();
  int selected=-1;boolean notes=false,afterOptions=false;
  for(String line:lines){
   String clean=line.trim(),low=clean.toLowerCase(Locale.ROOT);
   if(low.contains("enter to submit ")||low.startsWith("tab to add notes")||low.startsWith("tab or esc to clear notes")||low.startsWith("press enter to confirm")){
    if(low.contains("tab or esc to clear notes")||low.contains("tab to select")||low.contains("tab to pick")||low.contains("tab to switch"))notes=true;
    break;
   }
   Matcher m=OPTION.matcher(line);
   if(m.matches()){
    int number;try{number=Integer.parseInt(m.group(2));}catch(Exception e){return null;}
    if(number!=options.size()+1||number>12)return null;
    options.add(m.group(3).trim().replaceAll("\\s{2,}","\n"));afterOptions=true;
    if(m.group(1)!=null)selected=number-1;
   }else if(!afterOptions){
    if(clean.startsWith("›")){notes=true;details.append(clean.substring(1).trim());continue;}
    if(!clean.isEmpty())body.append(body.length()==0?"":question?" ":"\n").append(clean);
   }else if(!clean.isEmpty()){
    // Notes are a separate composer block. Keep its contents visible, not inside an option label.
    if(clean.startsWith("›")||low.equals("notes")||low.equals("add notes")||low.contains("type your answer")||low.contains("select an option to add notes"))notes=true;
    if(notes)details.append(details.length()==0?"":"\n").append(clean.replaceFirst("^›\\s*",""));
    else if(!options.isEmpty()){int last=options.size()-1;options.set(last,options.get(last)+" "+clean);}
   }
  }
  if(body.length()==0)return null;
  return new RequestPrompt(heading,body.toString(),options,selected,notes,question,details.toString());
 }

 static boolean requiresAction(String title){return title.toLowerCase(Locale.ROOT).replaceFirst("^\\s*\\[[^\\]]{1,12}\\]\\s*","").trim().startsWith("action required");}
 static boolean collapsed(String text){
  String tail=text.substring(Math.max(0,text.length()-2400));
  return tail.contains("Queued follow-up inputs")&&Pattern.compile("(?im)^\\s*(?:alt|\u2325)\\s*\\+\\s*\u2191\\s+to answer\\s*$").matcher(tail).find();
 }
 static RequestPrompt parseFollowup(String tail){return parseFollowup(tail,"");}
 static RequestPrompt parseFollowup(String tail,String expectedQuestion){
  Matcher footer=FOLLOWUP_FOOTER.matcher(tail);if(!footer.find())return null;
  Matcher header=FOLLOWUP_HEADER.matcher(tail.substring(0,footer.start()));int start=-1;
  while(header.find())start=header.end();if(start<0)return null;
  String block=tail.substring(start,footer.start()).trim(),heading="\ucd94\uac00 \uc9c8\ubb38";
  // Queued user messages precede the question. Exclude only their marked block.
  block=block.replaceFirst("(?s)^\u21b3[^\\n]*(?:\\n[^\\n]+)*?\\n\\s*\\n","");
  Matcher progress=Pattern.compile("^(\\d+) of (\\d+)\\s*\\n").matcher(block);
  if(progress.find()){heading+=" "+progress.group(1)+" / "+progress.group(2);block=block.substring(progress.end()).trim();}
  String[] lines=block.split("\\n");List<String> options=new ArrayList<>();StringBuilder body=new StringBuilder();
  int selected=-1;boolean choices=false;
  for(String line:lines){
   Matcher m=OPTION.matcher(line);
   if(m.matches()){
    int number;try{number=Integer.parseInt(m.group(2));}catch(Exception e){return null;}
    if(number!=options.size()+1||number>12)return null;
    choices=true;options.add(m.group(3).trim());if(m.group(1)!=null)selected=number-1;
   }else if(choices){
    if(!line.trim().isEmpty()){int i=options.size()-1;options.set(i,options.get(i)+" "+line.trim());}
   }else body.append(line).append('\n');
  }
  if(choices){
   if(options.size()<2||selected<0||body.toString().trim().isEmpty())return null;
   // Last choice is the CLI's inline Other editor. Its draft/placeholder is not question identity.
   options.set(options.size()-1,"\uc9c1\uc811 \uc785\ub825");
   return new RequestPrompt(heading,reflow(body.toString()),options,selected,selected==options.size()-1,true,"",true);
  }
  // Last separated block is the freeform composer, not part of the question.
  Matcher gap=Pattern.compile("\\n[ \\t]*\\n").matcher(block);int split=-1;
  while(gap.find()){if(expectedQuestion.isEmpty())split=gap.start();else if(block.substring(0,gap.start()).replaceAll("\\s+","").equals(expectedQuestion.replaceAll("\\s+",""))){split=gap.start();break;}}if(split<0)return null;
  String question=block.substring(0,split).trim();if(question.isEmpty()||question.startsWith("\u21b3"))return null;
  return new RequestPrompt(heading,reflow(question),Collections.emptyList(),-1,true,true,"",true);
 }
 static String reflow(String text){return text.trim().replaceAll("[ \\t]*\\n[ \\t]*"," ").replaceAll(" {2,}"," ");}
}
