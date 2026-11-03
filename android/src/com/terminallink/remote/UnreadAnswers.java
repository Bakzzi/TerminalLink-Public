package com.terminallink.remote;
import android.content.*;
import org.json.*;
import java.util.*;

// Small, device-local final-answer watermarks. No transcript, title or raw identity is saved.
// Receipts are shared with notifications; receipt pruning must not resurrect an old tab dot.
final class UnreadAnswers {
 static SharedPreferences store(Context c){return c.getSharedPreferences("unread-answers",0);}
 static String prefix(String pc){return RequestTracker.digest(pc)+":";}
 static synchronized void observe(Context c,String pc,String session,JSONObject conversation){
  if(!ConversationStatus.available(conversation))return;
  long latest=0;JSONArray messages=conversation.optJSONArray("messages");
  for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(m==null)continue;String phase=m.optString("phase");
   if(m.optString("role").equals("assistant")&&(phase.equals("final")||phase.equals("final_answer"))&&!m.optString("text").trim().isEmpty())latest=Math.max(latest,m.optLong("sequence"));
  }
  note(c,pc,session,conversation.optString("id"),latest);
 }
 static synchronized void note(Context c,String pc,String session,String thread,long sequence){
  if(pc.isEmpty()||session.isEmpty()||thread.isEmpty())return;
  SharedPreferences p=store(c);String scope=prefix(pc),key=scope+"session:"+RequestTracker.digest(session),t=RequestTracker.digest(thread);
  SharedPreferences.Editor edit=p.edit();boolean changed=false;
  if(!t.equals(p.getString(key,""))){edit.putString(key,t);changed=true;}
  String latest=scope+"latest:"+t,read=scope+"read:"+t;
  if(sequence>p.getLong(latest,0)){edit.putLong(latest,sequence).putString(scope+"tag:"+t,AnswerReceipts.tag(thread,sequence));changed=true;}
  if(sequence>p.getLong(read,0)&&AnswerReceipts.contains(c,pc,thread,sequence)){edit.putLong(read,sequence);changed=true;}
  if(changed)edit.apply();
 }
 static synchronized boolean has(Context c,String pc,String session){
  if(pc.isEmpty()||session.isEmpty())return false;SharedPreferences p=store(c);String scope=prefix(pc),t=p.getString(scope+"session:"+RequestTracker.digest(session),"");
  return !t.isEmpty()&&p.getLong(scope+"latest:"+t,0)>p.getLong(scope+"read:"+t,0);
 }
 static synchronized void read(Context c,String pc,Collection<String> tags){
  if(pc.isEmpty()||tags.isEmpty())return;SharedPreferences p=store(c);String scope=prefix(pc),start=scope+"tag:";SharedPreferences.Editor edit=p.edit();boolean changed=false;
  for(Map.Entry<String,?> entry:p.getAll().entrySet())if(entry.getKey().startsWith(start)&&tags.contains(entry.getValue())){
   String t=entry.getKey().substring(start.length());long latest=p.getLong(scope+"latest:"+t,0);
   if(latest>p.getLong(scope+"read:"+t,0)){edit.putLong(scope+"read:"+t,latest);changed=true;}
  }
  if(changed)edit.apply();
 }
 static synchronized void replied(Context c,String pc,String thread,long sequence){
  if(pc.isEmpty()||thread.isEmpty()||sequence<=0)return;SharedPreferences p=store(c);String scope=prefix(pc),t=RequestTracker.digest(thread);
  // User-message sequence is exclusive: a subsequent answer must still become unread.
  long read=sequence-1;if(read>p.getLong(scope+"read:"+t,0))p.edit().putLong(scope+"read:"+t,read).apply();
 }
}
