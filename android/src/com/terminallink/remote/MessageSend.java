package com.terminallink.remote;
import org.json.*;import java.util.UUID;

final class MessageSend {
 final LinkService service;boolean busy;String request="",session="",thread="",text="",error="";long after;Runnable timeout;
 MessageSend(LinkService s){service=s;}
 boolean nativeSubmit(){return service.readyMessage!=null&&service.readyMessage.optInt("nativeMessageSubmit")==1;}
 boolean startNew(String session,String identity,String text){return begin(session,identity,text,0,true);}
 boolean start(String session,String thread,String text,long after){return begin(session,thread,text,after,false);}
 boolean begin(String session,String thread,String text,long after,boolean first){
  if(busy||!service.ready||service.client==null)return false;
  if(service.readyMessage==null||service.readyMessage.optInt("messageSubmit")!=1){error="PC 연결 프로그램을 0.9.39 이상으로 업데이트하세요.";changed();return false;}
  boolean legacyFirst=first&&!nativeSubmit();
  if(legacyFirst&&(service.readyMessage.optInt("startMessage")!=1||!thread.startsWith("start:")||thread.length()<=6)){error="새 대화를 시작하려면 PC 연결 프로그램을 0.9.42 이상으로 업데이트하세요.";changed();return false;}
  this.session=session;this.thread=thread;this.text=text;this.after=after;request=UUID.randomUUID().toString();error="";busy=true;String nonce=request;
  timeout=()->{if(request.equals(nonce)&&busy)fail("전송 결과를 확인하지 못했습니다. 대화를 확인한 뒤 다시 시도하세요. 자동으로 다시 보내지 않았습니다.");};service.ui.postDelayed(timeout,60000);
  changed();service.send(MainActivity.obj("op","notificationInput","kind",legacyFirst?"startMessage":"message","identity",legacyFirst?thread.substring(6):"","request",request,"id",session,"thread",thread,"text",text));return true;
 }
 boolean result(JSONObject result){
  if(request.isEmpty()||!request.equals(result.optString("request")))return false;
  boolean pending=busy;cancel();busy=false;request="";
  if(result.optBoolean("ok")){error="";service.sentMessages.add(session,thread,after,text,new String[0]);MainActivity a=service.viewer;if(a!=null)a.sentMessage(session,thread,text,pending);}
  else error=result.optString("message","전송하지 못했습니다. 내용을 수정한 뒤 다시 보내세요.");
  changed();return true;
 }
 void fail(String why){if(!busy)return;cancel();busy=false;error=why;changed();}
 void cancel(){if(timeout!=null)service.ui.removeCallbacks(timeout);timeout=null;}
 void changed(){if(service.viewer!=null)service.viewer.refreshSendState();}
}
