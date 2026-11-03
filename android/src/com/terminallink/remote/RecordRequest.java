package com.terminallink.remote;
import org.json.*;
import java.util.*;

// Display pending original-record questions without moving the PC composer.
final class RecordRequest {
 final String thread,id,title;final List<String> choices;
 RecordRequest(String t,String i,String q,List<String> c){thread=t;id=i;title=q;choices=Collections.unmodifiableList(c);}
 static RecordRequest first(JSONObject conversation){
  if(!ConversationStatus.available(conversation)||conversation.optString("id").isEmpty())return null;
  JSONArray requests=conversation.optJSONArray("requests");if(requests==null||requests.length()==0)return null;
  JSONObject q=requests.optJSONObject(0);if(q==null||q.optString("id").isEmpty()||q.optString("title").trim().isEmpty())return null;
  List<String> choices=new ArrayList<>();JSONArray options=q.optJSONArray("options");
  if(options!=null)for(int i=0;i<options.length();i++){Object v=options.opt(i);if(!(v instanceof String)||((String)v).trim().isEmpty())return null;choices.add((String)v);}
  return new RecordRequest(conversation.optString("id"),q.optString("id"),q.optString("title"),choices);
 }
 String key(){return thread+"\n"+id+"\n"+title+"\n"+String.join("\n",choices);}
 RequestPrompt prompt(int selected){
  List<String> options=new ArrayList<>(choices);if(!options.isEmpty())options.add("직접 입력");
  return new RequestPrompt("추가 질문",title,options,options.isEmpty()?-1:selected,options.isEmpty()||selected==choices.size(),true,"",true);
 }
 JSONObject command(String session,int selected,String text,JSONObject conversation,JSONObject ready)throws JSONException{
  RecordRequest current=first(conversation);
  if(current==null||!key().equals(current.key())||!NotificationReplyAccess.allowed(conversation,ready))throw new IllegalStateException("요청이 바뀌었거나 연결을 확인할 수 없습니다. 다시 확인해 주세요.");
  boolean free=choices.isEmpty()||selected==choices.size();
  if(selected<0&&!choices.isEmpty()||selected>choices.size())throw new IllegalStateException("답변을 선택하세요.");
  if(free&&(text.trim().isEmpty()||text.length()>4096||text.matches("(?s).*[\\p{Cntrl}&&[^\\n]].*")))throw new IllegalStateException("답변을 4096자 이내로 입력하세요.");
  List<String> options=new ArrayList<>(choices);if(!options.isEmpty())options.add("직접 입력");
  return MainActivity.obj("op","notificationInput","request",UUID.randomUUID().toString(),"id",session,"thread",thread,"kind","request","requestKey",id,"question",title,"choices",new JSONArray(options),"followup",true,"questionKind",true,"choice",free?-1:selected,"text",free?text:"");
 }
}
