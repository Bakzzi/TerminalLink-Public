package com.terminallink.remote;
import org.json.*;
import java.util.*;

// Execute the actual ReadingView update/render/diff methods; Android surfaces only are stand-ins.
class ReadingView {
 static final int VISIBLE=0,GONE=8;
 final MainActivity activity=new MainActivity(); final Widget body=new Widget(),readNoticeText=new Widget(),readNotice=new Widget(),retryButton=new Widget(),conversationButton=new Widget(),terminalButton=new Widget(),scroll=new Widget(),content=new Widget();
 final TableTranscript tables=new TableTranscript();int sourceColumns,sourceRows;Object pendingRestore;boolean restoreBottom;int restoreY;int restores;
 /* PRODUCTION_FIELDS */
 /* PRODUCTION_METHODS */
 boolean hasSelection(){return false;}void scheduleRead(){}void restoreAfterLayout(boolean bottom,int y){restores++;restoreBottom=bottom;restoreY=y;}
}
class MainActivity {String selectedId="tab-a",draft="keep my draft";Link link=new Link();void updateInputTools(boolean c){}void updateWeekly(JSONObject c){}int dp(int v){return v;}}
class Link {SentMessages sentMessages=new SentMessages();}
class SentMessages {JSONArray project(String s,String t,JSONArray m){return m;}}
class Widget {String text="";int writes,visibility;Typeface face;void setEnabled(boolean v){}void setVisibility(int v){visibility=v;}void setText(CharSequence s){text=s.toString();writes++;}CharSequence getText(){return text;}int getScrollY(){return 120;}int getHeight(){return 500;}Typeface getTypeface(){return face;}void setTypeface(Typeface t){face=t;}void setAlpha(float v){}}
class Typeface {static final Typeface DEFAULT=new Typeface(),MONOSPACE=new Typeface();static final int BOLD=1;}
class ForegroundColorSpan {ForegroundColorSpan(int x){}}class StyleSpan {StyleSpan(int x){}}
interface Spanned {int SPAN_EXCLUSIVE_EXCLUSIVE=33;}interface Spannable extends CharSequence {}
class Selection {static void removeSelection(Spannable s){}}
class SpannableStringBuilder implements CharSequence {StringBuilder value=new StringBuilder();SpannableStringBuilder append(String s){value.append(s);return this;}void setSpan(Object s,int a,int b,int c){}public int length(){return value.length();}public char charAt(int n){return value.charAt(n);}public CharSequence subSequence(int a,int b){return value.subSequence(a,b);}public String toString(){return value.toString();}}
class TableTranscript {int shows,clears;void clear(){clears++;}void show(Object text,Object blocks,boolean same){shows++;}}
class MarkdownTables {static class Block {int start,end;Object rows,align;Block(int s,int e,Object r,Object a){start=s;end=e;rows=r;align=a;}}static List<Block> parse(String text,int columns){return text.contains("|")?List.of(new Block(0,text.length(),null,null)):List.of();}}
public class ReadingJvm {
 static void check(boolean b,String m){if(!b)throw new AssertionError(m);System.out.println("PASS "+m);}
 static JSONObject obj(Object... kv){JSONObject x=new JSONObject();for(int i=0;i<kv.length;i+=2)x.put((String)kv[i],kv[i+1]);return x;}
 static JSONObject chat(String thread,String answer){return obj("id",thread,"source","record","readState","ready","messages",new JSONArray().put(obj("role","user","text","Question","sequence",1)).put(obj("role","assistant","phase","final","text",answer,"sequence",2)));}
 static JSONObject frame(String tab,Object chat){return obj("id",tab,"text","terminal progress","conversation",chat);}
 public static void main(String[] args){
  for(String answer:new String[]{"Original answer","| column | value |\n| --- | --- |\n| x | y |"}){
   ReadingView v=new ReadingView();JSONObject ready=chat("thread-a",answer);v.update(frame("tab-a",ready));String original=v.body.text;int writes=v.body.writes,restores=v.restores,shows=v.tables.shows,clears=v.tables.clears;JSONArray rows=v.messages;
   for(Object failure:new Object[]{obj("id","unavailable:session-a","source","unavailable","readState","failed","readError","Temporary read failure"),JSONObject.NULL}){
    v.update(frame("tab-a",failure));
    check(v.messages==rows&&v.body.text.equals(original)&&v.body.writes==writes,"Read loss retains actual rendered messages without replacing the body");
    check(v.restores==restores&&v.tables.shows==shows&&v.tables.clears==clears,"Read loss does not rebuild tables or reset scroll");
    check(!v.readError.isEmpty()&&v.readOnly&&v.readNotice.visibility==0,"Read notice is separate and retained content grants no input authority");
    check(v.activity.draft.equals("keep my draft"),"Read failure leaves composer draft intact");
   }
   v.update(frame("tab-a",ready));check(v.readError.isEmpty()&&v.body.writes==writes&&v.readNotice.visibility==8,"Recovery with identical history removes only the notice");
   v.update(frame("tab-a",chat("thread-a","New final answer")));check(v.body.text.contains("New final answer")&&v.body.writes==writes+1,"New verified content updates normally after recovery");
   v.update(frame("tab-b",JSONObject.NULL));check(!v.body.text.contains("New final answer")&&v.thread.isEmpty(),"Another native tab never inherits old history");
   v.update(frame("tab-b",chat("thread-b","Other thread")));v.update(frame("tab-b",obj("id","start:new","source","start","readState","empty","messages",new JSONArray())));check(!v.body.text.contains("Other thread")&&v.newConversation,"Confirmed new conversation clears the prior thread");
  }
  ReadingView v=new ReadingView();v.conversationMode=true;v.modeChosen=true;v.update(frame("tab-a",JSONObject.NULL));check(!v.body.text.contains("대화를 읽지 못했습니다")&&v.readNotice.visibility==0,"Initial failure places the error beside the composer, not throughout the transcript");
 }
}
