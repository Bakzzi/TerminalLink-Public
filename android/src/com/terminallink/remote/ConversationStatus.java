package com.terminallink.remote;
import org.json.*;
// Shared authority boundary for display and notifications, including older PC payloads.
final class ConversationStatus {
 static boolean available(JSONObject conversation){
  if(conversation==null||conversation.optJSONArray("messages")==null)return false;
  String source=conversation.optString("source","record");
  return (source.equals("record")||source.equals("remote"))&&conversation.optString("readState","ready").equals("ready");
 }
 static boolean starting(JSONObject conversation){
  return conversation!=null&&conversation.optString("source").equals("start")&&conversation.optString("id").startsWith("start:")&&conversation.optString("id").length()>6&&conversation.optJSONArray("messages")!=null&&conversation.optJSONArray("messages").length()==0&&(conversation.optString("readState").equals("empty")||conversation.optString("readState").equals("pending"));
 }
 static String error(JSONObject conversation){
  String reason=conversation==null?"":conversation.optString("readError");
  if(reason.isEmpty())reason="이 탭의 대화 원본을 확인하지 못했습니다. 다시 시도하거나 터미널에서 내용을 확인해 주세요.";
  return "대화를 읽지 못했습니다.\n\n"+reason;
 }
}
