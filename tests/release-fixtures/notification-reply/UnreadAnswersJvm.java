package com.terminallink.remote;
import android.content.*;import org.json.*;import java.util.*;
public class UnreadAnswersJvm {
 static void check(boolean value,String message){NotificationReplyJvm.check(value,message);}
 static JSONObject chat(String thread,int sequence,String source){return NotificationReplyJvm.chat(thread,sequence,source);}
 static boolean unread(LinkService s,String tab){return UnreadAnswers.has(s,s.pin,tab);}
 static void run()throws Exception{
  LinkService s=new LinkService();LinkService.current=s;s.state.edit().putString("pin",s.pin).putBoolean("enabled",true).commit();
  JSONObject progress=chat("thread-a",1,"record");progress.getJSONArray("messages").getJSONObject(0).put("phase","commentary");s.observe("a","A",progress);
  check(!unread(s,"a"),"Progress alone does not show unread dot");
  s.prefs.edit().putBoolean("answer-notifications",false).commit();s.observe("a","A",chat("thread-a",2,"record"));s.observe("b","B",chat("thread-b",2,"remote"));
  check(unread(s,"a")&&unread(s,"b")&&s.nm.posts==0,"Local and SSH dots work with notifications disabled");
  s.readAnswers("a","thread-a",new HashSet<>(Arrays.asList(2L)));
  check(!unread(s,"a")&&unread(s,"b"),"Reading one tab clears only its dot");
  s.observe("a","Renamed A",chat("thread-a",2,"record"));s.observe("a","A",chat("thread-a",1,"record"));
  check(!unread(s,"a"),"Replay, old snapshots and renaming cannot reignite a read dot");
  s.observe("a","A",chat("thread-a",4,"record"));s.readAnswers("a","thread-a",new HashSet<>(Arrays.asList(2L)));
  check(unread(s,"a"),"Reading an older answer does not clear a newer final reply");
  s.observe("a","A",null);s.observe("a","A",chat("screen",99,"terminal"));
  check(unread(s,"a"),"Transient failure and raw terminal text preserve the dot");
  s.observe("a2","Second view",chat("thread-a",4,"record"));s.readAnswers("a","thread-a",new HashSet<>(Arrays.asList(4L)));
  check(!unread(s,"a")&&!unread(s,"a2"),"Two tabs showing the same conversation share mobile read state");
  s.getSharedPreferences("answer-receipts",0).edit().remove(RequestTracker.digest(s.pin+":"+AnswerReceipts.tag("thread-a",4))).commit();
  s.observe("a","A",chat("thread-a",4,"record"));check(!unread(s,"a"),"Pruning old notification receipts cannot resurrect the tab dot");
  s.observe("a","New conversation",chat("new-thread",1,"record"));check(unread(s,"a"),"Reused tab with a different conversation has independent unread state");
  s.observe("a","New conversation",chat("new-thread",4,"record"));AnswerReceipts.replied(s,s.pin,"new-thread",3);
  check(unread(s,"a"),"Reply before a new answer cannot clear the new dot");AnswerReceipts.replied(s,s.pin,"new-thread",5);
  check(!unread(s,"a")&&unread(s,"b"),"Verified PC or phone reply clears its tab without affecting another");
  JSONObject push=NotificationReplyJvm.push(s,"p","push-thread",8);PushNotifications.show(s,push);check(unread(s,"p"),"Push-only delivery records unread without notification permission");
  AnswerReceipts.mark(s,s.pin,Collections.singleton(AnswerReceipts.tag("push-thread",8)));PushNotifications.show(s,push);
  check(!unread(s,"p"),"Read or successful inline reply suppresses replayed push dot");
  s.prefs.edit().putBoolean("answer-notifications",true).commit();PushNotifications.show(s,NotificationReplyJvm.push(s,"p","push-thread",9));
  android.app.Notification n=NotificationReplyJvm.find(s,7002,AnswerReceipts.tag("push-thread",9));s.replies.handle(NotificationReplyJvm.reply(n,"synthetic reply"));JSONObject sent=s.sent.get(s.sent.size()-1);
  s.replies.result(MainActivity.obj("request",sent.optString("request"),"ok",false,"message","failure"));s.ui.runShort();check(unread(s,"p"),"Failed notification reply leaves unread dot");
  PushNotifications.show(s,NotificationReplyJvm.push(s,"p","push-thread",10));n=NotificationReplyJvm.find(s,7002,AnswerReceipts.tag("push-thread",10));s.replies.handle(NotificationReplyJvm.reply(n,"synthetic success"));sent=s.sent.get(s.sent.size()-1);
  s.replies.result(MainActivity.obj("request",sent.optString("request"),"ok",true));s.ui.runShort();check(!unread(s,"p"),"Successful notification reply clears unread dot");
  PushNotifications.show(s,NotificationReplyJvm.push(s,"p","push-thread",11));
  LinkService reopened=new LinkService();SharedPreferences.Editor copy=UnreadAnswers.store(reopened).edit();
  for(Map.Entry<String,?> entry:UnreadAnswers.store(s).getAll().entrySet()){Object v=entry.getValue();if(v instanceof Long)copy.putLong(entry.getKey(),(Long)v);else copy.putString(entry.getKey(),(String)v);}copy.commit();
  check(unread(reopened,"p")&&!unread(reopened,"a")&&unread(reopened,"b"),"Read and unread states survive recreation using persisted metadata only");
  check(!UnreadAnswers.has(reopened,"different-pc","p"),"Another PC cannot inherit a tab dot");
  String saved=UnreadAnswers.store(s).getAll().toString();check(!saved.contains("push-thread")&&!saved.contains("new-thread")&&!saved.contains("답변 내용")&&!saved.contains("fixture-pc"),"Unread storage contains hashes and sequence numbers, not private text or raw identities");
  LinkService.current=null;
 }
}
