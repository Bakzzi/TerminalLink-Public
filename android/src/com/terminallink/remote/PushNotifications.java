package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.os.*;
import org.json.*;
import java.util.*;

final class PushNotifications {
 static String signature(String thread,String key,String question,JSONArray choices){return RequestTracker.digest(thread+"|"+key+"|"+question+"|"+choices);}
 static boolean recordRequest(Context c,JSONObject v,String id,String thread){
  JSONObject p=v.optJSONObject("prompt");SharedPreferences state=c.getSharedPreferences("push-state",Context.MODE_PRIVATE);
  boolean known=p!=null&&signature(thread,v.optString("requestKey"),p.optString("question"),p.optJSONArray("choices")).equals(state.getString("request:"+id,""));
  SharedPreferences.Editor edit=state.edit().putLong("stamp:"+id,v.optLong("stamp"));
  if(!v.optBoolean("active"))edit.remove("request:"+id);else if(p!=null)edit.putString("request:"+id,signature(thread,v.optString("requestKey"),p.optString("question"),p.optJSONArray("choices")));
  edit.apply();return known;
 }
 static void show(Context context,JSONObject value)throws Exception{
  synchronized(AnswerReceipts.class){
  SharedPreferences state=context.getSharedPreferences("reply-state",Context.MODE_PRIVATE),prefs=context.getSharedPreferences("connection",Context.MODE_PRIVATE),push=context.getSharedPreferences("push-state",Context.MODE_PRIVATE);
  String pc=value.optString("pc"),id=value.optString("id"),kind=value.optString("kind"),thread=value.optString("thread");
  if(!state.getBoolean("enabled",false)||!pc.equals(state.getString("pin",""))||id.isEmpty()||thread.isEmpty())return;
  if(kind.equals("request")&&new RequestCompletions(state.getString("completed-requests","{}")).contains(id,thread,value.optString("requestKey")))return;
  NotificationManager nm=context.getSystemService(NotificationManager.class);
  LinkService live=LinkService.current;
  boolean answer=kind.equals("answer");if(!answer&&!kind.equals("request")&&!kind.equals("clear"))return;
  AnswerReceipts.replied(context,pc,thread,value.optLong("repliedThrough"));
  if(!answer){long stamp=value.optLong("stamp");if(stamp<=push.getLong("stamp:"+id,0))return;push.edit().putLong("stamp:"+id,stamp).commit();}
  if(kind.equals("clear")){push.edit().remove("request:"+id).commit();if(live!=null)live.clearRequest(id);else {nm.cancel(RequestTracker.digest(id),LinkService.REQUEST_ID);NotificationSummary.update(context,LinkService.REQUEST_ID,LinkService.REQUEST_SUMMARY_ID,LinkService.REQUEST_CHANNEL,"codex_requests","Codex 응답 요청",null,RequestTracker.digest(id),null);}return;}
  JSONObject route=new JSONObject(value.toString());route.remove("pc");route.remove("expires");route.remove("text");
  String tag,text,title,channel;int noteId;
  if(answer){
   long sequence=value.optLong("sequence");UnreadAnswers.note(context,pc,id,thread,sequence);ReplyCursor cursor=live!=null?live.cursor:new ReplyCursor(state.getString("cursors","{}"));
   if(cursor.seen.containsKey(thread)&&cursor.seen.get(thread)>=sequence)return;
   cursor.seen.put(thread,sequence);while(cursor.seen.size()>128)cursor.seen.remove(cursor.seen.keySet().iterator().next());state.edit().putString("cursors",cursor.save()).commit();
   tag=SecureClient.hex(java.security.MessageDigest.getInstance("SHA-256").digest((thread+":"+sequence).getBytes(java.nio.charset.StandardCharsets.UTF_8)));
   if(AnswerReceipts.contains(context,pc,thread,sequence))return;
   text=value.optString("text");title=NotificationTitles.answer(value.optString("title"));channel=LinkService.ANSWER_CHANNEL;noteId=LinkService.ANSWER_ID;
  }else{
   String signature=signature(thread,value.optString("requestKey"),value.optString("question"),value.optJSONArray("choices"));
   String previous=push.getString("request:"+id,"");if(signature.equals(previous))return;push.edit().putString("request:"+id,signature).commit();
   tag=RequestTracker.digest(id);text=value.optString("question");title="Codex 응답 요청";channel=LinkService.REQUEST_CHANNEL;noteId=LinkService.REQUEST_ID;
  }
  if(live!=null&&live.viewer!=null&&live.viewer.viewingSession(id))return;
  if(!prefs.getBoolean(answer?"answer-notifications":"request-notifications",true)||!nm.areNotificationsEnabled())return;
  NotificationChannel nc=new NotificationChannel(channel,answer?"Codex 최종 답변":"질문·승인 요청",NotificationManager.IMPORTANCE_HIGH);nc.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);nm.createNotificationChannel(nc);
  int icon=context.getResources().getIdentifier("ic_notification","drawable",context.getPackageName());
  Intent open=new Intent(context,MainActivity.class).setAction(Intent.ACTION_VIEW).setData(android.net.Uri.parse("terminallink://"+(answer?"answer":"request")+"/"+tag)).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP);
  open.putExtra("reply-session",id).putExtra("reply-pin",pc);if(answer)open.putExtra("reply-key",tag);else open.putExtra("request-view",true);
  PendingIntent content=PendingIntent.getActivity(context,0,open,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  Notification publicNotice=new Notification.Builder(context,channel).setSmallIcon(icon).setContentTitle(answer?"Codex 답변 도착":title).setContentText("TerminalLink에서 확인하세요.").build();
  Notification.Builder b=new Notification.Builder(context,channel).setSmallIcon(icon).setContentTitle(title).setContentText(text).setGroup(answer?"codex_replies":"codex_requests").setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN).setStyle(new Notification.BigTextStyle().bigText(text)).setContentIntent(content).setAutoCancel(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_MESSAGE).setVisibility(Notification.VISIBILITY_PRIVATE).setPublicVersion(publicNotice);
  NotificationReplies.decorate(context,pc,!value.optBoolean("openOnly"),b,route);
  Notification posted=b.build();nm.notify(tag,noteId,posted);NotificationSummary.update(context,noteId,answer?LinkService.SUMMARY_ID:LinkService.REQUEST_SUMMARY_ID,channel,answer?"codex_replies":"codex_requests",title,null,tag,posted);
  }
 }
}
