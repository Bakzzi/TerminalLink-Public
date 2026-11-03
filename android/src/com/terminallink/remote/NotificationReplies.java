package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.os.*;
import android.service.notification.StatusBarNotification;
import org.json.*;
import java.util.*;

final class NotificationReplies {
 static final String ACTION="com.terminallink.remote.REPLY",INPUT="message";
 final LinkService service;final HashMap<String,Runnable> deadlines=new HashMap<>(),dismissals=new HashMap<>();
 final android.content.SharedPreferences records;
 NotificationReplies(LinkService s){service=s;records=s.getSharedPreferences("notification-actions",Context.MODE_PRIVATE);recoverPending();}
 void recoverPending(){
  // An acknowledged reply may have outlived the short SystemUI dismissal callback.
  for(StatusBarNotification n:service.getSystemService(NotificationManager.class).getActiveNotifications())if(n.getId()==LinkService.ANSWER_ID&&!n.getNotification().extras.getBoolean("tl-stale-reply")){
   try{JSONObject saved=new JSONObject(records.getString(n.getNotification().extras.getString("tl-nonce",""),"{}")),result=saved.optJSONObject("result");if(result!=null&&result.optBoolean("ok"))AnswerReceipts.mark(service,saved.optString("pcCertificate"),Collections.singleton(n.getTag()));}catch(Exception ignored){}
   if(AnswerReceipts.dismissed(service,service.pin,n.getNotification(),n.getTag()))service.getSystemService(NotificationManager.class).cancel(n.getTag(),n.getId());
  }
  summaries();
  // The previous process may have died before its timeout/acknowledgement callback.
  for(StatusBarNotification n:service.getSystemService(NotificationManager.class).getActiveNotifications())
   if((n.getId()==LinkService.ANSWER_ID||n.getId()==LinkService.REQUEST_ID)&&"sending".equals(n.getNotification().extras.getString("tl-state")))
    status(n,"연결이 다시 시작돼 전송 결과를 확인하지 못했습니다. 대화에서 확인하세요.",n.getNotification().extras.getString("tl-draft",""),false,false);
 }
 JSONObject context(Notification n){try{return new JSONObject(records.getString(n.extras.getString("tl-nonce",""),"{}")).optJSONObject("context");}catch(Exception e){return null;}}
 static Notification.Action action(Context service,String nonce,String label,int choice,boolean input,CharSequence[] choices){
  Intent intent=new Intent(service,LinkService.class).setAction(ACTION).setData(android.net.Uri.parse("terminallink://reply/"+nonce+"/"+choice));
  intent.putExtra("nonce",nonce).putExtra("choice",choice);
  PendingIntent pending=PendingIntent.getService(service,0,intent,PendingIntent.FLAG_UPDATE_CURRENT|(input?PendingIntent.FLAG_MUTABLE:PendingIntent.FLAG_IMMUTABLE));
  Notification.Action.Builder b=new Notification.Action.Builder(null,label,pending).setSemanticAction(Notification.Action.SEMANTIC_ACTION_REPLY);
  if(Build.VERSION.SDK_INT>=31)b.setAuthenticationRequired(true);
  if(input)b.addRemoteInput(new RemoteInput.Builder(INPUT).setLabel("답장 입력").setAllowFreeFormInput(true).setChoices(choices).build());
  return b.build();
 }
 void decorate(Notification.Builder builder,JSONObject context){try{context.put("nativeReply",service.readyMessage!=null&&service.readyMessage.optInt("nativeNotificationReply")==1);}catch(Exception ignored){}decorate(service,service.pin,service.readyMessage!=null&&service.readyMessage.optInt("notificationReply")==1,builder,context);}
 static void decorate(Context service,String pin,boolean supported,Notification.Builder builder,JSONObject context){
  android.content.SharedPreferences records=service.getSharedPreferences("notification-actions",Context.MODE_PRIVATE);
  String nonce=UUID.randomUUID().toString();
  // The PC certificate fingerprint and routing context stay in app-private storage.
  if(records.getAll().size()>256){HashSet<String> active=new HashSet<>();for(StatusBarNotification n:service.getSystemService(NotificationManager.class).getActiveNotifications())active.add(n.getNotification().extras.getString("tl-nonce",""));android.content.SharedPreferences.Editor edit=records.edit();for(String key:records.getAll().keySet())if(!active.contains(key))edit.remove(key);edit.commit();}
  records.edit().putString(nonce,MainActivity.obj("pcCertificate",pin,"context",context).toString()).commit();
  Bundle extras=new Bundle();extras.putString("tl-nonce",nonce);extras.putString("tl-state","new");builder.addExtras(extras);
  if(!supported)return;
  if(context.optString("kind").equals("answer"))builder.addAction(action(service,nonce,"답장",-1,true,null));
  else{
   JSONArray choices=context.optJSONArray("choices");boolean free=context.optBoolean("questionKind");int count=choices==null?0:choices.length();
   if(context.optBoolean("followup")&&count>0)count--;
   int limit=free?2:3;
   for(int i=0;i<Math.min(count,limit);i++){String label=choices.optString(i);if(label.length()>32)label=label.substring(0,31)+"…";builder.addAction(action(service,nonce,label,i,false,null));}
   if(free){CharSequence[] suggested=new CharSequence[count];for(int i=0;i<count;i++)suggested[i]=choices.optString(i);builder.addAction(action(service,nonce,"직접 답장",-1,true,suggested));}
  }
 }
 StatusBarNotification find(String nonce){
  if(nonce==null)return null;
  for(StatusBarNotification n:service.getSystemService(NotificationManager.class).getActiveNotifications())if((n.getId()==LinkService.ANSWER_ID||n.getId()==LinkService.REQUEST_ID)&&nonce.equals(n.getNotification().extras.getString("tl-nonce")))return n;
  return null;
 }
 void connecting(Intent intent){
  StatusBarNotification item=find(intent.getStringExtra("nonce"));if(item==null)return;
  Notification n=item.getNotification();Bundle extras=new Bundle(n.extras);extras.putBoolean("android.remoteInputSpinner",true);
  Notification posted=Notification.Builder.recoverBuilder(service,n).setExtras(extras).setContentText("PC에 연결 중…").setOnlyAlertOnce(true).build();
  service.getSystemService(NotificationManager.class).notify(item.getTag(),item.getId(),posted);service.updateSummaryFor(item.getId(),item.getTag(),posted);
 }
 void connectionFailed(Intent intent){
  StatusBarNotification item=find(intent.getStringExtra("nonce"));if(item==null)return;Bundle input=RemoteInput.getResultsFromIntent(intent);
  CharSequence value=input==null?null:input.getCharSequence(INPUT);status(item,"PC에 연결하지 못했습니다. 다시 시도해 주세요.",value==null?"":value.toString(),false,true);
 }
 void handle(Intent intent){
  String nonce=intent.getStringExtra("nonce");StatusBarNotification item=find(nonce);
  Bundle input=RemoteInput.getResultsFromIntent(intent);CharSequence value=input==null?null:input.getCharSequence(INPUT);
  String text=value==null?"":value.toString().replace("\r\n","\n").replace("\r","\n").trim();int choice=intent.getIntExtra("choice",-1);
  if(item==null||!"new".equals(item.getNotification().extras.getString("tl-state"))){
   if(item!=null&&"sending".equals(item.getNotification().extras.getString("tl-state"))&&(text.isEmpty()||text.equals(item.getNotification().extras.getString("tl-draft","")))){
    status(item,"PC로 전송 중…",item.getNotification().extras.getString("tl-draft",""),true,false);
   }else unavailable(nonce,text,choice);
   return;
  }
  Notification note=item.getNotification();
  if(choice<0&&text.isEmpty()){status(item,"답장 내용을 입력하세요.","",false,true);return;}
  try{
   JSONObject saved=new JSONObject(records.getString(nonce,"{}")),context=saved.getJSONObject("context");if(saved.optBoolean("used")){unavailable(nonce,text,choice);return;}String kind=context.optString("kind");
   if(!service.pin.equals(saved.optString("pcCertificate"))){status(item,"연결한 PC가 바뀌었습니다. 앱에서 확인하세요.",text,false,false);return;}
   if(!service.ready||service.client==null){status(item,"연결이 끊겨 전송하지 못했습니다. 다시 연결한 뒤 답장하세요.",text,false,true);return;}
   if(service.readyMessage==null||service.readyMessage.optInt("notificationReply")!=1||(context.optBoolean("nativeReply")&&service.readyMessage.optInt("nativeNotificationReply")!=1)){status(item,"PC 프로그램을 업데이트해 주세요.",text,false,false);return;}
   if(text.length()>4096||text.matches("(?s).*[\\p{Cntrl}&&[^\\n]].*")){status(item,"4096자 이내로 답장해 주세요.",text,false,true);return;}
   JSONArray choices=context.optJSONArray("choices");
   if(kind.equals("request")&&choice<0&&choices!=null)for(int i=0;i<choices.length()-(context.optBoolean("followup")?1:0);i++)if(text.equals(choices.optString(i))){choice=i;break;}
   if(choice>=0){if(choices==null||choice>=choices.length())return;text=choices.optString(choice);}
   JSONObject command=new JSONObject(context.toString());command.put("op","notificationInput").put("request",nonce).put("text",choice>=0?"":text).put("choice",choice);
   saved.put("used",true);if(!records.edit().putString(nonce,saved.toString()).commit())throw new Exception("Action state could not be saved");
   status(item,"PC로 전송 중…",text,true,false);
   Runnable timeout=()->{deadlines.remove(nonce);StatusBarNotification current=find(nonce);if(current!=null&&"sending".equals(current.getNotification().extras.getString("tl-state")))status(current,"전송 결과를 확인하지 못했습니다. 앱에서 확인하세요.",current.getNotification().extras.getString("tl-draft",""),false,false);};
   deadlines.put(nonce,timeout);service.ui.postDelayed(timeout,60000);service.send(command);
  }catch(Exception e){status(item,"전송하지 못했습니다. 앱에서 확인하세요.",text,false,false);}
 }
 void result(JSONObject result){
  String nonce=result.optString("request");try{JSONObject saved=new JSONObject(records.getString(nonce,"{}"));saved.put("result",result);records.edit().putString(nonce,saved.toString()).commit();}catch(Exception ignored){}
  if(result.optBoolean("ok"))try{JSONObject saved=new JSONObject(records.getString(nonce,"{}")),c=saved.optJSONObject("context");if(c!=null&&c.optString("kind").equals("answer"))AnswerReceipts.mark(service,saved.optString("pcCertificate"),Collections.singleton(AnswerReceipts.tag(c.optString("thread"),c.optLong("sequence"))));}catch(Exception ignored){}
  Runnable timeout=deadlines.remove(nonce);if(timeout!=null)service.ui.removeCallbacks(timeout);
  StatusBarNotification item=find(nonce);if(item==null)return;
  if(item.getNotification().extras.getBoolean("tl-stale-reply"))return;
  if(result.optBoolean("ok")){
   // A direct reply must be acknowledged on the same row before cancellation.
   // Otherwise SystemUI may keep its remote-input row alive with the old action.
   status(item,"전송 완료",item.getNotification().extras.getString("tl-draft",""),false,false);
   Runnable old=dismissals.remove(nonce);if(old!=null)service.ui.removeCallbacks(old);
   Runnable dismiss=()->{
    dismissals.remove(nonce);StatusBarNotification current=find(nonce);
    if(current!=null&&!current.getNotification().extras.getBoolean("tl-stale-reply"))service.getSystemService(NotificationManager.class).cancel(current.getTag(),current.getId());
    summaries(nonce);
   };
   dismissals.put(nonce,dismiss);service.ui.postDelayed(dismiss,350);return;
  }
  status(item,result.optString("message"),item.getNotification().extras.getString("tl-draft",""),false,false);
 }
 void recover(){
  if(service.readyMessage==null||service.readyMessage.optInt("requestLifecycle")!=1)return;
  // Reconnection queries recorded submissions only; never repeats an answer.
  for(java.util.Map.Entry<String,?> entry:records.getAll().entrySet())try{
   if(!(entry.getValue() instanceof String))continue;JSONObject saved=new JSONObject((String)entry.getValue()),c=saved.optJSONObject("context"),result=saved.optJSONObject("result");
   if(!saved.optBoolean("used")||!service.pin.equals(saved.optString("pcCertificate"))||c==null||!c.optString("kind").equals("request")||c.optString("requestKey").isEmpty())continue;
   String delivery=result==null?"":result.optString("delivery");if(result!=null&&(result.optBoolean("ok")||delivery.equals("Rejected")))continue;
   service.send(MainActivity.obj("op","requestStatus","request",entry.getKey(),"submissionId",result==null?entry.getKey():result.optString("submissionId",entry.getKey()),"id",c.optString("id")));
  }catch(Exception ignored){}
 }
 void status(StatusBarNotification item,String message,String draft,boolean pending,boolean retry){
  Notification n=item.getNotification();Bundle extras=new Bundle(n.extras);
  if(!extras.containsKey("tl-original"))extras.putCharSequence("tl-original",extras.getCharSequence(Notification.EXTRA_TEXT));
  extras.putString("tl-state",pending?"sending":retry?"failed":"done");extras.putString("tl-draft",draft);
  extras.putBoolean("android.remoteInputSpinner",pending);extras.putBoolean("android.hideSmartReplies",pending);
  String text=String.valueOf(extras.getCharSequence("tl-original",""))+"\n\n"+message+(draft.isEmpty()?"":"\n나: "+draft);
  Notification.Builder b=new Notification.Builder(service,n.getChannelId()).setSmallIcon(n.getSmallIcon()).setContentTitle(n.extras.getCharSequence(Notification.EXTRA_TITLE)).setContentIntent(n.contentIntent).setWhen(n.when).setGroup(n.getGroup()).setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN).setAutoCancel(true).setCategory(Notification.CATEGORY_MESSAGE).setVisibility(Notification.VISIBILITY_PRIVATE).setPublicVersion(n.publicVersion).setExtras(extras).setOnlyAlertOnce(true).setContentText(message).setStyle(new Notification.BigTextStyle().bigText(text)).setRemoteInputHistory(draft.isEmpty()?null:new CharSequence[]{draft});
  if(retry){JSONObject c=context(n);if(c!=null)decorate(service,service.pin,true,b,c);}
  Notification posted=b.build();service.getSystemService(NotificationManager.class).notify(item.getTag(),item.getId(),posted);service.updateSummaryFor(item.getId(),item.getTag(),posted);
 }
 void unavailable(String nonce,String draft,int choice){
  // An OEM's cached RemoteInput action can outlive its child notification.
  // Never send it to a newer question silently; finish that row and preserve the draft.
  try{
   JSONObject saved=new JSONObject(records.getString(nonce,"{}")),c=saved.optJSONObject("context");if(c==null)return;
   boolean answer=c.optString("kind").equals("answer");int id=answer?LinkService.ANSWER_ID:LinkService.REQUEST_ID;
   String tag=answer?SecureClient.hex(java.security.MessageDigest.getInstance("SHA-256").digest((c.optString("thread")+":"+c.optLong("sequence")).getBytes(java.nio.charset.StandardCharsets.UTF_8))):RequestTracker.digest(c.optString("id"));
   for(StatusBarNotification current:service.getSystemService(NotificationManager.class).getActiveNotifications())if(current.getId()==id&&tag.equals(current.getTag())&&!nonce.equals(current.getNotification().extras.getString("tl-nonce"))){
    // Request notifications reuse a tab's tag. An old action must never replace a newer request.
    service.getSystemService(NotificationManager.class).notify(current.getTag(),current.getId(),current.getNotification());
    tag="reply-rejected:"+nonce;break;
   }
   if(choice>=0&&draft.isEmpty()){JSONArray options=c.optJSONArray("choices");if(options!=null)draft=options.optString(choice);}
   if(draft.length()>4096)draft=draft.substring(0,4096);
   String message="이전 알림의 답장칸입니다. 새 답변 알림이나 앱에서 다시 보내세요.";
   Bundle extras=new Bundle();extras.putString("tl-nonce",nonce);extras.putString("tl-state","done");extras.putString("tl-draft",draft);extras.putBoolean("tl-stale-reply",true);extras.putBoolean("android.remoteInputSpinner",false);extras.putBoolean("android.hideSmartReplies",false);
   Notification note=new Notification.Builder(service,answer?LinkService.ANSWER_CHANNEL:LinkService.REQUEST_CHANNEL).setSmallIcon(service.icon())
    .setContentTitle("답장 전송 확인").setContentText(message).setStyle(new Notification.BigTextStyle().bigText(message+(draft.isEmpty()?"":"\n\n나: "+draft)))
    .setContentIntent(service.open(c.optString("id"),tag,saved.optString("pcCertificate"))).setAutoCancel(true).setOnlyAlertOnce(true).setCategory(Notification.CATEGORY_MESSAGE).setVisibility(Notification.VISIBILITY_PRIVATE)
    .setGroup(answer?"codex_replies":"codex_requests").setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN).setExtras(extras).setRemoteInputHistory(draft.isEmpty()?null:new CharSequence[]{draft}).build();
   Runnable dismissal=dismissals.remove(nonce);if(dismissal!=null)service.ui.removeCallbacks(dismissal);
   service.getSystemService(NotificationManager.class).notify(tag,id,note);service.updateSummaryFor(id,tag,note);
  }catch(Exception ignored){}
 }
 void summaries(){summaries(null);}
 void summaries(String dismissedNonce){service.updateSummary(LinkService.ANSWER_ID,LinkService.SUMMARY_ID,LinkService.ANSWER_CHANNEL,"codex_replies","Codex 답변 도착",dismissedNonce);service.updateSummary(LinkService.REQUEST_ID,LinkService.REQUEST_SUMMARY_ID,LinkService.REQUEST_CHANNEL,"codex_requests","Codex 응답 요청",dismissedNonce);}
 void close(){
  for(String nonce:new ArrayList<>(deadlines.keySet())){
   service.ui.removeCallbacks(deadlines.get(nonce));StatusBarNotification n=find(nonce);
   if(n!=null&&"sending".equals(n.getNotification().extras.getString("tl-state")))status(n,"연결이 종료돼 전송 결과를 확인하지 못했습니다. 대화에서 확인하세요.",n.getNotification().extras.getString("tl-draft",""),false,false);
  }
  deadlines.clear();for(Runnable r:dismissals.values())service.ui.removeCallbacks(r);dismissals.clear();
 }
}
