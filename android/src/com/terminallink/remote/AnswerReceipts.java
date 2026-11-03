package com.terminallink.remote;
import android.content.*;
import android.app.*;
import org.json.*;
import java.util.*;

// Explicitly read/replied answers survive transport replay and service recreation.
// Individual answers are retained, so reading the latest answer does not mark older history read.
final class AnswerReceipts {
 static String pc(Context c){LinkService live=LinkService.current;return live!=null&&!live.pin.isEmpty()?live.pin:c.getSharedPreferences("reply-state",0).getString("pin","");}
 static String tag(String thread,long sequence){return RequestTracker.digest(thread+":"+sequence);}
 static boolean contains(Context c,String pc,String tag){return !pc.isEmpty()&&!tag.isEmpty()&&c.getSharedPreferences("answer-receipts",0).contains(RequestTracker.digest(pc+":"+tag));}
 static String threadKey(String pc,String thread){return RequestTracker.digest(pc+":"+thread);}
 static boolean contains(Context c,String pc,String thread,long sequence){
  return contains(c,pc,tag(thread,sequence))||(!pc.isEmpty()&&!thread.isEmpty()&&sequence>0&&sequence<c.getSharedPreferences("answer-reply-watermarks",0).getLong(threadKey(pc,thread),0));
 }
 static boolean dismissed(Context c,String pc,Notification note,String tag){
  if(contains(c,pc,tag))return true;
  try{
   JSONObject saved=new JSONObject(c.getSharedPreferences("notification-actions",0).getString(note.extras.getString("tl-nonce",""),"{}")),route=saved.optJSONObject("context");
   return pc.equals(saved.optString("pcCertificate"))&&route!=null&&route.optString("kind").equals("answer")&&contains(c,pc,route.optString("thread"),route.optLong("sequence"));
  }catch(Exception ignored){return false;}
 }
 static synchronized void replied(Context c,String pc,String thread,long sequence){
  if(pc.isEmpty()||thread.isEmpty()||sequence<=0)return;
  SharedPreferences watermarks=c.getSharedPreferences("answer-reply-watermarks",0);String key=threadKey(pc,thread);
  if(sequence<=watermarks.getLong(key,0))return;
  // Persist only a sequence per verified original conversation, never its text.
  if(!watermarks.edit().putLong(key,sequence).commit())return;
  UnreadAnswers.replied(c,pc,thread,sequence);
  NotificationManager nm=c.getSystemService(NotificationManager.class);boolean changed=false;
  for(android.service.notification.StatusBarNotification item:nm.getActiveNotifications()){
   Notification note=item.getNotification();
   // SystemUI's remote-input row must finish through the existing success acknowledgement.
   if(item.getId()!=LinkService.ANSWER_ID||note.extras.getBoolean("tl-stale-reply")||"sending".equals(note.extras.getString("tl-state")))continue;
   try{
    JSONObject saved=new JSONObject(c.getSharedPreferences("notification-actions",0).getString(note.extras.getString("tl-nonce",""),"{}")),route=saved.optJSONObject("context");
    if(pc.equals(saved.optString("pcCertificate"))&&route!=null&&thread.equals(route.optString("thread"))&&route.optLong("sequence")>0&&route.optLong("sequence")<sequence){nm.cancel(item.getTag(),item.getId());changed=true;}
   }catch(Exception ignored){}
  }
  if(changed)NotificationSummary.update(c,LinkService.ANSWER_ID,LinkService.SUMMARY_ID,LinkService.ANSWER_CHANNEL,"codex_replies","Codex 답변 도착",null,null,null);
 }
 static void observe(Context c,String pc,JSONObject conversation){
  if(!ConversationStatus.available(conversation))return;
  JSONArray messages=conversation.optJSONArray("messages");long latest=0;
  for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(m!=null&&m.optString("role").equals("user"))latest=Math.max(latest,m.optLong("sequence"));}
  replied(c,pc,conversation.optString("id"),latest);
 }
 static boolean mark(Context c,String pc,Collection<String> tags){
  if(pc.isEmpty()||tags.isEmpty())return false;SharedPreferences prefs=c.getSharedPreferences("answer-receipts",0);SharedPreferences.Editor edit=prefs.edit();long now=System.currentTimeMillis();int added=0;
  ArrayList<String> batch=new ArrayList<>(new LinkedHashSet<>(tags));if(batch.size()>1024)batch=new ArrayList<>(batch.subList(batch.size()-1024,batch.size()));
  for(String tag:batch){if(tag.isEmpty())continue;String key=RequestTracker.digest(pc+":"+tag);if(!prefs.contains(key)){edit.putLong(key,now);added++;}}
  if(added==0){UnreadAnswers.read(c,pc,tags);return false;}
  Map<String,?> all=prefs.getAll();int remove=Math.max(0,all.size()+added-1024);
  if(remove>0){ArrayList<String> keys=new ArrayList<>(all.keySet());Collections.sort(keys,(a,b)->Long.compare(prefs.getLong(a,0),prefs.getLong(b,0)));for(int i=0;i<Math.min(remove,keys.size());i++)edit.remove(keys.get(i));}
  boolean saved=edit.commit();if(saved)UnreadAnswers.read(c,pc,tags);return saved;
 }
}
