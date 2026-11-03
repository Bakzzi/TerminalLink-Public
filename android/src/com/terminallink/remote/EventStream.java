package com.terminallink.remote;
import org.json.*;
import java.util.*;

// Versioned transport state shared by direct event delivery and push notifications.
final class EventStream {
 final LinkService service;
 String epoch;long sequence;boolean resyncPending;
 EventStream(LinkService service){this.service=service;epoch=service.state.getString("event-epoch","");sequence=service.state.getLong("event-sequence",0);}
 void subscribe(){service.send(MainActivity.obj("op","eventSync","epoch",epoch,"after",sequence));}
 void resync(){if(!resyncPending){resyncPending=true;subscribe();}}
 boolean receive(JSONObject packet){
  String type=packet.optString("type");
  if(!type.equals("eventSnapshot")&&!type.equals("eventBatch"))return false;
  String nextEpoch=packet.optString("epoch");if(nextEpoch.isEmpty())return true;
  if(type.equals("eventSnapshot")){
   JSONArray states=packet.optJSONArray("states");long next=packet.optLong("sequence",-1);
   if(states==null||next<0){resync();return true;}
   if(epoch.equals(nextEpoch)&&next<sequence)return true;
   for(int i=0;i<states.length();i++)if(states.optJSONObject(i)==null){resync();return true;}
   for(int i=0;i<states.length();i++)apply(states.optJSONObject(i));
   epoch=nextEpoch;sequence=next;resyncPending=false;
  }else{
   if(!epoch.equals(nextEpoch)){resync();return true;}
   JSONArray events=packet.optJSONArray("events");
   if(events==null){resync();return true;}
   for(int i=0;i<events.length();i++){
    JSONObject e=events.optJSONObject(i);long next=e==null?-1:e.optLong("sequence",-1);
    if(next>=0&&next<=sequence)continue;
    if(next!=sequence+1||e==null||e.optJSONObject("value")==null){resync();break;}
    apply(e.optJSONObject("value"));sequence=next;resyncPending=false;
   }
  }
  service.state.edit().putString("event-epoch",epoch).putLong("event-sequence",sequence).apply();return true;
 }
 void apply(JSONObject value){
  if(value==null)return;
  if(!value.optString("type").equals("replyEvent")){service.receive(value);return;}
  JSONObject conversation=value.optJSONObject("conversation");
  if(conversation==null)return;
  String thread=conversation.optString("id"),id=value.optString("id");
  if(ConversationStatus.available(conversation))AnswerReceipts.replied(service,service.pin,thread,value.optLong("repliedThrough"));
  if(!thread.isEmpty()&&!service.cursor.seen.containsKey(thread))service.cursor.seen.put(thread,value.optLong("baselineSequence"));
  service.observe(id,value.optString("title"),conversation);
  if(value.optBoolean("active")&&service.requestCompletions.contains(id,thread,value.optString("requestKey")))return;
  if(value.optLong("stamp")<service.getSharedPreferences("push-state",0).getLong("stamp:"+id,0))return;
  boolean alreadyNotified=PushNotifications.recordRequest(service,value,id,thread);
  if(!value.optBoolean("active")){service.clearRequest(id);return;}
  JSONObject p=value.optJSONObject("prompt");RequestPrompt prompt=null;
  if(p!=null){
   ArrayList<String> choices=new ArrayList<>();JSONArray a=p.optJSONArray("choices");if(a!=null)for(int i=0;i<a.length();i++)choices.add(a.optString(i));
   prompt=new RequestPrompt(p.optBoolean("followup")?"추가 질문":p.optBoolean("questionKind")?"질문":"승인 요청",p.optString("question"),choices,p.optInt("selected",-1),p.optBoolean("notes"),p.optBoolean("questionKind"),"",p.optBoolean("followup"));
  }
  boolean changed=service.requests.note(id,prompt);
  String key=prompt==null?"?":prompt.key+"|"+value.optString("requestKey"),old=service.requestDetails.put(id,key);
  if(!alreadyNotified&&(changed||!key.equals(old)))service.postRequest(id,prompt,!changed&&Objects.equals(old,key));
  service.save();
 }
 static JSONObject merge(JSONObject previous,JSONObject patch)throws JSONException{
  if(patch==null||!patch.optBoolean("delta"))return patch;
  if(previous==null||!previous.optString("id").equals(patch.optString("id")))throw new JSONException("Missing conversation base");
  JSONArray old=previous.optJSONArray("messages"),added=patch.optJSONArray("messages"),merged=new JSONArray();
  long last=old==null||old.length()==0?0:old.getJSONObject(old.length()-1).optLong("sequence");
  if(last!=patch.optLong("baseSequence"))throw new JSONException("Conversation base changed");
  long first=patch.optLong("firstSequence");if(old!=null)for(int i=0;i<old.length();i++)if(old.getJSONObject(i).optLong("sequence")>=first)merged.put(old.getJSONObject(i));
  if(added!=null)for(int i=0;i<added.length();i++)merged.put(added.getJSONObject(i));
  JSONObject result=new JSONObject(patch.toString());result.remove("delta");result.remove("baseSequence");result.remove("firstSequence");result.put("messages",merged);return result;
 }
}
