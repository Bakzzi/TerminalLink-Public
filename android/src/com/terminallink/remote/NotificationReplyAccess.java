package com.terminallink.remote;
import org.json.JSONObject;
final class NotificationReplyAccess {
 static boolean allowed(JSONObject conversation,JSONObject ready){
  if(conversation==null||ready==null||ready.optInt("notificationReply")!=1)return false;
  if(!conversation.optString("readState","ready").equals("ready"))return false;
  String source=conversation.optString("source","record");
  if(!source.equals("record")&&!source.equals("remote"))return false;
  return !conversation.optBoolean("readOnly")||ready.optInt("nativeNotificationReply")==1;
 }
}
