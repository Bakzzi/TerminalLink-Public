package com.terminallink.remote;
import org.json.*;import android.app.*;import java.util.*;
public class RecordNotificationJvm {
 static void check(boolean ok,String name){if(!ok)throw new AssertionError(name);System.out.println("PASS "+name);}
 static Notification note(LinkService s,String thread,long n){for(android.service.notification.StatusBarNotification item:s.nm.getActiveNotifications())if(item.getId()==7002&&item.getTag().equals(AnswerReceipts.tag(thread,n)))return item.getNotification();return null;}
 static JSONObject chat(String id,boolean ro,long n){return MainActivity.obj("id",id,"source",ro?"remote":"record","readOnly",ro,"messages",new JSONArray().put(MainActivity.obj("role","assistant","phase","final","text","answer "+n,"sequence",n)));}
 public static void main(String[] args)throws Exception {
  for(boolean ro:new boolean[]{false,true}){LinkService s=new LinkService();String id=ro?"remote-tab":"local-tab",thread=id+"-thread";s.observe(id,id,chat(thread,ro,1));check(s.nm.posts==0,"Existing history does not notify "+id);s.observe(id,id,chat(thread,ro,2));Notification n=note(s,thread,2);check(n!=null&&n.reply==!ro,"New answer notifies with correct input authority "+id);s.observe(id,id,chat(thread,ro,2));check(s.nm.posts==1,"Repeated snapshot does not notify again "+id);s.readAnswers(id,thread,new HashSet<>(Arrays.asList(2L)));check(note(s,thread,2)==null,"Reading removes matching notification "+id);s.observe(id,id,chat(thread,ro,1));s.observe(id,id,chat(thread,ro,2));check(s.nm.posts==1,"Out-of-order history does not revive read notification "+id);}
 }
}
