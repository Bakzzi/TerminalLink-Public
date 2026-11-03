package com.terminallink.remote;
import org.json.*;
public class MessageSendJvm {
 static void check(boolean b,String s){NotificationReplyJvm.check(b,s);}
 static JSONObject user(String text,long seq){return MainActivity.obj("role","user","text",text,"sequence",seq);}
 static void run()throws Exception{
  SentEchoes echoes=new SentEchoes();JSONArray original=new JSONArray().put(user("Previous",1));
  echoes.add("tab","thread",1,"Repeated",new String[0]);echoes.add("tab","thread",1,"Repeated",new String[0]);
  check(echoes.project("tab","thread",original).length()==3&&original.length()==1,"Confirmed sends display immediately without mutating original records");
  original.put(user("Repeated",2));check(echoes.project("tab","thread",original).length()==3,"One record consumes only one identical send");
  check(echoes.project("tab","thread",original).length()==3,"Replayed frame cannot consume a second identical send");
  original.put(user("Repeated",3));check(echoes.project("tab","thread",original)==original,"Arriving originals remove echoes with no duplicate messages");
  echoes.add("tab","thread",1,"Repeated",new String[0]);check(echoes.project("tab","thread",original).length()==4,"Already matched originals cannot eat a later send");
  check(echoes.project("other","thread",original)==original,"Switching tabs does not show another tab's send");
  check(echoes.project("tab","new-thread",new JSONArray()).length()==0,"Changing conversation clears only the old conversation echo");
  SentEchoes early=new SentEchoes();early.add("tab","thread",1,"Repeated",new String[0]);check(early.project("tab","thread",original)==original,"Original arriving before acknowledgement does not duplicate");
  LinkService s=new LinkService();s.viewer=new MainActivity();s.readyMessage.put("messageSubmit",1);MessageSend send=s.messageSend;
  check(send.start("tab","thread","Question",1)&&send.busy&&s.sent.size()==1,"Send enters pending state and uses acknowledged message protocol");
  check(!send.start("tab","thread","Duplicate",1)&&s.sent.size()==1,"Pending submit cannot send twice");
  String request=send.request;check(!send.result(MainActivity.obj("request","wrong","ok",true))&&send.busy,"Other request acknowledgements cannot unlock input");
  send.result(MainActivity.obj("request",request,"ok",false,"message","Synthetic failure"));check(!send.busy&&send.text.equals("Question")&&!send.error.isEmpty()&&s.sentMessages.project("tab","thread",new JSONArray()).length()==0,"Failure unlocks with draft retained and no false message");
  send.start("tab","thread","Edited question",1);request=send.request;send.result(MainActivity.obj("request",request,"ok",true));
  check(!send.busy&&s.viewer.confirmed==1&&s.sentMessages.project("tab","thread",new JSONArray()).length()==1,"Success unlocks and projects the confirmed message immediately");
  send.start("tab","thread","Unknown result",1);send.timeout.run();check(!send.busy&&send.text.equals("Unknown result")&&s.sent.size()==3,"Timeout unlocks the retained text without automatic resend");
  send.start("tab","thread","Disconnect",1);send.fail("Disconnected");check(!send.busy&&send.text.equals("Disconnect"),"Disconnect releases pending editor state without losing its text");
  JSONObject start=MainActivity.obj("id","start:console/native","source","start","readState","empty","messages",new JSONArray());
  check(ConversationStatus.starting(start)&&!ConversationStatus.available(start),"Fresh Codex is displayable but is never an original conversation");
  check(ConversationStatus.starting(new JSONObject(start.toString()).put("readState","pending")),"Waiting for the first original has a distinct state");
  check(!ConversationStatus.starting(new JSONObject(start.toString()).put("source","unavailable"))&&!ConversationStatus.starting(new JSONObject(start.toString()).put("messages",new JSONArray().put(user("old",1)))),"An unreadable or existing conversation cannot impersonate a fresh start");
  check(!send.startNew("fresh","start:console/native","First")&&!send.busy,"Older connectors cannot receive an unsupported first-message command");
  s.readyMessage.put("startMessage",1);int before=s.sent.size();
  check(send.startNew("fresh","start:console/native","First")&&send.busy&&s.sent.size()==before+1,"First message enters the same input-lock lifecycle");
  JSONObject command=s.sent.get(s.sent.size()-1);
  check(command.optString("kind").equals("startMessage")&&command.optString("identity").equals("console/native")&&command.optString("id").equals("fresh"),"First message carries the selected native identity, not a fabricated original thread");
  check(!send.startNew("fresh","start:console/native","Duplicate")&&s.sent.size()==before+1,"Pending first message cannot submit twice");
  send.result(MainActivity.obj("request",send.request,"ok",true));
  check(!send.busy&&s.sentMessages.project("fresh","start:console/native",new JSONArray()).length()==1,"Acknowledged first message displays immediately");
  JSONArray arrived=new JSONArray().put(user("First",1));
  check(s.sentMessages.project("fresh","actual-original",arrived)==arrived,"The real first message replaces its startup receipt without duplication");
  s.readyMessage.put("nativeMessageSubmit",1);
  check(send.nativeSubmit(),"Connector advertises selected-console ordinary sends independently of history");
  check(send.start("selected-tab","","No readable original",0),"An empty original identity does not block an ordinary native send");
  command=s.sent.get(s.sent.size()-1);
  check(command.optString("kind").equals("message")&&command.optString("id").equals("selected-tab")&&command.optString("thread").isEmpty(),"Unreadable history still sends to the explicit selected tab, without inventing a thread");
  send.result(MainActivity.obj("request",send.request,"ok",false,"message","native write failed"));
  check(!send.busy&&send.text.equals("No readable original"),"Native write failure retains the draft and releases input");
  check(send.startNew("selected-tab","start:console/native","First with native submit"),"New native composer uses the same send path");
  command=s.sent.get(s.sent.size()-1);
  check(command.optString("kind").equals("message")&&command.optString("identity").isEmpty(),"New native send has no original record or welcome-screen gate");
  send.result(MainActivity.obj("request",send.request,"ok",true));
 }
}
