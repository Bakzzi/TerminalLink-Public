package com.terminallink.remote;
import android.app.Dialog;
import android.graphics.Color;
import android.graphics.drawable.ColorDrawable;
import android.graphics.drawable.GradientDrawable;
import android.text.InputType;
import android.view.*;
import android.view.inputmethod.InputMethodManager;
import android.widget.*;
import java.util.Locale;
import org.json.*;

// Operate the already running CLI's prompt. Never infer or submit an answer automatically.
final class ActionControls extends LinearLayout {
 final MainActivity activity;final TextView label;final Button open;final HorizontalScrollView tools;
 final LinearLayout card,choices;final TextView question,detail,cardTitle;final Button confirm;final EditText answer;
 final ScrollView scroll;final FrameLayout overlay;Dialog dialog;
 boolean pending=false,expanded=false,queued=false,live=false,opening=false,submitting=false,recordUnavailable=false;
 RecordRequest recordRequest;JSONObject conversation;String replyNonce="",replyError="",requestOpenKey="";Runnable replyTimeout,replyStatus;
 String lastTitle="",lastText="";
 String sessionId="",rendered="",fallback="",shownQuestion="";Runnable dismissPending,unlock;RequestPrompt prompt;int awaiting=-1;
 boolean recordEnded=false;int replyQueries;final Button nextQuestion;
 final java.util.Map<String,String> drafts=new java.util.HashMap<>();
 final java.util.Map<String,Integer> selections=new java.util.HashMap<>();
 final java.util.Map<String,String> submissions=new java.util.HashMap<>();
 ActionControls(MainActivity a){
  super(a);activity=a;setOrientation(VERTICAL);setTag("action-controls");
  LinearLayout bar=a.row();label=a.label("응답 요청",12,0xffffd18a);bar.addView(label,new LayoutParams(0,-2,1));
  open=a.button("요청 보기",v->openRequest());open.setTag("action-open");bar.addView(open);addView(bar,new LayoutParams(-1,a.dp(40)));
  overlay=new FrameLayout(a);overlay.setOnClickListener(v->dismissPopup());
  card=new LinearLayout(a){@Override protected void onMeasure(int width,int height){super.onMeasure(width,MeasureSpec.makeMeasureSpec(Math.min(MeasureSpec.getSize(height),a.dp(620)),MeasureSpec.AT_MOST));}};card.setOrientation(VERTICAL);card.setTag("request-card");card.setPadding(a.dp(16),a.dp(12),a.dp(16),a.dp(12));card.setClickable(true);
  GradientDrawable background=new GradientDrawable();background.setColor(0xff19283a);background.setCornerRadius(a.dp(20));card.setBackground(background);
  overlay.addView(card,new FrameLayout.LayoutParams(-1,-1,Gravity.CENTER));
  overlay.setOnApplyWindowInsetsListener((v,insets)->{
   android.graphics.Insets bars=insets.getInsets(WindowInsets.Type.systemBars()),ime=insets.getInsets(WindowInsets.Type.ime());
   int bottom=Math.max(bars.bottom,ime.bottom),margin=a.dp(16);
   v.setPadding(bars.left+margin,bars.top+margin,bars.right+margin,bottom+margin);
   android.graphics.Rect bounds=a.getWindowManager().getCurrentWindowMetrics().getBounds();
   FrameLayout.LayoutParams size=(FrameLayout.LayoutParams)card.getLayoutParams();
   size.width=Math.min(a.dp(560),Math.max(1,bounds.width()-bars.left-bars.right-2*margin));
   size.height=LayoutParams.WRAP_CONTENT;card.setLayoutParams(size);return insets;
  });
  LinearLayout heading=a.row();cardTitle=a.label("응답 요청",17,0xffffd18a);heading.addView(cardTitle,new LayoutParams(0,-2,1));
  nextQuestion=a.button("다른 질문",v->nextRequest());heading.addView(nextQuestion);
  Button close=a.button("나중에",v->dismissPopup());close.setTag("action-collapse");close.setContentDescription("요청 팝업 닫기 · 답변은 전송하지 않음");heading.addView(close);card.addView(heading);
  scroll=new ScrollView(a);scroll.setFillViewport(false);LinearLayout body=new LinearLayout(a);body.setOrientation(VERTICAL);scroll.addView(body);
  question=a.label("",17,a.FG);question.setTag("request-question");question.setTextIsSelectable(true);body.addView(question);
  choices=new LinearLayout(a);choices.setOrientation(VERTICAL);body.addView(choices);
  detail=a.label("",12,a.MUTED);detail.setMinLines(2);body.addView(detail);card.addView(scroll,new LayoutParams(-1,0,1));
  tools=new HorizontalScrollView(a);tools.setHorizontalScrollBarEnabled(false);LinearLayout keys=a.row();
  key(keys,"↑","UP",false);key(keys,"↓","DOWN",false);key(keys,"Tab","TAB",false);key(keys,"Shift+Tab","TAB",true);
  key(keys,"Space","SPACE",false);key(keys,"Enter","ENTER",false);key(keys,"Esc","ESC",false);tools.addView(keys);card.addView(tools,new LayoutParams(-1,a.dp(44)));
  answer=a.input("직접 답변하거나 선택한 답변에 메모를 덧붙이세요","");
  answer.setTag("request-answer");answer.setSingleLine(false);answer.setMinLines(1);answer.setMaxLines(3);
  answer.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_MULTI_LINE|InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
  card.addView(answer,new LayoutParams(-1,-2));
  confirm=a.button("답변 보내기",v->confirm());confirm.setTag("request-confirm");confirm.setTextSize(15);
  confirm.setBackgroundTintList(android.content.res.ColorStateList.valueOf(0xff245354));card.addView(confirm,new LayoutParams(-1,a.dp(48)));
  card.setFocusableInTouchMode(true);card.setVisibility(GONE);setVisibility(GONE);
 }
 void key(LinearLayout keys,String text,String key,boolean shift){Button b=activity.button(text,v->sendKey(key,shift));b.setTag("action-key-"+(shift?"SHIFT-":"")+key);keys.addView(b,new LayoutParams(-2,activity.dp(44)));}
 void update(String title,String text){update(title,text,null);}
 void update(String title,String text,JSONObject current){
  if(!activity.selectedId.equals(sessionId)){clear();sessionId=activity.selectedId;}
  lastTitle=title;lastText=text;
  if(activity.link!=null)current=activity.link.requestCompletions.filter(sessionId,current);
  conversation=current;
  RecordRequest nextRecord=RecordRequest.first(current);
  // Read failure is not request completion. Keep presentation/draft, but grant no input.
  boolean available=ConversationStatus.available(current);
  JSONArray requests=current==null?null:current.optJSONArray("requests");
  boolean sameThread=recordRequest!=null&&available&&recordRequest.thread.equals(current.optString("id"));
  if(sameThread&&recordRequest!=null){
   RecordRequest pinned=RecordRequest.find(current,recordRequest.id);
   if(pinned!=null)nextRecord=pinned;
   else if(expanded){recordEnded=true;live=false;recordUnavailable=false;pending=nextRecord!=null;cancelDismiss();render();return;}
  }
  if(recordRequest!=null&&(!available||sameThread&&(requests==null||requests.length()>0&&nextRecord==null))){
   recordUnavailable=true;live=false;queued=false;opening=false;pending=true;cancelDismiss();render();return;
  }
  recordUnavailable=false;
  // Only an authoritative empty request list completes a previously shown record request.
  if(sameThread&&requests!=null&&requests.length()==0){
   cancelReplyTimeout();cancelDismiss();cancelUnlock();recordRequest=null;replyNonce="";replyError="";
   pending=false;expanded=false;queued=false;live=false;opening=false;submitting=false;awaiting=-1;prompt=null;answer.setText("");render();return;
  }
  boolean sameRecord=recordRequest!=null&&nextRecord!=null&&recordRequest.key().equals(nextRecord.key());
  if(!sameRecord&&(recordRequest!=null||nextRecord!=null)){saveDraft();cancelReplyTimeout();replyNonce="";replyError="";submitting=false;recordEnded=false;if(recordRequest!=null&&nextRecord==null)prompt=null;}
  recordRequest=nextRecord;
  requestOpenKey=RequestPrompt.openKey(text);queued=nextRecord==null&&!requestOpenKey.isEmpty();
  RequestPrompt next=nextRecord!=null?nextRecord.prompt(sameRecord&&prompt!=null?prompt.selected:0):RequestPrompt.parse(text);
  // A completed original request must not be resurrected from old visible terminal text.
  if(nextRecord==null&&current!=null&&(!available||requests!=null&&requests.length()==0)){queued=false;if(next!=null&&next.followup)next=null;}
  live=next!=null;
  if(next!=null){
   if(prompt==null||!prompt.key.equals(next.key)||nextRecord!=null&&!sameRecord){cancelUnlock();awaiting=-1;submitting=false;answer.setText("");hideAnswerKeyboard();}
   prompt=next;opening=false;
   if(recordRequest!=null&&!sameRecord){String k=draftKey();answer.setText(drafts.getOrDefault(k,""));prompt=recordRequest.prompt(selections.getOrDefault(k,0));replyNonce=submissions.getOrDefault(k,"");submitting=!replyNonce.isEmpty();}
   if(awaiting>=0&&next.selected==awaiting){cancelUnlock();awaiting=-1;}
  }
  boolean detected=queued||live||(current==null&&RequestPrompt.requiresAction(title));
  if(detected){cancelDismiss();pending=true;}
  else if(current!=null&&available&&requests!=null&&requests.length()==0){cancelDismiss();pending=false;}
  else if(pending&&dismissPending==null){
   String requestSession=sessionId;
   dismissPending=()->{dismissPending=null;if(!sessionId.equals(requestSession))return;pending=false;expanded=false;prompt=null;submitting=false;opening=false;awaiting=-1;answer.setText("");cancelUnlock();render();};
   activity.ui.postDelayed(dismissPending,1500);
  }
  render();
 }
 void render(){
  setVisibility(pending||expanded?VISIBLE:GONE);
  label.setText("답변·선택이 필요한 요청이 있어요");
  open.setText(opening?"불러오는 중…":"요청 보기");open.setEnabled(!opening);
  if(!expanded){dismissPopup();card.setVisibility(GONE);return;}
  JSONArray pendingList=conversation==null?null:conversation.optJSONArray("requests");nextQuestion.setVisibility(pendingList!=null&&pendingList.length()>0&&recordRequest!=null?VISIBLE:GONE);nextQuestion.setEnabled(!submitting);
  String value=recordEnded+"|"+replyNonce+"|"+replyError+"|"+recordUnavailable+"|"+live+"|"+awaiting+"|"+submitting+"|"+queued+"|"+(prompt==null?"":prompt.key+"|"+prompt.selected+"|"+prompt.notes+"|"+prompt.details);
  if(value.equals(rendered))return;rendered=value;card.setVisibility(VISIBLE);
  String identity=prompt==null?"":prompt.key;boolean changed=!shownQuestion.equals(identity);shownQuestion=identity;
  int oldY=scroll.getScrollY();choices.removeAllViews();
  tools.setVisibility(GONE);
  if(prompt==null){
   cardTitle.setText("응답 요청");setQuestionText("요청 내용을 읽지 못했습니다.");
   detail.setText("다시 확인을 누르거나 터미널에서 현재 요청을 확인해 주세요.");
   answer.setVisibility(GONE);confirm.setVisibility(VISIBLE);confirm.setText("다시 확인");confirm.setEnabled(activity.inputReady());
  }else{
   cardTitle.setText(prompt.heading);setQuestionText(prompt.question);
   for(int i=0;i<prompt.choices.size();i++){
    final int index=i;final String key=prompt.key,session=sessionId;
    Button b=activity.button((i==prompt.selected?"●  ":"○  ")+prompt.choices.get(i),v->choose(index,key,session));
    b.setTag("request-option-"+i);b.setAllCaps(false);b.setGravity(Gravity.START|Gravity.CENTER_VERTICAL);b.setTextSize(15);
    b.setPadding(activity.dp(12),activity.dp(8),activity.dp(12),activity.dp(8));b.setMinimumHeight(activity.dp(52));
    b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(i==prompt.selected?0xff245354:0xff253449));
    b.setEnabled(live&&awaiting<0&&!submitting&&(!prompt.notes||prompt.followup)&&prompt.selected>=0);choices.addView(b,new LayoutParams(-1,-2));
   }
   detail.setText(!replyError.isEmpty()?replyError:submitting?(recordRequest!=null&&!replyNonce.isEmpty()?"PC로 답변을 전송하고 있습니다…":"답변을 보냈습니다. PC의 다음 내용을 기다리고 있어요."):awaiting>=0?"PC에 선택을 반영하고 있습니다…":(prompt.details.isEmpty()?"":prompt.details+"\n")+(prompt.questionKind?(prompt.followup?(prompt.notes?"아래에 답변을 작성한 뒤 보내세요.":"답변을 선택하거나 ‘직접 입력’을 누르세요."):(prompt.choices.isEmpty()?"아래에 답변을 작성한 뒤 보내세요.":"선택하거나 아래에 직접 답변한 뒤 보내세요.")):"선택 내용을 확인한 뒤 확인 버튼을 누르세요."));
   answer.setHint(prompt.choices.isEmpty()?"답변을 입력하세요":"직접 답변하거나 선택한 답변에 메모를 덧붙이세요");
   answer.setVisibility(prompt.questionKind&&(!prompt.followup||prompt.notes)?VISIBLE:GONE);answer.setEnabled(awaiting<0&&!submitting);
   confirm.setVisibility(VISIBLE);confirm.setText(prompt.questionKind?"답변 보내기":"선택 확인");confirm.setEnabled(live&&awaiting<0&&!submitting);
   if(recordUnavailable){
    detail.setText(submitting?"전송 결과와 요청 상태를 확인하고 있습니다. 자동으로 다시 보내지 않습니다.":"요청 상태를 다시 확인하고 있습니다. 질문과 작성한 답변은 유지됩니다.");
    confirm.setText("다시 확인");confirm.setEnabled(activity.inputReady()&&!submitting);
   }
   if(recordEnded){detail.setText(submitting?(!replyError.isEmpty()?replyError:"답변 도착 결과를 확인하고 있습니다. 자동으로 다시 보내지 않습니다."):"이 요청은 종료되었습니다. 작성한 내용은 유지됩니다. ‘다른 질문’에서 남은 요청을 확인하세요.");confirm.setEnabled(false);answer.setEnabled(false);}
   if(!replyNonce.isEmpty()&&submitting){confirm.setText("전송 결과 확인");confirm.setEnabled(activity.inputReady());}
  }
  scroll.post(()->{if(expanded&&shownQuestion.equals(identity))scroll.scrollTo(0,changed?0:oldY);});
 }
 void setQuestionText(String text){if(!java.util.Objects.equals(question.getText().toString(),text))question.setText(text);}
 boolean usable(){return !recordEnded&&activity.inputReady()&&sessionId.equals(activity.selectedId)&&live&&prompt!=null&&awaiting<0&&!submitting;}
 void choose(int index,String identity,String session){
  if(!usable()||!sessionId.equals(session)||!prompt.key.equals(identity)||prompt.selected<0||index==prompt.selected)return;
  if(recordRequest!=null){prompt=recordRequest.prompt(index);render();return;}
  if(prompt.notes&&!prompt.followup)return;
  int delta=index-prompt.selected;awaiting=index;
  for(int i=0;i<Math.abs(delta);i++)activity.send(MainActivity.obj("op","key","key",delta>0?"DOWN":"UP"));
  cancelUnlock();unlock=()->{unlock=null;awaiting=-1;render();};activity.ui.postDelayed(unlock,3000);render();
 }
 void confirm(){
  if(!replyNonce.isEmpty()&&submitting){activity.send(MainActivity.obj("op","requestStatus","request",replyNonce,"id",sessionId));return;}
  if(recordUnavailable){if(activity.inputReady()&&!submitting&&sessionId.equals(activity.selectedId))activity.send(MainActivity.obj("op","conversationRetry","id",sessionId));return;}
  if(prompt==null){if(activity.inputReady())activity.send(MainActivity.obj("op","conversationRetry","id",sessionId));return;}
  if(!usable())return;
  if(recordRequest!=null){confirmRecord();return;}
  String value=prompt.questionKind&&(!prompt.followup||prompt.notes)?answer.getText().toString():"";
  if(prompt.followup&&prompt.notes&&value.trim().isEmpty()){answer.setError("답변을 입력하세요.");return;}
  if(value.length()>4096){answer.setError("한 번에 4096자까지 입력할 수 있습니다.");return;}
  answer.setError(null);submitting=true;
  // Open notes before pasting; never toggle TAB when PC notes are already open.
  if(!value.isEmpty()){if(!prompt.followup&&!prompt.notes&&!prompt.choices.isEmpty())activity.send(MainActivity.obj("op","key","key","TAB"));activity.send(MainActivity.obj("op","text","text",value));}
  activity.send(MainActivity.obj("op","key","key","ENTER"));answer.setText("");hideAnswerKeyboard();render();
  // No automatic resend: the next question or completion releases the submitted state.
 }
 void openRequest(){
  if(!pending||!activity.inputReady()||!sessionId.equals(activity.selectedId))return;
  showPopup();
  if(recordRequest==null&&queued&&!opening){String openingSession=sessionId;opening=true;boolean shift=requestOpenKey.equals("SHIFT_LEFT");activity.send(MainActivity.obj("op","key","key",shift?"LEFT":"UP","shift",shift,"alt",!shift));render();
   activity.ui.postDelayed(()->{if(opening&&sessionId.equals(openingSession)){opening=false;render();}},3000);}
 }
 void confirmRecord(){
  try{
   JSONObject ready=activity.link==null?null:activity.link.readyMessage;
   JSONObject command=recordRequest.command(sessionId,prompt.selected,answer.getText().toString(),conversation,ready);
   replyNonce=command.optString("request");replyError="";submitting=true;replyQueries=0;
   saveDraft();submissions.put(draftKey(),replyNonce);
   String operation=draftKey();cancelReplyTimeout();
   replyTimeout=()->{replyTimeout=null;cancelStatus();if(!draftKey().equals(operation)||!submitting)return;replyError="전송 결과를 확인하지 못했습니다. 대화나 터미널을 확인하세요. 자동으로 다시 보내지 않습니다.";render();};
   activity.ui.postDelayed(replyTimeout,300000);activity.send(command);scheduleStatus();render();
  }catch(Exception e){replyError=e.getMessage()==null?"전송할 수 없습니다. 연결을 확인하세요.":e.getMessage();render();}
 }
 void result(JSONObject result){
  if(replyNonce.isEmpty()||!replyNonce.equals(result.optString("request")))return;
  String delivery=result.optString("delivery");
  if(!result.optBoolean("ok")&&(delivery.equals("SentUnconfirmed")||delivery.equals("Dispatching")||delivery.equals("Unknown"))){String canonical=result.optString("submissionId");if(!canonical.isEmpty())replyNonce=canonical;submissions.put(draftKey(),replyNonce);submitting=true;replyError=result.optString("message");scheduleStatus();render();return;}
  cancelReplyTimeout();
  submissions.remove(draftKey());replyNonce="";
  if(result.optBoolean("ok")){
   if(activity.link!=null&&recordRequest!=null)activity.link.completeRequest(sessionId,recordRequest.thread,recordRequest.id);
   drafts.remove(draftKey());selections.remove(draftKey());answer.setText("");
   recordRequest=null;prompt=null;replyError="";recordEnded=false;recordUnavailable=false;submitting=false;pending=false;queued=false;live=false;opening=false;awaiting=-1;cancelDismiss();cancelUnlock();dismissPopup();
   update(lastTitle,lastText,conversation);return;
  }
  else{submitting=false;replyError=result.optString("message","전송하지 못했습니다. 다시 확인하세요.");}
  render();
 }
 String draftKey(){return sessionId+"\n"+(recordRequest==null?"":recordRequest.key());}
 void saveDraft(){if(recordRequest!=null){drafts.put(draftKey(),answer.getText().toString());if(prompt!=null)selections.put(draftKey(),prompt.selected);}}
 void nextRequest(){
  if(submitting||conversation==null)return;JSONArray list=conversation.optJSONArray("requests");if(list==null||list.length()==0)return;
  int index=-1;for(int i=0;i<list.length();i++)if(list.optJSONObject(i)!=null&&recordRequest!=null&&recordRequest.id.equals(list.optJSONObject(i).optString("id")))index=i;
  JSONObject next=list.optJSONObject((index+1)%list.length());if(next==null)return;RecordRequest selected=RecordRequest.find(conversation,next.optString("id"));if(selected==null)return;
  saveDraft();recordRequest=selected;recordEnded=false;recordUnavailable=false;replyError="";prompt=selected.prompt(selections.getOrDefault(draftKey(),0));answer.setText(drafts.getOrDefault(draftKey(),""));replyNonce=submissions.getOrDefault(draftKey(),"");submitting=!replyNonce.isEmpty();live=true;render();
 }
 void scheduleStatus(){
  if(replyStatus!=null||replyTimeout==null||replyNonce.isEmpty()||!submitting)return;
  replyStatus=()->{replyStatus=null;if(replyTimeout!=null&&submitting&&!replyNonce.isEmpty()&&activity.inputReady())activity.send(MainActivity.obj("op","requestStatus","request",replyNonce,"id",sessionId));};
  activity.ui.postDelayed(replyStatus,replyQueries++==0?1500:5000);
 }
 void cancelStatus(){if(replyStatus!=null){activity.ui.removeCallbacks(replyStatus);replyStatus=null;}}
 void cancelReplyTimeout(){cancelStatus();if(replyTimeout!=null){activity.ui.removeCallbacks(replyTimeout);replyTimeout=null;}}
 void showPopup(){
  if(dialog!=null&&dialog.isShowing())return;
  expanded=true;rendered="";render();
  if(overlay.getParent()!=null)((android.view.ViewGroup)overlay.getParent()).removeView(overlay);
  Dialog popup=new Dialog(activity);popup.requestWindowFeature(Window.FEATURE_NO_TITLE);popup.setContentView(overlay);dialog=popup;
  Window window=popup.getWindow();window.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));
  window.addFlags(WindowManager.LayoutParams.FLAG_DIM_BEHIND);
  window.setDimAmount(.65f);window.setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE|WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN);
  window.setDecorFitsSystemWindows(false);
  popup.setOnDismissListener(d->{if(dialog==popup){hideAnswerKeyboard();dialog=null;expanded=false;rendered="";card.setVisibility(GONE);}});
  popup.show();window.setLayout(-1,-1);card.requestFocus();overlay.requestApplyInsets();
 }
 void hideAnswerKeyboard(){if(dialog!=null&&dialog.isShowing()){((InputMethodManager)activity.getSystemService(android.content.Context.INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(answer.getWindowToken(),0);card.requestFocus();}}
 void dismissPopup(){saveDraft();Dialog popup=dialog;if(popup!=null){hideAnswerKeyboard();dialog=null;popup.dismiss();}expanded=false;rendered="";card.setVisibility(GONE);setVisibility(pending?VISIBLE:GONE);}
 void sendKey(String key,boolean shift){
  if(!activity.inputReady()||!sessionId.equals(activity.selectedId))return;
  activity.send(MainActivity.obj("op","key","key",key,"shift",shift));
 }
 void cancelUnlock(){if(unlock!=null){activity.ui.removeCallbacks(unlock);unlock=null;}}
 void cancelDismiss(){if(dismissPending!=null){activity.ui.removeCallbacks(dismissPending);dismissPending=null;}}
 @Override protected void onDetachedFromWindow(){clear();super.onDetachedFromWindow();}
 void clear(){saveDraft();recordEnded=false;cancelReplyTimeout();recordRequest=null;conversation=null;replyNonce="";replyError="";requestOpenKey="";recordUnavailable=false;cancelDismiss();cancelUnlock();dismissPopup();sessionId="";pending=false;queued=false;live=false;opening=false;submitting=false;awaiting=-1;prompt=null;rendered="";shownQuestion="";fallback="";answer.setText("");setVisibility(GONE);card.setVisibility(GONE);}
}
