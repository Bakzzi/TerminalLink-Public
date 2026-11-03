package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.content.pm.ServiceInfo;
import android.os.*;
import org.json.*;
import java.util.*;

public final class LinkService extends Service {
 static LinkService current;
 final SentEchoes sentMessages=new SentEchoes();final MessageSend messageSend=new MessageSend(this);
 AutoUpdates updates;
 static final String TOGGLE="com.terminallink.remote.TOGGLE";
 static final String CONNECT="com.terminallink.remote.CONNECT",STOP="com.terminallink.remote.STOP";
 static final String CONNECTION_CHANNEL="pc_connection",ANSWER_CHANNEL="codex_answers";
 static final int CONNECTION_ID=7001,ANSWER_ID=7002,SUMMARY_ID=7003,REQUEST_ID=7004,REQUEST_SUMMARY_ID=7005;
 final Handler ui=new Handler(Looper.getMainLooper());final IBinder binder=new LocalBinder();
 static final String REQUEST_CHANNEL="codex_requests";
 NotificationReplies replies;final LinkedHashMap<String,JSONObject> conversations=new LinkedHashMap<>();final HashMap<String,String> requestDetails=new HashMap<>();
 RequestTracker requests;final HashMap<String,Runnable> requestClear=new HashMap<>();
 RequestCompletions requestCompletions=new RequestCompletions("{}");
 final LinkedHashSet<String> watched=new LinkedHashSet<>();
 SecureClient client;SecureClient.Listener observer;MainActivity viewer;android.content.SharedPreferences state,prefs;
 EventStream events;boolean eventMode,pushReady,pushRegistering,parked,parking;Intent pendingReply;Runnable parkTask,replyConnectTimeout;
 ReplyCursor cursor;JSONObject readyMessage,windowsMessage,lastFrame;boolean enabled,ready,supportsWatch;
 String host="",pin="",token="",selectedId="";int port=49443,retries=0;Runnable retry;
 final class LocalBinder extends Binder {LinkService get(){return LinkService.this;}}
 @Override public IBinder onBind(Intent intent){return binder;}
 @Override public void onCreate(){super.onCreate();prefs=getSharedPreferences("connection",MODE_PRIVATE);state=getSharedPreferences("reply-state",MODE_PRIVATE);cursor=new ReplyCursor(state.getString("cursors","{}"));requests=new RequestTracker(state.getString("requests","{}"));selectedId=state.getString("selected","");
  requestCompletions=new RequestCompletions(state.getString("completed-requests","{}"));
  try{JSONArray a=new JSONArray(state.getString("watched","[]"));for(int i=0;i<a.length()&&i<32;i++)watched.add(a.optString(i));}catch(Exception ignored){}
  enabled=state.getBoolean("enabled",false);parked=state.getBoolean("parked",false);events=new EventStream(this);pin=state.getString("pin","");replies=new NotificationReplies(this);updates=new AutoUpdates(this);channels();current=this;ConnectionTile.refresh();
 }
 void channels(){
  NotificationManager nm=getSystemService(NotificationManager.class);
  NotificationChannel connection=new NotificationChannel(CONNECTION_CHANNEL,"PC 연결 유지",NotificationManager.IMPORTANCE_LOW);connection.setDescription("앱을 닫아도 PC 연결과 답변 수신을 유지합니다.");connection.setShowBadge(false);nm.createNotificationChannel(connection);
  NotificationChannel answers=new NotificationChannel(ANSWER_CHANNEL,"Codex 최종 답변",NotificationManager.IMPORTANCE_HIGH);answers.setDescription("진행 메시지를 제외한 새로운 최종 답변");answers.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);nm.createNotificationChannel(answers);
  NotificationChannel request=new NotificationChannel(REQUEST_CHANNEL,"질문·승인 요청",NotificationManager.IMPORTANCE_HIGH);request.setName("질문·승인 요청");request.setDescription("답변이나 선택이 필요한 새로운 요청");request.setLockscreenVisibility(Notification.VISIBILITY_PRIVATE);nm.createNotificationChannel(request);
 }
 int icon(){return getResources().getIdentifier("ic_notification","drawable",getPackageName());}
 PendingIntent open(String id,String key){return open(id,key,pin);}
 PendingIntent open(String id,String key,String certificate){
  Intent i=new Intent(this,MainActivity.class).setAction(Intent.ACTION_VIEW).setData(android.net.Uri.parse("terminallink://answer/"+key)).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP);
  i.putExtra("reply-key",key);i.putExtra("reply-session",id);i.putExtra("reply-pin",certificate);
  return PendingIntent.getActivity(this,0,i,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
 }
 Notification connectionNotification(){
  PendingIntent stop=PendingIntent.getService(this,1,new Intent(this,LinkService.class).setAction(STOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  return new Notification.Builder(this,CONNECTION_CHANNEL).setSmallIcon(icon()).setContentTitle(ready?"TerminalLink · PC 연결 중":"TerminalLink · PC에 다시 연결 중")
   .setContentText("답변과 요청을 알림으로 받아요 · 눌러서 열기").setContentIntent(open(selectedId,"connection")).setOngoing(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_SERVICE)
   .addAction(new Notification.Action.Builder(null,"연결 끊기",stop).build()).build();
 }
 @Override public int onStartCommand(Intent intent,int flags,int startId){
  if(intent!=null&&NotificationReplies.ACTION.equals(intent.getAction())){
   if(ready&&client!=null){replies.handle(intent);return START_STICKY;}
   if(!state.getBoolean("enabled",false)){replies.handle(intent);stopSelf();return START_NOT_STICKY;}
   if(pendingReply!=null){replies.connectionFailed(intent);return START_STICKY;}
   pendingReply=intent;replies.connecting(intent);wake();
   replyConnectTimeout=()->{if(pendingReply!=null){Intent failed=pendingReply;pendingReply=null;replies.connectionFailed(failed);}replyConnectTimeout=null;if((viewer==null||!viewer.resumed)&&state.getBoolean("push-confirmed",false))parkLocally();else schedulePark();};ui.postDelayed(replyConnectTimeout,20000);return START_STICKY;
  }
  if(intent!=null&&STOP.equals(intent.getAction())){disconnect();return START_NOT_STICKY;}
  if(intent!=null&&TOGGLE.equals(intent.getAction())){
   if(enabled||state.getBoolean("enabled",false)){disconnect();return START_NOT_STICKY;}
   intent=ConnectionTile.savedConnection(this);
   if(intent==null){startForeground(CONNECTION_ID,connectionNotification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);disconnect();return START_NOT_STICKY;}
  }
  boolean connecting=intent!=null&&CONNECT.equals(intent.getAction());
  if(!connecting&&!state.getBoolean("enabled",false)){stopSelf();return START_NOT_STICKY;}
  enabled=true;startForeground(CONNECTION_ID,connectionNotification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);
  if(connecting){
   String nextPin=intent.getStringExtra("pin");if(nextPin==null)nextPin="";
   String oldPin=state.getString("pin","");
   if(!oldPin.equals(nextPin)){messageSend.fail("등록된 PC가 바뀌었습니다. 전송 여부를 확인하세요.");sentMessages.clear();state.edit().putBoolean("push-confirmed",false).remove("event-epoch").remove("event-sequence").apply();events=new EventStream(this);watched.clear();conversations.clear();requestDetails.clear();requests=new RequestTracker("{}");requestCompletions=new RequestCompletions("{}");state.edit().remove("completed-requests").apply();getSystemService(NotificationManager.class).cancelAll();cursor=new ReplyCursor("{}");selectedId="";lastFrame=null;}
   host=intent.getStringExtra("host");port=intent.getIntExtra("port",49443);pin=nextPin;token=intent.getStringExtra("token");if(token==null)token="";
   state.edit().putBoolean("enabled",true).putString("host",host).putInt("port",port).putString("pin",pin).apply();save();
  }else{host=state.getString("host","");port=state.getInt("port",49443);pin=state.getString("pin","");token="";}
  connect();return START_STICKY;
 }
 void connect(){
  if(!enabled)return;getSystemService(android.app.job.JobScheduler.class).cancel(PushRegistrationJob.ID);getSharedPreferences("push-state",MODE_PRIVATE).edit().putBoolean("disablePending",false).apply();if(retry!=null){ui.removeCallbacks(retry);retry=null;}if(client!=null)client.close();
  cancelRequestTimers();parked=false;parking=false;pushReady=false;pushRegistering=false;state.edit().putBoolean("parked",false).apply();ready=false;readyMessage=null;windowsMessage=null;lastFrame=null;conversations.clear();
  ConnectionTile.refresh();getSystemService(NotificationManager.class).notify(CONNECTION_ID,connectionNotification());
  if(observer!=null)observer.json(MainActivity.obj("type","connecting"));
  final SecureClient[] current=new SecureClient[1];
  current[0]=new SecureClient(host,port,pin,token,new SecureClient.Listener(){
   public void json(JSONObject j){ui.post(()->{if(client==current[0])receive(j);});}
   public void frame(byte[] bytes){ui.post(()->{if(client==current[0]&&observer!=null)observer.frame(bytes);});}
   public void ended(String reason){ui.post(()->{if(client!=current[0])return;client=null;ready=false;readyMessage=null;ConnectionTile.refresh();cancelRequestTimers();getSystemService(NotificationManager.class).notify(CONNECTION_ID,connectionNotification());
    messageSend.fail("연결이 끊겼습니다. 전송 여부를 확인한 뒤 다시 시도하세요.");updates.disconnected();if(observer!=null)observer.ended(reason);
    if(!token.isEmpty()){disconnect();return;}
    if(enabled){retry=()->{retry=null;connect();};ui.postDelayed(retry,Math.min(30000,2000L*(1L<<Math.min(retries++,4))));}
   });}
  });client=current[0];client.start();
 }
 void receive(JSONObject j){
  String type=j.optString("type");
  if(type.startsWith("update")){updates.message(j);return;}
  if(events!=null&&events.receive(j))return;
  if(type.equals("notificationResult")){requestResult(j);schedulePark();return;}
  if(type.equals("pushStatus")){pushReady=j.optBoolean("ready");if(pushReady){state.edit().putBoolean("push-confirmed",true).apply();schedulePark();}return;}
  if(type.equals("parked")){
   parking=false;if(!j.optBoolean("ok"))return;
   parkLocally();
   if(viewer!=null&&viewer.resumed)wake();return;
  }
  if(type.equals("ready")){
   ready=true;ConnectionTile.refresh();retries=0;token="";readyMessage=j;updates.connected(j.optInt("updates")==1);eventMode=j.optInt("events")==1;viewState();supportsWatch=j.optInt("replyWatch")==1;
   prefs.edit().putString("host",host).putInt("port",port).putString("pin",pin).apply();
   PushClient.register(this);
   replies.recover();
   if(pendingReply!=null){Intent queued=pendingReply;pendingReply=null;if(replyConnectTimeout!=null)ui.removeCallbacks(replyConnectTimeout);replyConnectTimeout=null;replies.handle(queued);}
   schedulePark();
   getSystemService(NotificationManager.class).notify(CONNECTION_ID,connectionNotification());
  }else if(type.equals("windows")){
   JSONArray windows=j.optJSONArray("windows");if(windows==null)return;
   HashSet<String> allowed=new HashSet<>();for(int i=0;i<windows.length();i++)allowed.add(windows.optJSONObject(i).optString("id"));
   String desired=j.optString("selectId",selectedId);if(desired.isEmpty()||desired.equals("null"))desired=selectedId;
   if(!allowed.contains(desired))desired=windows.length()>0?windows.optJSONObject(0).optString("id"):"";
   if(!selectedId.equals(desired))lastFrame=null;selectedId=desired;
   try{j.put("selectId",selectedId);}catch(Exception ignored){}windowsMessage=j;
   requestCompletions.retain(allowed);watched.retainAll(allowed);conversations.keySet().retainAll(allowed);requestDetails.keySet().retainAll(allowed);for(String id:new ArrayList<>(requests.active.keySet()))if(!allowed.contains(id))clearRequest(id);save();
   if(!selectedId.isEmpty())send(MainActivity.obj("op","select","id",selectedId));
   if(supportsWatch){for(String id:new ArrayList<>(watched))send(MainActivity.obj("op","watchReplies","id",id));for(int i=0;i<windows.length()&&i<32;i++)watch(windows.optJSONObject(i).optString("id"));}
   if(eventMode)events.subscribe();
  }else if(type.equals("terminalText")){
   String id=j.optString("id");JSONObject conversation=j.optJSONObject("conversation");
   if(conversation!=null&&conversation.optBoolean("delta"))try{conversation=EventStream.merge(lastFrame==null?null:lastFrame.optJSONObject("conversation"),conversation);j.put("conversation",conversation);}catch(JSONException e){send(MainActivity.obj("op","viewState","visible",false));viewState();return;}
   if(conversation!=null)observe(id,j.optString("title"),conversation);
   watch(id);if(readyMessage==null||readyMessage.optInt("requestWatch")!=1)observeRequest(id,j.optString("title"),j.optString("text"));
   if(id.equals(selectedId)){
    JSONObject cached=j;
    if(!j.has("conversation")&&lastFrame!=null&&lastFrame.has("conversation"))try{cached=new JSONObject();Iterator<String> fields=j.keys();while(fields.hasNext()){String field=fields.next();cached.put(field,j.get(field));}cached.put("conversation",lastFrame.get("conversation"));}catch(Exception ignored){}
    lastFrame=cached;
   }
  }else if(type.equals("replyState")){observe(j.optString("id"),j.optString("title"),j.optJSONObject("conversation"));if(j.has("requestText"))observeRequest(j.optString("id"),j.optString("title"),j.optString("requestText"));return;}
  else if(type.equals("replyWatchClosed")){conversations.remove(j.optString("id"));watched.remove(j.optString("id"));clearRequest(j.optString("id"));save();return;}
  else if(type.equals("closed")){if(lastFrame!=null&&lastFrame.optString("id").equals(j.optString("id")))lastFrame=null;conversations.remove(j.optString("id"));watched.remove(j.optString("id"));clearRequest(j.optString("id"));save();}
  if(observer!=null)observer.json(j);
 }
 void requestResult(JSONObject j){
  if(messageSend.result(j))return;
  RequestCompletions.Target done=requestCompletions.result(j);
  if(done!=null)completeRequest(done.session,done.thread,done.item);
  replies.result(j);if(observer!=null)observer.json(j);
 }
 void watch(String id){
  if(id.isEmpty()||watched.contains(id))return;
  if(watched.size()>=32){String old=watched.iterator().next();watched.remove(old);if(supportsWatch)send(MainActivity.obj("op","unwatchReplies","id",old));}
  watched.add(id);save();if(supportsWatch)send(MainActivity.obj("op","watchReplies","id",id));
 }
 void observe(String id,String title,JSONObject conversation){
  conversation=requestCompletions.filter(id,conversation);
  if(!ConversationStatus.available(conversation)){conversations.remove(id);clearRequest(id);return;}
  AnswerReceipts.observe(this,pin,conversation);
  UnreadAnswers.observe(this,pin,id,conversation);
  // Background notifications need request routing, never a second copy of every tab's transcript.
  if(conversation!=null){
   conversations.remove(id);
   JSONObject metadata=MainActivity.obj("id",conversation.optString("id"),"readOnly",conversation.optBoolean("readOnly"),"source",conversation.optString("source"),"readState",conversation.optString("readState","ready"));
   JSONArray pending=conversation.optJSONArray("requests");if(pending!=null)try{metadata.put("requests",pending);}catch(Exception ignored){}
   conversations.put(id,metadata);
   while(conversations.size()>64)conversations.remove(conversations.keySet().iterator().next());
  }
  java.util.List<JSONObject> answers=cursor.read(conversation);
  for(JSONObject answer:answers)postAnswer(id,title,conversation.optString("id"),answer);
  save();
 }
 void postAnswer(String id,String title,String thread,JSONObject answer){
  synchronized(AnswerReceipts.class){
  if(viewer!=null&&viewer.viewingSession(id))return;
  if(!prefs.getBoolean("answer-notifications",true)||!getSystemService(NotificationManager.class).areNotificationsEnabled())return;
  String text=answer.optString("text");if(text.length()>4000)text=text.substring(0,4000)+"…";
  String key;try{key=SecureClient.hex(java.security.MessageDigest.getInstance("SHA-256").digest((thread+":"+answer.optLong("sequence")).getBytes(java.nio.charset.StandardCharsets.UTF_8)));}catch(Exception e){return;}
  if(AnswerReceipts.contains(this,pin,thread,answer.optLong("sequence")))return;
  Notification publicNotice=new Notification.Builder(this,ANSWER_CHANNEL).setSmallIcon(icon()).setContentTitle("Codex 답변 도착").setContentText("TerminalLink에서 답변을 확인하세요.").build();
  Notification.Builder b=new Notification.Builder(this,ANSWER_CHANNEL).setSmallIcon(icon()).setContentTitle(NotificationTitles.answer(title)).setContentText(text)
   .setGroup("codex_replies").setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN).setStyle(new Notification.BigTextStyle().bigText(text)).setContentIntent(open(id,key)).setAutoCancel(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_MESSAGE)
   .setVisibility(Notification.VISIBILITY_PRIVATE).setPublicVersion(publicNotice);
  JSONObject route=MainActivity.obj("kind","answer","id",id,"thread",thread,"sequence",answer.optLong("sequence"));
  if(NotificationReplyAccess.allowed(conversations.get(id),readyMessage))replies.decorate(b,route);
  else NotificationReplies.decorate(this,pin,false,b,route);
  Notification posted=b.build();getSystemService(NotificationManager.class).notify(key,ANSWER_ID,posted);updateSummaryFor(ANSWER_ID,key,posted);
  }
 }
 void readAnswers(String id,String thread,Set<Long> sequences){
  if(sequences.isEmpty()||thread.isEmpty())return;
  HashSet<String> keys=new HashSet<>();for(long sequence:sequences)keys.add(AnswerReceipts.tag(thread,sequence));
  boolean changed=AnswerReceipts.mark(this,pin,keys);
  NotificationManager nm=getSystemService(NotificationManager.class);
  for(android.service.notification.StatusBarNotification n:nm.getActiveNotifications())if(n.getId()==ANSWER_ID&&keys.contains(n.getTag())&&!"sending".equals(n.getNotification().extras.getString("tl-state"))){nm.cancel(n.getTag(),ANSWER_ID);changed=true;}
  if(changed)updateSummary();
 }
 void acknowledge(String key){AnswerReceipts.mark(this,pin,Collections.singleton(key));getSystemService(NotificationManager.class).cancel(key,ANSWER_ID);updateSummary();}

 void updateSummary(){updateSummary(ANSWER_ID,SUMMARY_ID,ANSWER_CHANNEL,"codex_replies","Codex 답변 도착");}
 void updateSummaryFor(int id,String tag,Notification posted){
  boolean answer=id==ANSWER_ID;
  updateSummary(id,answer?SUMMARY_ID:REQUEST_SUMMARY_ID,answer?ANSWER_CHANNEL:REQUEST_CHANNEL,answer?"codex_replies":"codex_requests",answer?"Codex 답변 도착":"Codex 응답 요청",null,tag,posted);
 }
 void updateSummary(int itemId,int summaryId,String channel,String group,String title){updateSummary(itemId,summaryId,channel,group,title,null);}
 void updateSummary(int itemId,int summaryId,String channel,String group,String title,String dismissedNonce){updateSummary(itemId,summaryId,channel,group,title,dismissedNonce,null,null);}
 void updateSummary(int itemId,int summaryId,String channel,String group,String title,String dismissedNonce,String incomingTag,Notification incoming){
  NotificationSummary.update(this,itemId,summaryId,channel,group,title,dismissedNonce,incomingTag,incoming);
 }

 String requestIdentity(String id,RequestPrompt prompt){
  if(prompt==null||!prompt.followup)return "";
  JSONObject c=conversations.get(id);JSONArray queued=c==null?null:c.optJSONArray("requests");
  if(queued!=null)for(int i=0;i<queued.length();i++){JSONObject q=queued.optJSONObject(i);if(q!=null&&q.optString("title").replaceAll("\\s+","").equals(prompt.question.replaceAll("\\s+","")))return q.optString("id");}
  return "";
 }
 void observeRequest(String id,String title,String text){
  if(id.isEmpty())return;RequestPrompt prompt=RequestPrompt.parse(text);
  JSONObject known=conversations.get(id);JSONArray original=known==null?null:known.optJSONArray("requests");
  if(ConversationStatus.available(known)&&original!=null&&original.length()==0&&(prompt==null||prompt.followup)){clearRequest(id);return;}
  if(prompt!=null&&prompt.followup&&original!=null&&original.length()>0){
   boolean found=false;for(int i=0;i<original.length();i++){JSONObject q=original.optJSONObject(i);if(q!=null&&q.optString("title").replaceAll("\\s+","").equals(prompt.question.replaceAll("\\s+","")))found=true;}
   if(!found){RecordRequest first=RecordRequest.first(known);if(first!=null)prompt=first.prompt(0);}
  }
  // A blank paragraph typed into a freeform composer is not a new question.
  if(prompt!=null&&prompt.followup&&prompt.choices.isEmpty()){
   JSONObject c=conversations.get(id);JSONArray pending=c==null?null:c.optJSONArray("requests");
   if(pending!=null)for(int i=0;i<pending.length();i++){JSONObject q=pending.optJSONObject(i);if(q==null)continue;
    RequestPrompt stable=RequestPrompt.parseFollowup(text,q.optString("title"));
    if(stable!=null&&stable.question.replaceAll("\\s+","").equals(q.optString("title").replaceAll("\\s+",""))){prompt=stable;break;}
   }
  }
  if(prompt==null&&RequestPrompt.collapsed(text)){
   JSONObject conversation=conversations.get(id);JSONArray queued=conversation==null?null:conversation.optJSONArray("requests");
   // An explicit empty list retires cached follow-up alerts, even if the terminal
   // still shows a collapsed queue while its next frame is being redrawn.
   if(queued!=null&&queued.length()==0){clearRequest(id);return;}
   if(queued!=null&&queued.length()>0){JSONObject q=queued.optJSONObject(0);if(q!=null){ArrayList<String> choices=new ArrayList<>();JSONArray options=q.optJSONArray("options");if(options!=null)for(int i=0;i<options.length();i++)choices.add(options.optString(i));if(!choices.isEmpty())choices.add("직접 입력");prompt=new RequestPrompt("추가 질문",q.optString("title"),choices,choices.isEmpty()?-1:0,choices.isEmpty(),true,"",true);}}
  }
  if(prompt!=null||RequestPrompt.collapsed(text)||RequestPrompt.requiresAction(title)){
   Runnable old=requestClear.remove(id);if(old!=null)ui.removeCallbacks(old);
   boolean changed=requests.note(id,prompt);requestDetails.keySet().retainAll(requests.active.keySet());
   String detail=prompt==null?"?":prompt.key+"|"+requestIdentity(id,prompt),oldDetail=requestDetails.get(id);
   boolean newIdentity=prompt!=null&&oldDetail!=null&&!oldDetail.equals("?")&&!detail.equals(oldDetail);
   if(prompt!=null||oldDetail==null)requestDetails.put(id,detail);
   if(changed||newIdentity||prompt!=null&&"?".equals(oldDetail))postRequest(id,prompt,!changed&&(!newIdentity||oldDetail.endsWith("|")));
   state.edit().putString("requests",requests.save()).apply();
  }else if(requests.contains(id)&&!requestClear.containsKey(id)){
   Runnable clear=()->{requestClear.remove(id);clearRequest(id);};requestClear.put(id,clear);ui.postDelayed(clear,1800);
  }
 }
 void cancelRequestTimers(){for(Runnable r:requestClear.values())ui.removeCallbacks(r);requestClear.clear();}
 void clearRequest(String id){Runnable r=requestClear.remove(id);if(r!=null)ui.removeCallbacks(r);requests.remove(id);requestDetails.remove(id);acknowledgeRequest(id);state.edit().putString("requests",requests.save()).apply();}
 void acknowledgeRequest(String id){String tag=RequestTracker.digest(id);getSystemService(NotificationManager.class).cancel(tag,REQUEST_ID);updateSummary(REQUEST_ID,REQUEST_SUMMARY_ID,REQUEST_CHANNEL,"codex_requests","Codex \uc751\ub2f5 \uc694\uccad",null,tag,null);}
 void completeRequest(String id,String thread,String item){
  requestCompletions.complete(id,thread,item);
  state.edit().putString("completed-requests",requestCompletions.save()).apply();
  JSONObject cached=conversations.get(id);if(cached!=null){cached=requestCompletions.filter(id,cached);conversations.put(id,cached);}
  if(lastFrame!=null&&id.equals(lastFrame.optString("id")))try{JSONObject c=lastFrame.optJSONObject("conversation");if(c!=null)lastFrame.put("conversation",requestCompletions.filter(id,c));}catch(JSONException ignored){}
  JSONArray left=cached==null?null:cached.optJSONArray("requests");
  if(left==null||left.length()==0)clearRequest(id);
 }
 void postRequest(String id,RequestPrompt prompt,boolean quiet){
  if(viewer!=null&&viewer.viewingSession(id))return;
  NotificationManager nm=getSystemService(NotificationManager.class);
  if(!prefs.getBoolean("request-notifications",true)||!nm.areNotificationsEnabled())return;
  String key=RequestTracker.digest(id),text=prompt==null?"PC\uc5d0\uc11c \ub2f5\ubcc0\uc774\ub098 \uc120\ud0dd\uc744 \uae30\ub2e4\ub9ac\uace0 \uc788\uc5b4\uc694.":prompt.question;
  if(text.length()>2000)text=text.substring(0,2000)+"\u2026";
  Intent i=new Intent(this,MainActivity.class).setAction(Intent.ACTION_VIEW).setData(android.net.Uri.parse("terminallink://request/"+key)).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP);
  i.putExtra("reply-session",id).putExtra("reply-pin",pin).putExtra("request-view",true);
  PendingIntent open=PendingIntent.getActivity(this,0,i,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  Notification publicNotice=new Notification.Builder(this,REQUEST_CHANNEL).setSmallIcon(icon()).setContentTitle("Codex \uc751\ub2f5 \uc694\uccad").setContentText("TerminalLink\uc5d0\uc11c \uc694\uccad\uc744 \ud655\uc778\ud558\uc138\uc694.").build();
  Notification.Builder b=new Notification.Builder(this,REQUEST_CHANNEL).setSmallIcon(icon()).setContentTitle("Codex \uc751\ub2f5 \uc694\uccad").setContentText(text)
   .setStyle(new Notification.BigTextStyle().bigText(text)).setContentIntent(open).setAutoCancel(true).setCategory(Notification.CATEGORY_MESSAGE)
   .setGroup("codex_requests").setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN).setOnlyAlertOnce(quiet).setVisibility(Notification.VISIBILITY_PRIVATE).setPublicVersion(publicNotice);
  JSONObject conversation=conversations.get(id);
  if(prompt!=null&&NotificationReplyAccess.allowed(conversation,readyMessage))replies.decorate(b,MainActivity.obj("kind","request","id",id,"thread",conversation.optString("id"),"question",prompt.question,"requestKey",requestIdentity(id,prompt),"choices",new JSONArray(prompt.choices),"followup",prompt.followup,"questionKind",prompt.questionKind));
  Notification posted=b.build();nm.notify(key,REQUEST_ID,posted);updateSummaryFor(REQUEST_ID,key,posted);
 }
 void save(){state.edit().putString("completed-requests",requestCompletions.save()).putString("requests",requests.save()).putString("cursors",cursor.save()).putString("watched",new JSONArray(watched).toString()).putString("selected",selectedId).apply();}
 void parkLocally(){
  if(retry!=null){ui.removeCallbacks(retry);retry=null;}if(parkTask!=null){ui.removeCallbacks(parkTask);parkTask=null;}
  parked=true;parking=false;ready=false;SecureClient closing=client;client=null;if(closing!=null)closing.close();lastFrame=null;conversations.clear();
  state.edit().putBoolean("parked",true).apply();stopForeground(STOP_FOREGROUND_REMOVE);ConnectionTile.refresh();stopSelf();
 }
 void wake(){
  if(!state.getBoolean("enabled",false)||client!=null)return;
  enabled=true;host=state.getString("host","");port=state.getInt("port",49443);pin=state.getString("pin","");token="";
  startForeground(CONNECTION_ID,connectionNotification(),ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE);connect();
 }
 boolean transferring(){return messageSend.busy||updates!=null&&updates.busy()||pendingReply!=null||!replies.deadlines.isEmpty()||!replies.dismissals.isEmpty()||viewer!=null&&(viewer.attachments!=null&&viewer.attachments.busy||viewer.actions!=null&&viewer.actions.submitting||viewer.remoteFiles!=null&&viewer.remoteFiles.busy());}
 void schedulePark(){
  if(parkTask!=null){ui.removeCallbacks(parkTask);parkTask=null;}
  if(!enabled||!ready||!pushReady||parking||viewer!=null&&viewer.resumed)return;
  parkTask=()->{parkTask=null;if(!enabled||!ready||!pushReady||viewer!=null&&viewer.resumed)return;if(transferring()){schedulePark();return;}parking=true;send(MainActivity.obj("op","park","epoch",events.epoch,"after",events.sequence));};ui.postDelayed(parkTask,1500);
 }
 void viewState(){if(viewer!=null&&viewer.resumed)wake();if(eventMode&&ready)send(MainActivity.obj("op","viewState","visible",viewer!=null&&viewer.resumed));if(updates!=null)updates.tick();schedulePark();}
 void attach(SecureClient.Listener listener,MainActivity activity){observer=listener;viewer=activity;viewState();if(readyMessage!=null)listener.json(readyMessage);if(windowsMessage!=null)listener.json(windowsMessage);if(lastFrame!=null)listener.json(lastFrame);activity.updates.render();}
 void detach(SecureClient.Listener listener){if(observer==listener){observer=null;viewer=null;viewState();}}
 void send(JSONObject j){
  if(!ready||client==null)return;
  if(j.optString("op").equals("select")){
   String id=j.optString("id");if(!id.equals(selectedId)){selectedId=id;lastFrame=null;try{if(windowsMessage!=null)windowsMessage.put("selectId",id);}catch(Exception ignored){}save();}
  }
  requestCompletions.track(j);client.send(j);
 }
 void disconnect(){
  messageSend.fail("연결이 종료되었습니다. 전송 여부를 확인한 뒤 다시 시도하세요.");sentMessages.clear();
  updates.disconnected();
  if(parkTask!=null){ui.removeCallbacks(parkTask);parkTask=null;}if(replyConnectTimeout!=null){ui.removeCallbacks(replyConnectTimeout);replyConnectTimeout=null;}pendingReply=null;
  parked=false;parking=false;pushReady=false;state.edit().putBoolean("parked",false).apply();
  getSystemService(android.app.job.JobScheduler.class).cancel(PushRegistrationJob.ID);
  if(client!=null&&ready&&eventMode){SecureClient closing=client;client=null;closing.sendAndClose(MainActivity.obj("op","pushDisable"));}
  cancelRequestTimers();boolean wasConnected=enabled||client!=null;enabled=false;ready=false;state.edit().putBoolean("enabled",false).putBoolean("push-confirmed",false).apply();if(eventMode){getSharedPreferences("push-state",MODE_PRIVATE).edit().putBoolean("disablePending",true).apply();PushRegistrationJob.schedule(this);}if(retry!=null){ui.removeCallbacks(retry);retry=null;}
  if(client!=null){client.close();client=null;}readyMessage=null;windowsMessage=null;lastFrame=null;conversations.clear();watched.clear();save();
  stopForeground(STOP_FOREGROUND_REMOVE);ConnectionTile.refresh();if(wasConnected&&observer!=null)observer.ended("연결을 끊었습니다.");stopSelf();
 }
 @Override public void onDestroy(){if(updates!=null)updates.close();messageSend.fail("연결이 종료되었습니다. 전송 여부를 확인하세요.");if(parkTask!=null)ui.removeCallbacks(parkTask);if(replyConnectTimeout!=null)ui.removeCallbacks(replyConnectTimeout);replies.close();cancelRequestTimers();if(retry!=null)ui.removeCallbacks(retry);SecureClient closing=client;client=null;if(closing!=null)closing.close();enabled=false;ready=false;if(current==this)current=null;ConnectionTile.refresh();observer=null;viewer=null;readyMessage=null;windowsMessage=null;lastFrame=null;conversations.clear();requestDetails.clear();ui.removeCallbacksAndMessages(null);super.onDestroy();}
}
