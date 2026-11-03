package com.terminallink.remote;
import java.util.*;
import org.json.*;

// Remember active request identities across reconnects. Title animation and editor echo are not new requests.
final class RequestTracker {
 final LinkedHashMap<String,String> active=new LinkedHashMap<>();
 RequestTracker(String saved){try{JSONObject o=new JSONObject(saved);Iterator<String> keys=o.keys();while(keys.hasNext()&&active.size()<64){String id=keys.next();active.put(id,o.optString(id));}}catch(Exception ignored){}}
 boolean contains(String id){return active.containsKey(id);}
 boolean note(String id,RequestPrompt prompt){
  String next=prompt==null?"?":digest(prompt.key),old=active.get(id);
  if(old!=null&&next.equals("?"))return false;
  active.put(id,next);
  while(active.size()>64)active.remove(active.keySet().iterator().next());
  return old==null||(!old.equals("?")&&!old.equals(next));
 }
 void remove(String id){active.remove(id);}
 void retain(Set<String> ids){active.keySet().retainAll(ids);}
 String save(){return new JSONObject(active).toString();}
 static String digest(String s){try{return SecureClient.hex(java.security.MessageDigest.getInstance("SHA-256").digest(s.getBytes(java.nio.charset.StandardCharsets.UTF_8)));}catch(Exception e){throw new IllegalStateException(e);}}
}
