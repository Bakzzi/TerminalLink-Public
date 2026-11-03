package com.terminallink.remote;
import android.app.*;import android.service.notification.*;import org.json.*;import java.util.*;import java.nio.file.*;
public class NotificationJvm {
 static void check(boolean b,String s){if(!b)throw new AssertionError(s);System.out.println("PASS "+s);}
 static JSONObject message(long n){return MainActivity.obj("role","assistant","phase","final","text","answer "+n,"sequence",n);}
 static JSONObject chat(String id,String source,boolean readOnly,long... seq){JSONArray m=new JSONArray();for(long n:seq)m.put(message(n));return MainActivity.obj("id",id,"source",source,"readOnly",readOnly,"messages",m);}
 static Notification note(LinkService l,String id,long seq){String tag=AnswerReceipts.tag(id,seq);for(StatusBarNotification n:l.nm.getActiveNotifications())if(n.getTag().equals(tag))return n.getNotification();return null;}
 public static void main(String[] args)throws Exception{
  JSONArray corpus=new JSONArray(Files.readString(Path.of(args[0])));
  for(int i=0;i<corpus.length();i++){
   LinkService l=new LinkService();LinkService.current=l;l.state.edit().putBoolean("enabled",true).putString("pin",l.pin).commit();
   JSONObject row=corpus.getJSONObject(i),event=row.getJSONObject("notice"),c=event.getJSONObject("conversation");String id=event.getString("id"),thread=c.getString("id");boolean ro=c.optBoolean("readOnly");
   EventStream stream=new EventStream(l);stream.apply(event);
   check(note(l,thread,4)!=null,id+" PC event produces Android notification");check(note(l,thread,4).reply!=ro,id+" event inline reply respects capability");int count=l.nm.posts;stream.apply(event);check(l.nm.posts==count,id+" direct replay suppressed");
   JSONArray pushes=row.getJSONArray("push");for(int j=0;j<pushes.length();j++)PushNotifications.show(l,pushes.getJSONObject(j));check(l.nm.posts==count,id+" FCM after direct delivery deduplicated");
   check(NotificationRecord.readable(c),id+" viewer can acknowledge read-only answer");l.readAnswers(id,thread,new HashSet<>(Arrays.asList(4L)));check(note(l,thread,4)==null,id+" reading clears matching notification");
   l.cursor=new ReplyCursor("{}");PushNotifications.show(l,pushes.getJSONObject(0));check(note(l,thread,4)==null,id+" read receipt survives new service cursor");
   JSONObject latest=chat(thread,c.optString("source"),ro,4,5);l.observe(id,id,latest);check(note(l,thread,5)!=null,id+" next answer after reading still notifies");
   l.observe(id,id,chat(thread,"cached",true,4,5,6));check(note(l,thread,6)==null,id+" cached content never alerts");l.observe(id,id,chat(thread,c.optString("source"),ro,4,5,6));check(note(l,thread,6)!=null,id+" confirmed content after cache alerts");
   l.viewer=new MainActivity();l.viewer.viewing=true;l.viewer.selected=id;l.observe(id,id,chat(thread,c.optString("source"),ro,7));check(note(l,thread,7)==null,id+" visible conversation suppresses direct alert");
   l.observe("other-tab","other",chat("other-"+thread,"record",ro,1));l.observe("other-tab","other",chat("other-"+thread,"record",ro,2));check(note(l,"other-"+thread,2)!=null,id+" other tab still alerts while viewer active");
   l.viewer=null;l.observe(id,id,chat(thread,c.optString("source"),ro,8));check(note(l,thread,8)!=null,id+" background resumes notifications");
   l.readAnswers(id,thread,new HashSet<>(Arrays.asList(8L)));check(note(l,"other-"+thread,2)!=null,id+" reading selected tab preserves other notification");
   LinkService p=new LinkService();LinkService.current=p;p.state.edit().putBoolean("enabled",true).putString("pin",p.pin).commit();PushNotifications.show(p,pushes.getJSONObject(0));check(note(p,thread,4)!=null&&note(p,thread,4).reply!=ro,id+" FCM-only background path and reply capability");
   JSONObject prompt=MainActivity.obj("question","Approve fixture?","choices",new JSONArray().put("Yes").put("No"),"selected",0,"questionKind",false,"followup",false,"notes",false);
   JSONObject request=new JSONObject(event.toString()).put("conversation",chat(thread,c.optString("source"),ro,8)).put("prompt",prompt).put("active",true).put("stamp",100);
   EventStream requests=new EventStream(p);requests.apply(request);boolean found=false;for(StatusBarNotification n:p.nm.getActiveNotifications())if(n.getId()==LinkService.REQUEST_ID){found=true;check(n.getNotification().reply!=ro,id+" request capability is separate from visibility");}check(found,id+" request appears on read-only records");
   requests.apply(new JSONObject(request.toString()).put("active",false).put("stamp",101));for(StatusBarNotification n:p.nm.getActiveNotifications())check(n.getId()!=LinkService.REQUEST_ID,id+" completed request cleared");
   int before=p.nm.posts;requests.apply(request);check(p.nm.posts==before,id+" stale request cannot reignite");
  }
  LinkService l=new LinkService();l.observe("raw","raw",chat("screen:x","terminal",false,1));l.observe("raw","raw",chat("screen:x","terminal",false,2));check(l.nm.posts==0,"Raw terminal fragments never generate final alerts");
 }
}
