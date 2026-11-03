package com.terminallink.remote;
import org.json.*;import java.util.*;import java.nio.file.*;import android.view.View;
public class PopupJvm {
 static void stability()throws Exception{
  MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
  for(String source:new String[]{"record","remote"}){
   c.clear();a.sent.clear();JSONObject q=chat(source,"stable","읽는 동안 유지해야 하는 질문","선택","다른 선택");
   frame(a,q,"working");c.openRequest();c.choose(2,c.prompt.key,c.sessionId);c.answer.setText("작성 중인 답변\n😀");
   JSONObject failed=MainActivity.obj("id","unavailable:tab-a","source","unavailable","readState","failed","requests",new JSONArray());
   for(int i=0;i<4;i++){frame(a,failed,"working");a.ui.tick();}
   check(c.expanded&&c.dialog.isShowing()&&c.question.text.equals("읽는 동안 유지해야 하는 질문")&&c.answer.text.equals("작성 중인 답변\n😀")&&c.prompt.selected==2,"Temporary read failures retain popup question, selection and draft: "+source);
   c.confirm();check(a.sent.size()==1&&a.sent.get(0).optString("op").equals("conversationRetry"),"Unavailable request permits read retry only, never stale reply: "+source);
   frame(a,q,"working");check(c.live&&c.prompt.selected==2&&c.answer.text.equals("작성 중인 답변\n😀"),"Recovery of same original request restores submission without clearing draft: "+source);
   c.confirm();JSONObject sent=a.sent.get(1);frame(a,failed,"working");
   c.result(MainActivity.obj("request",sent.optString("request"),"ok",true));check(a.link.acknowledged>0&&c.submitting&&c.answer.text.isEmpty(),"Submission result remains correlated across a read failure: "+source);
   JSONObject done=new JSONObject(q.toString()).put("requests",new JSONArray());frame(a,done,"• Queued follow-up inputs\nalt + ↑ to answer");
   check(!c.expanded&&!c.pending&&c.recordRequest==null&&!c.live,"Authoritative completion closes popup despite stale action title or terminal text: "+source);
   frame(a,q,"working");c.openRequest();c.answer.setText("old draft");frame(a,failed,"working");
   frame(a,chat(source,"next","새 요청","새 선택"),"working");check(c.live&&c.question.text.equals("새 요청")&&c.answer.text.isEmpty(),"A different verified request replaces old state: "+source);
   frame(a,failed,"working");a.selectedId+="-new";frame(a,null,"working");a.ui.tick();check(c.recordRequest==null&&!c.expanded&&c.answer.text.isEmpty(),"Tab changes never retain another tab request: "+source);
  }
 }
 static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);System.out.println("PASS "+label);}
 static JSONObject chat(String source,String qid,String title,String... options){return MainActivity.obj("id","thread-"+source,"messages",new JSONArray(),"source",source,"readOnly",true,"readState","ready","requests",new JSONArray().put(MainActivity.obj("id",qid,"title",title,"options",new JSONArray(Arrays.asList(options)))));}
 static void frame(MainActivity a,JSONObject chat,String text){JSONObject f=MainActivity.obj("id",a.selectedId,"title","Action Required","text",text);try{if(chat!=null)f.put("conversation",chat);}catch(Exception e){throw new RuntimeException(e);}a.link.lastFrame=f;a.updateActions(f);}
 public static void main(String[] args)throws Exception{
  stability();
  MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
  String question="요청 보기 테스트야. 질문과 선택지가 제대로 보여?";JSONObject q=chat("record","q1",question,"잘 보여","계속 로딩","이상해");
  frame(a,q,"Unrecognized terminal output");c.openRequest();check(c.live&&!c.opening&&c.question.text.equals(question)&&c.choices.getChildCount()==4,"Original question opens immediately without terminal parsing");check(a.sent.isEmpty(),"Opening record popup sends no terminal keys");
  c.choose(1,c.prompt.key,c.sessionId);check(c.prompt.selected==1&&a.sent.isEmpty(),"Choosing an option remains local until explicit send");
  c.confirm();JSONObject command=a.sent.get(0);check(command.optString("op").equals("notificationInput")&&command.optString("id").equals(a.selectedId)&&command.optString("thread").equals("thread-record")&&command.optString("requestKey").equals("q1")&&command.optInt("choice")==1,"Explicit submit uses existing guarded target and request identity");
  c.confirm();check(a.sent.size()==1,"Repeated send while pending does not duplicate input");
  c.result(MainActivity.obj("request","old","ok",true));check(c.submitting&&!c.replyNonce.isEmpty(),"Unrelated reply result cannot complete popup");
  c.result(MainActivity.obj("request",command.optString("request"),"ok",true));check(c.submitting&&c.replyNonce.isEmpty()&&a.link.acknowledged==1,"Matched success acknowledges request once and waits for removal");
  frame(a,chat("record","q2",question,"잘 보여","계속 로딩","이상해"),"working");check(!c.submitting&&c.recordRequest.id.equals("q2"),"Same wording with a new request ID becomes answerable");
  c.choose(3,c.prompt.key,c.sessionId);String longText="한글 답변 😀\n줄 유지 "+"길게".repeat(700);c.answer.setText(longText);
  a.updateActions(MainActivity.obj("id",a.selectedId,"title","[ . ] Action Required","text","redraw only"));check(c.answer.text.equals(longText)&&c.prompt.selected==3,"Text-only redraw retains cached original, draft and selection");
  c.confirm();JSONObject free=a.sent.get(1);check(free.optInt("choice")==-1&&free.optString("text").equals(longText),"Long multiline Unicode freeform reply is preserved exactly");
  c.result(MainActivity.obj("request",free.optString("request"),"ok",false,"message","PC draft is occupied"));check(!c.submitting&&c.answer.text.equals(longText)&&c.detail.text.contains("occupied"),"Rejected input preserves draft and exposes server reason");
  c.confirm();int sent=a.sent.size();a.ui.tick();check(c.submitting&&c.detail.text.contains("전송 결과")&&a.sent.size()==sent,"Timeout shows uncertainty without automatic retry or losing draft");
  c.dismissPopup();c.openRequest();c.confirm();check(a.sent.size()==sent,"Reopening timed out request cannot resend automatically");
  frame(a,chat("remote","remote-q","SSH 질문"),"no prompt here");check(c.live&&c.answer.getVisibility()==View.VISIBLE&&!c.submitting,"Remote original uses identical popup path and freeform control");
  c.answer.setText("SSH 답변");c.confirm();JSONObject remote=a.sent.get(a.sent.size()-1);check(remote.optString("thread").equals("thread-remote")&&remote.optString("requestKey").equals("remote-q"),"Remote reply keeps original SSH target");
  JSONObject done=chat("remote","unused","unused");done.put("requests",new JSONArray());frame(a,done,"• Queued follow-up inputs\nalt + ↑ to answer");check(!c.live&&!c.queued,"Completed original request is not resurrected from terminal text");
  c.clear();a.sent.clear();c.update("Action Required","Unknown prompt");c.openRequest();check(c.question.text.contains("읽지 못")&&c.confirm.text.equals("다시 확인")&&a.sent.isEmpty(),"Unrecognized request shows explicit error instead of endless loading");c.confirm();check(a.sent.size()==1&&a.sent.get(0).optString("op").equals("conversationRetry")&&a.sent.get(0).optString("id").equals(a.selectedId),"Retry addresses selected original reader and sends no key");
  c.clear();a.sent.clear();frame(a,q,"unknown");a.link.readyMessage=MainActivity.obj("notificationReply",1);c.confirm();check(a.sent.isEmpty()&&!c.replyError.isEmpty(),"Older PC lacking native reply capability cannot receive record input");
  a.link.readyMessage=MainActivity.obj("notificationReply",1,"nativeNotificationReply",1);JSONObject bad=new JSONObject(q.toString());bad.put("readState","failed");check(RecordRequest.first(bad)==null,"Failed original record cannot supply request");bad=new JSONObject(q.toString());bad.put("source","terminal");check(RecordRequest.first(bad)==null,"Screen fallback cannot impersonate original request");
  RecordRequest model=RecordRequest.first(q);boolean rejected=false;try{model.command("tab",0,"",chat("record","other",question,"잘 보여"),a.link.readyMessage);}catch(Exception e){rejected=true;}check(rejected,"Changed request identity is rejected before sending");
  a.selectedId="tab-b";frame(a,chat("record","other","다른 탭"),"unparsed");c.openRequest();check(c.sessionId.equals("tab-b")&&c.question.text.equals("다른 탭")&&c.answer.text.isEmpty(),"Switching tabs clears old reply state and draft");
  String legacy="Question 1/1\nContinue?\n› 1. Yes\n  2. No\nenter to submit answer";c.clear();a.sent.clear();c.update("Action Required",legacy);c.openRequest();check(c.live&&c.recordRequest==null&&c.question.text.equals("Continue?")&&a.sent.isEmpty(),"Existing terminal question parser and no-submit-on-open remain available");c.confirm();int legacySent=a.sent.size();c.update("Action Required",legacy);c.confirm();check(c.submitting&&a.sent.size()==legacySent,"Legacy terminal redraw also preserves submit lock");
 }
}
