package com.terminallink.remote;
import org.json.*;import java.util.*;
// Display-only confirmed sends. Never use this list as the original record or input authority.
final class SentEchoes {
 static final class Receipt {
  final String session,thread,text;final long after,id;final String[] files;
  Receipt(String s,String t,long a,String text,String[] f,long id){session=s;thread=t;after=a;this.text=text;files=f;this.id=id;}
  JSONObject message(){String shown=text;for(String file:files)shown+="\n📎 "+file;return MainActivity.obj("role","user","phase","sent","text",shown,"sequence",id);}
 }
 final ArrayList<Receipt> pending=new ArrayList<>();final HashMap<String,Long> matched=new HashMap<>();long serial;
 void clear(){pending.clear();matched.clear();}
 static long last(JSONArray messages){long seq=0;if(messages!=null)for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(m!=null)seq=Math.max(seq,m.optLong("sequence"));}return seq;}
 void add(String session,String thread,long after,String text,String[] files){pending.add(new Receipt(session,thread,after,text,files,--serial));}
 static String normalize(String text){return text.replace("\r\n","\n").replace('\r','\n').trim();}
 static boolean matches(String original,Receipt r){
  String body=normalize(original),text=normalize(r.text);if(r.files.length==0)return body.equals(text);
  body=body.replaceFirst("^(?:\\[Image #\\d+\\]\\s*)+","");if(body.equals(text))return true;
  if(!body.startsWith((text.isEmpty()?"첨부한 파일을 확인해 줘.":text)+"\n\n첨부 파일 (PC 저장 경로):\n")&&!body.startsWith((text.isEmpty()?"첨부한 파일을 확인해 줘.":text)+"\n\n첨부 파일 (원격 저장 경로):\n"))return false;
  for(String f:r.files)if(!f.toLowerCase(java.util.Locale.ROOT).matches(".*\\.(png|jpg|jpeg|webp|bmp|gif)$")&&!body.contains("- "+f+": \""))return false;return true;
 }
 JSONArray project(String session,String thread,JSONArray originals){
  if(originals==null)return null;String key=session+"\n"+thread;long consumed=matched.containsKey(key)?matched.get(key):0;
  for(Iterator<Receipt> it=pending.iterator();it.hasNext();){Receipt r=it.next();if(!r.session.equals(session))continue;if(!r.thread.equals(thread)){it.remove();continue;}
   for(int i=0;i<originals.length();i++){JSONObject m=originals.optJSONObject(i);if(m!=null&&m.optString("role").equals("user")&&m.optLong("sequence")>Math.max(r.after,consumed)&&matches(m.optString("text"),r)){consumed=m.optLong("sequence");it.remove();break;}}
  }
  matched.put(key,consumed);ArrayList<Receipt> remaining=new ArrayList<>();for(Receipt r:pending)if(r.session.equals(session)&&r.thread.equals(thread))remaining.add(r);
  if(remaining.isEmpty())return originals;JSONArray result=new JSONArray();
  for(int i=0;i<originals.length();i++){JSONObject m=originals.optJSONObject(i);if(m==null)continue;for(Iterator<Receipt> it=remaining.iterator();it.hasNext();){Receipt r=it.next();if(r.after<m.optLong("sequence")){result.put(r.message());it.remove();}}result.put(m);}
  for(Receipt r:remaining)result.put(r.message());return result;
 }
}
