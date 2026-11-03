package com.terminallink.remote;
import org.json.*;import java.util.*;import java.nio.file.*;import android.view.View;
public class PopupJvm {
 static void stability()throws Exception{
  MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
  c.setQuestionText("선택할 질문");int questionWrites=c.question.textWrites;
  c.setQuestionText("선택할 질문");check(c.question.textWrites==questionWrites,"Unchanged question is not reset during request refresh");
  c.setQuestionText("바뀐 질문");check(c.question.textWrites==questionWrites+1&&c.question.text.equals("바뀐 질문"),"Changed question is displayed");
  for(String source:new String[]{"record","remote"}){
   c.clear();a.sent.clear();JSONObject q=chat(source,"stable","읽는 동안 유지해야 하는 질문","선택","다른 선택");
   frame(a,q,"working");c.openRequest();c.choose(2,c.prompt.key,c.sessionId);c.answer.setText("작성 중인 답변\n😀");
   JSONObject failed=MainActivity.obj("id","unavailable:tab-a","source","unavailable","readState","failed","requests",new JSONArray());
   for(int i=0;i<4;i++){frame(a,failed,"working");a.ui.tick();}
   check(c.expanded&&c.dialog.isShowing()&&c.question.text.equals("읽는 동안 유지해야 하는 질문")&&c.answer.text.equals("작성 중인 답변\n😀")&&c.prompt.selected==2,"Temporary read failures retain popup question, selection and draft: "+source);
   c.confirm();check(a.sent.size()==1&&a.sent.get(0).optString("op").equals("conversationRetry"),"Unavailable request permits read retry only, never stale reply: "+source);
   frame(a,q,"working");check(c.live&&c.prompt.selected==2&&c.answer.text.equals("작성 중인 답변\n😀"),"Recovery of same original request restores submission without clearing draft: "+source);
   c.confirm();JSONObject sent=a.sent.get(1);frame(a,failed,"working");
   c.result(MainActivity.obj("request",sent.optString("request"),"ok",true));check(a.link.acknowledged>0&&!c.expanded&&!c.pending&&!c.submitting&&c.answer.text.isEmpty(),"Accepted reply closes the popup even across a read failure: "+source);
   JSONObject done=new JSONObject(q.toString()).put("requests",new JSONArray());frame(a,done,"• Queued follow-up inputs\nalt + ↑ to answer");
   check(!c.expanded&&!c.pending&&!c.live,"Authoritative completion does not reopen an accepted popup: "+source);
   c.clear();frame(a,chat(source,"stable-next","Another question","선택"),"working");c.openRequest();c.answer.setText("old draft");frame(a,failed,"working");
   frame(a,chat(source,"next","새 요청","새 선택"),"working");check(c.recordEnded&&c.answer.text.equals("old draft"),"A different request cannot replace a popup being read: "+source);c.nextRequest();check(c.live&&c.question.text.equals("새 요청")&&c.answer.text.isEmpty(),"Explicit next request opens its separate draft: "+source);
   frame(a,failed,"working");a.selectedId+="-new";frame(a,null,"working");a.ui.tick();check(c.recordRequest==null&&!c.expanded&&c.answer.text.isEmpty(),"Tab changes never retain another tab request: "+source);
  }
 }
 static void check(boolean ok,String label){if(!ok)throw new AssertionError(label);System.out.println("PASS "+label);}
 static JSONObject chat(String source,String qid,String title,String... options){return MainActivity.obj("id","thread-"+source,"messages",new JSONArray(),"source",source,"readOnly",true,"readState","ready","requests",new JSONArray().put(MainActivity.obj("id",qid,"title",title,"options",new JSONArray(Arrays.asList(options)))));}
 static void frame(MainActivity a,JSONObject chat,String text){JSONObject f=MainActivity.obj("id",a.selectedId,"title","Action Required","text",text);try{if(chat!=null)f.put("conversation",chat);}catch(Exception e){throw new RuntimeException(e);}a.link.lastFrame=f;a.updateActions(f);}
 static void multiple()throws Exception{
  MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
  JSONObject first=chat("record","batch:0","First pending question","One","Two"),second=chat("record","batch:1","Second pending question","Keep","Reset");
  JSONObject both=new JSONObject(first.toString());both.getJSONArray("requests").put(second.getJSONArray("requests").getJSONObject(0));
  frame(a,both,"• Queued follow-up inputs\n  ? 2 questions · 3s\n    shift+← to answer");c.openRequest();
  check(c.live&&c.recordRequest.id.equals("batch:0")&&c.question.text.equals("First pending question"),"Two pending original questions open the first without losing the conversation");
  c.choose(2,c.prompt.key,c.sessionId);c.answer.setText("first question draft");
  JSONObject reversed=new JSONObject(both.toString()).put("requests",new JSONArray().put(second.getJSONArray("requests").getJSONObject(0)).put(first.getJSONArray("requests").getJSONObject(0)));
  frame(a,reversed,"working");check(c.recordRequest.id.equals("batch:0")&&c.answer.text.equals("first question draft")&&c.prompt.selected==2,"Reordering pending questions preserves the pinned identity, draft and choice");
  c.nextRequest();check(c.recordRequest.id.equals("batch:1")&&c.answer.text.isEmpty(),"Each question has a separate draft");c.nextRequest();check(c.answer.text.equals("first question draft")&&c.prompt.selected==2,"Returning restores the first question draft");
  c.confirm();JSONObject sent=a.sent.get(0);c.result(MainActivity.obj("request",sent.optString("request"),"ok",true));
  frame(a,second,"working");c.openRequest();check(c.live&&!c.submitting&&c.recordRequest.id.equals("batch:1")&&c.question.text.equals("Second pending question"),"Opening the remaining question uses its own request identity");
  c.result(MainActivity.obj("request",sent.optString("request"),"ok",false,"message","old failure"));
  check(c.live&&c.recordRequest.id.equals("batch:1")&&c.replyError.isEmpty(),"Late result for the first question cannot damage the second question");
  String nativePrompt="• Queued follow-up inputs\n  1 of 2\n\nFirst pending question\n› 1. One\n  2. Two\n  3. Other\nenter submit  ctrl + ] skip  shift + → main prompt  shift + ← next question";
  RequestPrompt parsed=RequestPrompt.parse(nativePrompt);
  check(parsed!=null&&parsed.question.equals("First pending question")&&parsed.followup,"Current Shift-arrow footer supports the first of multiple questions");
  check(RequestPrompt.collapsed("• Queued follow-up inputs\n  ? 2 questions · 3s\n    shift+← to answer"),"Current collapsed multiple-question hint is recognized");
  c.clear();a.sent.clear();c.update("Action Required","• Queued follow-up inputs\n  ? 2 questions · 3s\n    shift+← to answer");c.openRequest();
  check(a.sent.size()==1&&a.sent.get(0).optString("key").equals("LEFT")&&a.sent.get(0).optBoolean("shift")&&!a.sent.get(0).optBoolean("alt"),"Opening the current request hint uses Shift-Left, without typing an answer");
  c.update("Action Required",nativePrompt);check(c.live&&c.question.text.equals("First pending question"),"First native question opens after the current shortcut");
  c.update("Action Required",nativePrompt.replace("1 of 2","2 of 2").replace("First pending question","Second pending question").replace("main prompt","prev question").replace("next question","queued messages"));
  check(c.live&&c.question.text.equals("Second pending question"),"Second native question and previous-question footer remain readable");
  check(RequestPrompt.parse(nativePrompt.replace("shift + →","alt + ↓").replace("shift + ←","alt + ↑"))!=null,"Older Alt-arrow question footers remain supported");
 }
 static void completionFlow()throws Exception{
  for(String source:new String[]{"record","remote"})for(boolean emptyBeforeReceipt:new boolean[]{false,true}){
   MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
   JSONObject q=chat(source,"flow:0","Request to finish","Choice one","Choice two");
   frame(a,q,"working");c.openRequest();c.choose(2,c.prompt.key,c.sessionId);c.answer.setText("Exact multiline reply\n한글 😀");c.confirm();
   JSONObject sent=a.sent.get(0);
   JSONObject acceptedConversation=new JSONObject(q.toString()).put("requests",new JSONArray()).put("messages",new JSONArray().put(MainActivity.obj("role","user","text","질문: Request to finish\n\n답변: Exact multiline reply\n한글 😀")));
   if(emptyBeforeReceipt){frame(a,acceptedConversation,"Working; no final answer yet");check(c.submitting&&!c.detail.text.contains("종료되었습니다"),"Receipt race stays in confirmation state, not expired-request state");}
   c.result(MainActivity.obj("request",sent.optString("request"),"ok",true,"delivery","Accepted"));
   check(!c.expanded&&c.dialog==null&&!c.pending&&c.getVisibility()==View.GONE&&!c.submitting,"Accepted closes popup + removes banner before any assistant final: "+source+" emptyFirst="+emptyBeforeReceipt);
   // Old queue, native prompt and animated title can arrive after the receipt.
   for(int i=0;i<3;i++){frame(a,q,"• Queued follow-up inputs\nalt + ↑ to answer");c.openRequest();}
   check(!c.expanded&&!c.pending&&c.recordRequest==null,"Stale snapshots/title cannot resurrect or reopen the completed request: "+source);
   a.ui.advance(70000);check(!c.pending&&a.sent.size()==1,"Late timers do not reopen or resend an accepted reply: "+source);
   MainActivity restored=new MainActivity();restored.link.requestCompletions=new RequestCompletions(a.link.requestCompletions.save());restored.actions=new ActionControls(restored);frame(restored,q,"Action Required");
   check(!restored.actions.pending,"Completion survives Activity/service recreation while the server snapshot is stale: "+source);
   JSONObject newQuestion=chat(source,"flow:1","Request to finish","Choice one","Choice two");frame(a,newQuestion,"Action Required");c.openRequest();check(c.live&&c.recordRequest.id.equals("flow:1"),"Same text with a different request ID remains answerable: "+source);
  }
  MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
  JSONObject first=chat("record","queue:0","First question","One"),both=new JSONObject(first.toString());
  both.getJSONArray("requests").put(chat("record","queue:1","Second question","Two").getJSONArray("requests").getJSONObject(0));
  frame(a,both,"working");c.openRequest();c.confirm();String nonce=a.sent.get(0).optString("request");
  c.result(MainActivity.obj("request",nonce,"ok",false,"delivery","SentUnconfirmed","submissionId","canonical","message","Awaiting receipt"));
  a.ui.advance(1500);JSONObject query=a.sent.get(a.sent.size()-1);
  check(query.optString("op").equals("requestStatus")&&query.optString("request").equals("canonical"),"Unknown delivery automatically queries the same submission instead of sending another answer");
  c.result(MainActivity.obj("request","canonical","ok",true,"delivery","Accepted"));
  check(!c.expanded&&c.pending&&c.recordRequest.id.equals("queue:1"),"Acknowledgement removes only the answered request, preserving the second banner");
  frame(a,both,"• Queued follow-up inputs\nalt + ↑ to answer");c.openRequest();check(c.question.text.equals("Second question"),"Opening a stale two-request snapshot shows only the remaining request");
  check(a.sent.stream().filter(x->x.optString("op").equals("notificationInput")).count()==1,"Status recovery never repeats text or Enter");
  c.answer.setText("unsent draft");JSONObject ended=new JSONObject(both.toString()).put("requests",new JSONArray());frame(a,ended,"Action Required");
  check(c.expanded&&c.recordEnded&&c.answer.text.equals("unsent draft")&&!c.pending,"External completion retains an unsent draft without retaining an actionable banner");
  c.dismissPopup();check(!c.pending&&c.getVisibility()==View.GONE,"Closing an externally ended popup hides the obsolete banner immediately");
  RequestCompletions store=new RequestCompletions("{}");store.track(MainActivity.obj("op","notificationInput","kind","request","request","n","id","tab-a","thread","thread-a","requestKey","item"));
  check(store.result(MainActivity.obj("request","unrelated","ok",true))==null&&!store.contains("tab-a","thread-a","item"),"Unrelated acknowledgements cannot complete a request");
  store.result(MainActivity.obj("request","n","ok",true));check(store.contains("tab-a","thread-a","item")&&!store.contains("tab-b","thread-a","item")&&!store.contains("tab-a","thread-b","item"),"Completion is scoped to exact native tab, thread and request ID");
  check(!store.save().contains("thread-a")&&!store.save().contains("item"),"Saved completion markers contain hashes, not request/answer contents");
  store.retain(new HashSet<>(Arrays.asList("tab-b")));check(!store.contains("tab-a","thread-a","item"),"Closing a native tab retires its completion markers");
  MainActivity compacting=new MainActivity();ActionControls comp=compacting.actions=new ActionControls(compacting);frame(compacting,first,"working");comp.openRequest();comp.confirm();String compactNonce=comp.replyNonce;
  for(int i=0;i<36;i++){compacting.ui.advance(5000);comp.result(MainActivity.obj("request",compactNonce,"ok",false,"delivery","SentUnconfirmed","message","Queued while compacting"));}
  check(comp.submitting&&comp.replyTimeout!=null&&compacting.sent.stream().filter(x->x.optString("op").equals("notificationInput")).count()==1,"Three-minute compaction keeps querying the original send without another answer or Enter");
  comp.result(MainActivity.obj("request",compactNonce,"ok",true,"delivery","Accepted"));check(!comp.expanded&&!comp.pending&&!comp.submitting,"Delayed original receipt closes popup and retires the banner without an assistant final");
  MainActivity delayed=new MainActivity();ActionControls waiting=delayed.actions=new ActionControls(delayed);frame(delayed,first,"working");waiting.openRequest();waiting.confirm();String waitingNonce=waiting.replyNonce;
  for(int i=0;i<65;i++){delayed.ui.advance(5000);waiting.result(MainActivity.obj("request",waitingNonce,"ok",false,"delivery","SentUnconfirmed","message","Awaiting receipt"));}
  int queries=delayed.sent.size();delayed.ui.advance(120000);
  check(queries<=61&&delayed.sent.size()==queries&&delayed.sent.stream().filter(x->x.optString("op").equals("notificationInput")).count()==1,"Automatic status checks stop within 5 minutes and never duplicate an answer");
  waiting.result(MainActivity.obj("request",waitingNonce,"ok",true));check(!waiting.expanded&&!waiting.pending,"Late confirmed delivery still closes the popup after status-query timeout");
 }
 static void ordinaryMessageSupersedes()throws Exception{
  for(String source:new String[]{"record","remote"}){
   MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
   JSONObject q=chat(source,"superseded","Old pending question","One","Two");
   frame(a,q,"• Queued follow-up inputs\nalt + ↑ to answer");
   check(c.pending&&!c.expanded,"Original request shows the banner without opening a popup: "+source);
   JSONObject failed=MainActivity.obj("id",q.optString("id"),"messages",new JSONArray(),"source","unavailable","readState","failed");frame(a,failed,"working");
   check(c.pending,"Read failure while ordinary input is in flight is not request cancellation: "+source);
   JSONObject accepted=new JSONObject(q.toString()).put("requests",new JSONArray());
   accepted.getJSONArray("messages").put(MainActivity.obj("role","user","phase","","text","New ordinary instruction","sequence",1));
   frame(a,accepted,"• Queued follow-up inputs\nalt + ↑ to answer");
   check(!c.pending&&!c.expanded&&c.getVisibility()==View.GONE,"Accepted ordinary input clears the banner before an assistant final, despite stale terminal text: "+source);
   frame(a,accepted,"Action Required");c.openRequest();
   check(!c.pending&&!c.expanded&&a.sent.isEmpty(),"A stale action title cannot reopen or submit the retired request: "+source);
   frame(a,chat(source,"new-request","New pending question","Choice"),"working");
   check(c.pending&&c.recordRequest.id.equals("new-request"),"The next request is still shown normally: "+source);
  }
 }
 public static void main(String[] args)throws Exception{
  ordinaryMessageSupersedes();completionFlow();stability();multiple();
  MainActivity a=new MainActivity();ActionControls c=a.actions=new ActionControls(a);
  String question="요청 보기 테스트야. 질문과 선택지가 제대로 보여?";JSONObject q=chat("record","q1",question,"잘 보여","계속 로딩","이상해");
  frame(a,q,"Unrecognized terminal output");c.openRequest();check(c.live&&!c.opening&&c.question.text.equals(question)&&c.choices.getChildCount()==4,"Original question opens immediately without terminal parsing");check(a.sent.isEmpty(),"Opening record popup sends no terminal keys");
  c.choose(1,c.prompt.key,c.sessionId);check(c.prompt.selected==1&&a.sent.isEmpty(),"Choosing an option remains local until explicit send");
  c.confirm();JSONObject command=a.sent.get(0);check(command.optString("op").equals("notificationInput")&&command.optString("id").equals(a.selectedId)&&command.optString("thread").equals("thread-record")&&command.optString("requestKey").equals("q1")&&command.optInt("choice")==1,"Explicit submit uses existing guarded target and request identity");
  c.confirm();check(a.sent.size()==2&&a.sent.get(1).optString("op").equals("requestStatus"),"Repeated send while pending queries status instead of duplicating input");
  c.result(MainActivity.obj("request","old","ok",true));check(c.submitting&&!c.replyNonce.isEmpty(),"Unrelated reply result cannot complete popup");
  c.result(MainActivity.obj("request",command.optString("request"),"ok",true));check(!c.expanded&&!c.pending&&c.replyNonce.isEmpty()&&a.link.acknowledged==1,"Matched receipt closes popup and clears its banner immediately");
  frame(a,chat("record","q2",question,"잘 보여","계속 로딩","이상해"),"working");c.openRequest();check(!c.submitting&&c.recordRequest.id.equals("q2"),"Same wording with a new request ID becomes separately answerable");
  c.choose(3,c.prompt.key,c.sessionId);String longText="한글 답변 😀\n줄 유지 "+"길게".repeat(700);c.answer.setText(longText);
  a.updateActions(MainActivity.obj("id",a.selectedId,"title","[ . ] Action Required","text","redraw only"));check(c.answer.text.equals(longText)&&c.prompt.selected==3,"Text-only redraw retains cached original, draft and selection");
  c.confirm();JSONObject free=a.sent.get(a.sent.size()-1);check(free.optInt("choice")==-1&&free.optString("text").equals(longText),"Long multiline Unicode freeform reply is preserved exactly");
  c.result(MainActivity.obj("request",free.optString("request"),"ok",false,"message","PC draft is occupied"));check(!c.submitting&&c.answer.text.equals(longText)&&c.detail.text.contains("occupied"),"Rejected input preserves draft and exposes server reason");
  c.confirm();int sent=a.sent.size();a.ui.tick();check(c.submitting&&c.detail.text.contains("전송 결과")&&a.sent.size()==sent,"Timeout shows uncertainty without automatic retry or losing draft");
  c.dismissPopup();c.openRequest();c.confirm();check(a.sent.size()==sent+1&&a.sent.get(sent).optString("op").equals("requestStatus"),"Reopening timed out request queries the same operation without repeating the answer");
  frame(a,chat("remote","remote-q","SSH 질문"),"no prompt here");check(c.live&&c.answer.getVisibility()==View.VISIBLE&&!c.submitting,"Remote original uses identical popup path and freeform control");
  c.answer.setText("SSH 답변");c.confirm();JSONObject remote=a.sent.get(a.sent.size()-1);check(remote.optString("thread").equals("thread-remote")&&remote.optString("requestKey").equals("remote-q"),"Remote reply keeps original SSH target");
  JSONObject done=chat("remote","unused","unused");done.put("requests",new JSONArray());frame(a,done,"• Queued follow-up inputs\nalt + ↑ to answer");check(!c.live&&!c.queued,"Completed original request is not resurrected from terminal text");
  c.clear();a.sent.clear();c.update("Action Required","Unknown prompt");c.openRequest();check(c.question.text.contains("읽지 못")&&c.confirm.text.equals("다시 확인")&&a.sent.isEmpty(),"Unrecognized request shows explicit error instead of endless loading");c.confirm();check(a.sent.size()==1&&a.sent.get(0).optString("op").equals("conversationRetry")&&a.sent.get(0).optString("id").equals(a.selectedId),"Retry addresses selected original reader and sends no key");
  q=chat("record","q-unsupported",question,"잘 보여","계속 로딩","이상해");c.clear();a.sent.clear();frame(a,q,"unknown");a.link.readyMessage=MainActivity.obj("notificationReply",1);c.confirm();check(a.sent.isEmpty()&&!c.replyError.isEmpty(),"Older PC lacking native reply capability cannot receive record input");
  a.link.readyMessage=MainActivity.obj("notificationReply",1,"nativeNotificationReply",1);JSONObject bad=new JSONObject(q.toString());bad.put("readState","failed");check(RecordRequest.first(bad)==null,"Failed original record cannot supply request");bad=new JSONObject(q.toString());bad.put("source","terminal");check(RecordRequest.first(bad)==null,"Screen fallback cannot impersonate original request");
  RecordRequest model=RecordRequest.first(q);boolean rejected=false;try{model.command("tab",0,"",chat("record","other",question,"잘 보여"),a.link.readyMessage);}catch(Exception e){rejected=true;}check(rejected,"Changed request identity is rejected before sending");
  a.selectedId="tab-b";frame(a,chat("record","other","다른 탭"),"unparsed");c.openRequest();check(c.sessionId.equals("tab-b")&&c.question.text.equals("다른 탭")&&c.answer.text.isEmpty(),"Switching tabs clears old reply state and draft");
  String legacy="Question 1/1\nContinue?\n› 1. Yes\n  2. No\nenter to submit answer";c.clear();a.sent.clear();c.update("Action Required",legacy);c.openRequest();check(c.live&&c.recordRequest==null&&c.question.text.equals("Continue?")&&a.sent.isEmpty(),"Existing terminal question parser and no-submit-on-open remain available");c.confirm();int legacySent=a.sent.size();c.update("Action Required",legacy);c.confirm();check(c.submitting&&a.sent.size()==legacySent,"Legacy terminal redraw also preserves submit lock");
 }
}
