package com.terminallink.remote;
import org.json.*;
import java.util.*;

// Receipt-backed display state. A stale snapshot/title cannot reopen an answered
// request. Only identifiers are hashed/persisted; no question or answer is stored.
final class RequestCompletions {
 static final class Target {
  final String session,thread,item;
  Target(String s,String t,String i){session=s;thread=t;item=i;}
 }
 final Map<String,Set<String>> completed=new HashMap<>();
 final Map<String,Target> pending=new HashMap<>();
 RequestCompletions(String saved){try{JSONObject root=new JSONObject(saved);Iterator<String> keys=root.keys();while(keys.hasNext()){String key=keys.next();JSONArray a=root.optJSONArray(key);if(!key.matches("[a-f0-9]{64}")||a==null)continue;Set<String> ids=new HashSet<>();for(int i=0;i<a.length();i++){String id=a.optString(i);if(id.matches("[a-f0-9]{64}"))ids.add(id);}completed.put(key,ids);}}catch(Exception ignored){}}
 static String hash(String value){try{byte[] bytes=java.security.MessageDigest.getInstance("SHA-256").digest(value.getBytes(java.nio.charset.StandardCharsets.UTF_8));StringBuilder s=new StringBuilder();for(byte b:bytes)s.append(Character.forDigit((b&255)>>4,16)).append(Character.forDigit(b&15,16));return s.toString();}catch(Exception e){throw new IllegalStateException(e);}}
 void track(JSONObject command){
  if(!command.optString("op").equals("notificationInput")||!command.optString("kind").equals("request"))return;
  String nonce=command.optString("request"),session=command.optString("id"),thread=command.optString("thread"),item=command.optString("requestKey");
  if(!nonce.isEmpty()&&!session.isEmpty()&&!thread.isEmpty()&&!item.isEmpty())pending.put(nonce,new Target(session,thread,item));
 }
 Target result(JSONObject result){
  String nonce=result.optString("request");Target target=pending.get(nonce);if(target==null)return null;
  if(result.optBoolean("ok")){complete(target.session,target.thread,target.item);pending.entrySet().removeIf(e->e.getValue()==target);return target;}
  String delivery=result.optString("delivery");
  if(!delivery.equals("SentUnconfirmed")&&!delivery.equals("Dispatching")&&!delivery.equals("Unknown"))pending.entrySet().removeIf(e->e.getValue()==target);
  else{String canonical=result.optString("submissionId");if(!canonical.isEmpty())pending.put(canonical,target);}
  return null;
 }
 void complete(String session,String thread,String item){completed.computeIfAbsent(hash(session),k->new HashSet<>()).add(hash(thread+"\n"+item));}
 boolean contains(String session,String thread,String item){Set<String> ids=completed.get(hash(session));return ids!=null&&ids.contains(hash(thread+"\n"+item));}
 JSONObject filter(String session,JSONObject chat){
  if(chat==null)return null;JSONArray list=chat.optJSONArray("requests");if(list==null)return chat;
  JSONArray remaining=new JSONArray();for(int i=0;i<list.length();i++){JSONObject q=list.optJSONObject(i);if(q==null||!contains(session,chat.optString("id"),q.optString("id")))remaining.put(list.opt(i));}
  if(remaining.length()==list.length())return chat;
  // Shallow projection: retain the existing transcript, never copy its text or
  // mutate the original snapshot that other consumers still hold.
  JSONObject copy=new JSONObject();try{Iterator<String> keys=chat.keys();while(keys.hasNext()){String key=keys.next();copy.put(key,chat.opt(key));}copy.put("requests",remaining);}catch(JSONException e){throw new IllegalStateException(e);}return copy;
 }
 void retain(Set<String> sessions){Set<String> keys=new HashSet<>();for(String id:sessions)keys.add(hash(id));completed.keySet().retainAll(keys);pending.entrySet().removeIf(e->!sessions.contains(e.getValue().session));}
 String save(){JSONObject root=new JSONObject();try{for(Map.Entry<String,Set<String>> e:completed.entrySet())root.put(e.getKey(),new JSONArray(e.getValue()));}catch(JSONException e){throw new IllegalStateException(e);}return root.toString();}
}
