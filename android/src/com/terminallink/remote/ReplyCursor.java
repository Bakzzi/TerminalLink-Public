package com.terminallink.remote;
import org.json.*;
import java.util.*;

// Stable record sequence, independent of text equality and the trimmed history window.
final class ReplyCursor {
 final LinkedHashMap<String,Long> seen=new LinkedHashMap<>();
 ReplyCursor(String saved){try{JSONObject o=new JSONObject(saved);Iterator<String> keys=o.keys();while(keys.hasNext()){String key=keys.next();seen.put(key,o.optLong(key));}}catch(Exception ignored){}}
 java.util.List<JSONObject> read(JSONObject conversation){
  ArrayList<JSONObject> result=new ArrayList<>();if(conversation==null)return result;
  String thread=conversation.optString("id");JSONArray messages=conversation.optJSONArray("messages");
  if(thread.isEmpty()||messages==null)return result;
  long last=seen.containsKey(thread)?seen.get(thread):-1,max=0;
  for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(m==null)continue;long seq=m.optLong("sequence");max=Math.max(max,seq);
   String phase=m.optString("phase");if(last>=0&&seq>last&&m.optString("role").equals("assistant")&&(phase.equals("final_answer")||phase.equals("final"))&&!m.optString("text").trim().isEmpty())result.add(m);
  }
  if(max==0)return result;
  // Main-view and background-watch snapshots can arrive out of order. Never rewind.
  if(max<last)return new ArrayList<>();
  seen.remove(thread);seen.put(thread,max);while(seen.size()>128)seen.remove(seen.keySet().iterator().next());
  return result;
 }
 String save(){return new JSONObject(seen).toString();}
}
