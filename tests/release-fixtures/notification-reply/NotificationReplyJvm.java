package com.terminallink.remote;
import android.app.*;import android.content.*;import android.os.*;import org.json.*;import java.util.*;
public class NotificationReplyJvm {
 static void check(boolean b,String s){if(!b)throw new AssertionError(s);System.out.println("PASS "+s);}
 static Notification find(LinkService s,int id,String tag){for(android.service.notification.StatusBarNotification n:s.nm.getActiveNotifications())if(n.getId()==id&&(tag==null||tag.equals(n.getTag())))return n.getNotification();return null;}
 static JSONObject chat(String t,int seq,String source){return MainActivity.obj("id",t,"source",source,"readOnly",true,"messages",new JSONArray().put(MainActivity.obj("role","assistant","phase","final","text","답변 내용","sequence",seq)));}
 static Intent reply(Notification n,String text){Intent i=n.actions[n.actions.length-1].actionIntent.intent;i.remote=new Bundle();i.remote.putCharSequence("message",text);return i;}
 public static void main(String[] a)throws Exception{
  LinkService s=new LinkService();s.state.edit().putString("pin",s.pin).putBoolean("enabled",true).commit();
  check(!NotificationReplyAccess.allowed(chat("t",1,"record"),MainActivity.obj("notificationReply",1)),"Old PC cannot enable daemon replies");
  check(!NotificationReplyAccess.allowed(chat("t",1,"terminal"),s.readyMessage),"Screen-only conversation cannot reply");
  s.observe("local","테스트 A",chat("thread",1,"record"));
  for(int seq=2;seq<=3;seq++){
   s.observe("local","테스트 A",chat("thread",seq,"record"));String tag=AnswerReceipts.tag("thread",seq);Notification n=find(s,7002,tag);
   check(n!=null&&n.actions.length==1&&n.actions[0].getRemoteInputs()!=null&&n.actions[0].auth,"Local answer has authenticated inline reply "+seq);
   check(n.extras.getCharSequence(Notification.EXTRA_TITLE).equals("테스트 A 답변 도착"),"Tab title preserved");
   String text="연속 긴 답장 🐱\n".repeat(200)+seq;Intent intent=reply(n,text);int before=s.sent.size();s.replies.handle(intent);
   JSONObject command=s.sent.get(s.sent.size()-1);check(s.sent.size()==before+1&&command.getString("text").equals(text)&&command.optLong("sequence")==seq&&command.optString("thread").equals("thread")&&command.optBoolean("nativeReply"),"Long reply retains text and original route "+seq);
   s.replies.handle(intent);check(s.sent.size()==before+1,"Repeated PendingIntent cannot duplicate input");
   s.replies.result(MainActivity.obj("request",command.getString("request"),"ok",true));s.ui.runShort();check(find(s,7002,tag)==null,"Successful reply removes child notification");
   s.replies.handle(intent);check(s.sent.size()==before+1,"Stale action never targets new answer");
  }
  JSONObject push=MainActivity.obj("kind","answer","pc",s.pin,"id","ssh","thread","remote:fixture","sequence",9,"title","테스트 B","text","원격 답변","openOnly",false,"nativeReply",true);
  PushNotifications.show(s,push);Notification pn=find(s,7002,AnswerReceipts.tag("remote:fixture",9));check(pn!=null&&pn.actions.length==1,"Background SSH push offers direct reply");
  Intent pi=reply(pn,"SSH reply");s.replies.handle(pi);JSONObject sent=s.sent.get(s.sent.size()-1);check(sent.optString("id").equals("ssh")&&sent.optString("thread").equals("remote:fixture"),"SSH push reply stays on original tab");
  s.observe("request","요청 탭",chat("request-thread",1,"record"));RequestPrompt prompt=new RequestPrompt("질문","테스트 질문",Arrays.asList("예","아니오","직접 입력"),0,false,true,"",true);
  s.postRequest("request",prompt,false);Notification q=find(s,7004,null);check(q!=null&&q.actions.length==3&&q.actions[2].getRemoteInputs()!=null,"Request notification offers choices and typed reply");
  s.replies.handle(reply(q,"직접 작성한 답변"));sent=s.sent.get(s.sent.size()-1);check(sent.optString("kind").equals("request")&&sent.optString("question").equals("테스트 질문")&&sent.optString("text").equals("직접 작성한 답변"),"Request direct reply retains question identity");
  s.replies.result(MainActivity.obj("request",sent.optString("request"),"ok",true));s.ui.runShort();check(find(s,7004,null)==null,"Request reply success removes notification");
  LinkService old=new LinkService();old.readyMessage=MainActivity.obj("notificationReply",1);old.observe("t","old",chat("o",1,"record"));old.observe("t","old",chat("o",2,"record"));check(find(old,7002,null).actions.length==0,"No broken action offered by an old companion");
  System.out.println("PASS Physical phone and OEM notification UI are outside JVM proof");
  replyClearsTab();
  requestRecovery();
  requestCompletion();
  UnreadAnswersJvm.run();
  MessageSendJvm.run();
  ScenarioJvm.main(a);
 }
 static void requestCompletion()throws Exception{
  LinkService s=new LinkService();s.observer=new LinkService.Observer();s.state.edit().putBoolean("enabled",true).putString("pin",s.pin).commit();
  JSONObject first=MainActivity.obj("id","call:0","title","First question","options",new JSONArray().put("Yes"));
  JSONObject second=MainActivity.obj("id","call:1","title","Second question","options",new JSONArray().put("Later"));
  JSONObject original=chat("thread",1,"record").put("requests",new JSONArray().put(first).put(second));
  String collapsed="• Queued follow-up inputs\nalt + ↑ to answer";
  s.observe("tab","Tab",original);s.lastFrame=MainActivity.obj("id","tab","conversation",original);s.observeRequest("tab","Action Required",collapsed);
  s.send(MainActivity.obj("op","notificationInput","kind","request","request","submit-0","id","tab","thread","thread","requestKey","call:0"));
  s.requestResult(MainActivity.obj("request","submit-0","ok",true));
  check(s.conversations.get("tab").getJSONArray("requests").length()==1&&s.lastFrame.getJSONObject("conversation").getJSONArray("requests").length()==1&&s.requests.contains("tab"),"Receipt filters service and visible caches immediately while preserving the second pending request");
  check(original.getJSONArray("requests").length()==2,"Completion projection never mutates the original record");
  s.observe("tab","Tab",original);s.observeRequest("tab","Action Required",collapsed);
  Notification pending=find(s,7004,RequestTracker.digest("tab"));check(pending!=null&&pending.text.equals("Second question"),"Stale snapshot cannot repost the answered request notification");
  s.send(MainActivity.obj("op","notificationInput","kind","request","request","submit-1","id","tab","thread","thread","requestKey","call:1"));
  s.requestResult(MainActivity.obj("request","submit-1","ok",false,"delivery","SentUnconfirmed","submissionId","canonical-1"));
  check(s.requests.contains("tab"),"Unconfirmed delivery never prematurely retires a request");
  s.requestResult(MainActivity.obj("request","canonical-1","ok",true));
  check(!s.requests.contains("tab")&&find(s,7004,RequestTracker.digest("tab"))==null&&s.observer.received==3,"Correlated status receipt clears the last request and its notification before any assistant final");
  s.observe("tab","Tab",original);s.observeRequest("tab","Action Required",collapsed);
  check(!s.requests.contains("tab")&&find(s,7004,null)==null,"Delayed terminal queue and animated title do not recreate a completed request");
  RequestCompletions restored=new RequestCompletions(s.state.getString("completed-requests","{}"));check(restored.filter("tab",original).getJSONArray("requests").length()==0,"Service persisted exact completion markers for reconnect/recreation");
  EventStream events=new EventStream(s);events.receive(MainActivity.obj("type","eventSnapshot","epoch","fixture","sequence",1,"states",new JSONArray().put(MainActivity.obj("type","replyEvent","id","tab","title","Action Required","conversation",original,"active",true,"requestKey","call:0","stamp",500))));
  check(!s.requests.contains("tab")&&find(s,7004,null)==null,"Delayed event snapshot cannot resurrect a completed request");
  LinkService.current=null;PushNotifications.show(s,MainActivity.obj("pc",s.pin,"kind","request","id","tab","thread","thread","requestKey","call:0","question","First question","stamp",501));
  check(find(s,7004,null)==null,"Delayed background push respects the persisted completion without a live service");
  JSONObject approval=chat("approval-thread",1,"record").put("requests",new JSONArray());s.observe("approval","Other tab",approval);
  s.observeRequest("approval","Action Required","Question 1/1\nContinue?\n› 1. Yes\n  2. No\nenter to submit answer");
  check(s.requests.contains("approval"),"An unrelated native approval remains actionable with an empty follow-up list");
 }
 static void requestRecovery()throws Exception{
  LinkService s=new LinkService();s.readyMessage.put("requestLifecycle",1);String local=UUID.randomUUID().toString(),canonical=UUID.randomUUID().toString();
  JSONObject context=MainActivity.obj("kind","request","id","tab","requestKey","call:0","thread","thread");
  JSONObject saved=MainActivity.obj("used",true,"pcCertificate",s.pin,"context",context,"result",MainActivity.obj("ok",false,"delivery","SentUnconfirmed","submissionId",canonical));
  s.replies.records.edit().putString(local,saved.toString()).commit();s.replies.recover();
  check(s.sent.size()==1&&s.sent.get(0).optString("op").equals("requestStatus")&&s.sent.get(0).optString("request").equals(local)&&s.sent.get(0).optString("submissionId").equals(canonical),"Request reconnect queries canonical submission with original notification correlation, never resends text");
  saved.put("result",MainActivity.obj("ok",true,"delivery","Accepted"));s.replies.records.edit().putString(local,saved.toString()).commit();s.replies.recover();check(s.sent.size()==1,"Accepted request notifications are not queried or submitted again");
 }
 static JSONObject user(int sequence){return MainActivity.obj("role","user","text","Recorded reply","sequence",sequence);}
 static JSONObject push(LinkService s,String tab,String thread,int sequence){return MainActivity.obj("pc",s.pin,"kind","answer","id",tab,"thread",thread,"sequence",sequence,"title",tab,"text","Synthetic answer","nativeReply",true);}
 static void replyClearsTab()throws Exception{
  LinkService s=new LinkService();s.state.edit().putString("pin",s.pin).putBoolean("enabled",true).commit();
  s.observe("a","Tab A",chat("a-thread",1,"record"));s.observe("b","Tab B",chat("b-thread",1,"remote"));
  s.observe("a","Tab A",chat("a-thread",2,"record"));s.observe("a","Tab A",chat("a-thread",3,"record"));s.observe("b","Tab B",chat("b-thread",2,"remote"));
  JSONObject joined=chat("a-thread",5,"record");joined.getJSONArray("messages").put(user(4));s.observe("a","Tab A",joined);
  check(find(s,7002,AnswerReceipts.tag("a-thread",2))==null&&find(s,7002,AnswerReceipts.tag("a-thread",3))==null,"Recorded app or PC reply clears all earlier answers on its conversation");
  check(find(s,7002,AnswerReceipts.tag("b-thread",2))!=null&&find(s,7002,AnswerReceipts.tag("a-thread",5))!=null,"Other tab and new answer in same batch are preserved");
  AnswerReceipts.replied(s,s.pin,"a-thread",2);
  check(find(s,7002,AnswerReceipts.tag("a-thread",5))!=null,"Out-of-order reply receipt cannot delete a newer answer");
  JSONObject wrong=chat("a-thread",5,"terminal");wrong.getJSONArray("messages").put(user(99));s.observe("a","Tab A",wrong);
  check(find(s,7002,AnswerReceipts.tag("a-thread",5))!=null,"Unverified screen text never acknowledges answers");
  s.cursor=new ReplyCursor("{}");s.replies=new NotificationReplies(s);PushNotifications.show(s,push(s,"a","a-thread",3));
  check(find(s,7002,AnswerReceipts.tag("a-thread",3))==null,"Persisted receipt suppresses delayed push after cursor reset and reply handler recreation");
  PushNotifications.show(s,push(s,"a","a-thread",6));Notification pending=find(s,7002,AnswerReceipts.tag("a-thread",6));s.replies.handle(reply(pending,"pending reply"));JSONObject sent=s.sent.get(s.sent.size()-1);
  AnswerReceipts.replied(s,s.pin,"a-thread",7);
  check(find(s,7002,AnswerReceipts.tag("a-thread",5))==null&&find(s,7002,AnswerReceipts.tag("a-thread",6))!=null,"Recorded reply clears older answers while SystemUI sending row awaits acknowledgement");
  s.replies.result(MainActivity.obj("request",sent.optString("request"),"ok",true));s.ui.runShort();
  check(find(s,7002,AnswerReceipts.tag("a-thread",6))==null,"Acknowledgement finishes and dismisses the sending row");
  PushNotifications.show(s,push(s,"a","a-thread",8));pending=find(s,7002,AnswerReceipts.tag("a-thread",8));s.replies.handle(reply(pending,"failed reply"));sent=s.sent.get(s.sent.size()-1);s.replies.result(MainActivity.obj("request",sent.optString("request"),"ok",false,"message","Synthetic failure"));s.ui.runShort();
  check(find(s,7002,AnswerReceipts.tag("a-thread",8))!=null&&!AnswerReceipts.contains(s,s.pin,"a-thread",8),"Failed send keeps the answer and does not advance reply receipt");
  LinkService.current=null;
  PushNotifications.show(s,MainActivity.obj("pc","wrong-pc","kind","clear","id","a","thread","a-thread","stamp",100,"repliedThrough",100));
  check(find(s,7002,AnswerReceipts.tag("a-thread",8))!=null,"A different PC cannot clear this PC's notification");
  PushNotifications.show(s,MainActivity.obj("pc",s.pin,"kind","clear","id","a","thread","a-thread","stamp",101,"repliedThrough",9));
  check(find(s,7002,AnswerReceipts.tag("a-thread",8))==null&&find(s,7002,AnswerReceipts.tag("b-thread",2))!=null,"PC reply clears old answers through encrypted push path with no live connection");
  PushNotifications.show(s,push(s,"a","a-thread",10).put("openOnly",true));
  check(find(s,7002,AnswerReceipts.tag("a-thread",10)).actions.length==0,"Open-only answers keep routing without adding unsupported reply actions");
  EventStream events=new EventStream(s);
  events.receive(MainActivity.obj("type","eventSnapshot","epoch","new-epoch","sequence",1,"states",new JSONArray().put(MainActivity.obj("type","replyEvent","id","a","title","Tab A","conversation",chat("a-thread",12,"record"),"baselineSequence",10,"repliedThrough",11,"active",false,"stamp",102))));
  check(find(s,7002,AnswerReceipts.tag("a-thread",10))==null&&find(s,7002,AnswerReceipts.tag("a-thread",12))!=null,"Reconnect snapshot clears open-only old answers and preserves the next answer");
  events.receive(MainActivity.obj("type","eventBatch","epoch","new-epoch","events",new JSONArray().put(MainActivity.obj("sequence",2,"value",MainActivity.obj("type","replyEvent","id","a","title","Tab A","conversation",MainActivity.obj("id","a-thread","source","record","messages",new JSONArray()),"repliedThrough",13,"active",false,"stamp",103)))));
  check(find(s,7002,AnswerReceipts.tag("a-thread",12))==null,"Reply-only background event clears answers without waiting for another assistant answer");
  PushNotifications.show(s,push(s,"b","b-thread",4).put("kind","request").put("question","Unanswered request").put("stamp",200));
  AnswerReceipts.replied(s,s.pin,"b-thread",3);
  check(find(s,7004,RequestTracker.digest("b"))!=null,"An ordinary reply never dismisses an unrelated pending request");
  check(find(s,7002,null)==null&&find(s,7003,null)==null,"Removing the final answered tab also removes its answer summary");
 }
}
