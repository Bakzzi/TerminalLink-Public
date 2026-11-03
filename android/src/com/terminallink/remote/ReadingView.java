package com.terminallink.remote;
import android.graphics.Typeface;
import android.text.*;
import android.text.style.*;
import android.view.*;
import android.widget.*;
import org.json.*;

// Device-local typography. No PC pixel dimensions or terminal columns affect layout.
final class ReadingView extends LinearLayout {
 final MainActivity activity;final ScrollView scroll;final LinearLayout content,readNotice;final TextView body,readNoticeText;final TableTranscript tables;final Button conversationButton,terminalButton,retryButton;
 String terminalText="";JSONArray messages=null,originalMessages=null;boolean conversationMode=false,modeChosen=false,earlierOmitted=false,newConversation=false,startPending=false;
 String thread="",session="",renderedThread="",renderedSession="",source="record",renderedSource="record";final java.util.ArrayList<long[]> answerRanges=new java.util.ArrayList<>();
 // Keep only the last displayed values; JSON frames may be new objects with identical content.
 String[] renderedFields;long[] renderedSequences;String renderedTerminal="";
 boolean readOnly;String readError=ConversationStatus.error(null),renderedError="";
 boolean hasRendered,renderedConversation,renderedOmitted;
 boolean sameContent(){
  if(!hasRendered||!source.equals(renderedSource)||conversationMode!=renderedConversation||!thread.equals(renderedThread)||!session.equals(renderedSession))return false;
  if(!conversationMode)return terminalText.equals(renderedTerminal);
  if(messages==null||renderedFields==null||messages.length()!=renderedSequences.length||earlierOmitted!=renderedOmitted)return false;
  for(int i=0;i<messages.length();i++){
   JSONObject m=messages.optJSONObject(i);int offset=i*3;
   if(m==null){if(renderedFields[offset]!=null)return false;continue;}
   if(!m.optString("text").equals(renderedFields[offset])||!m.optString("role").equals(renderedFields[offset+1])
     ||!m.optString("phase").equals(renderedFields[offset+2])||m.optLong("sequence")!=renderedSequences[i])return false;
  }
  return true;
 }
 void rememberContent(){
  hasRendered=true;renderedError=readError;renderedSource=source;renderedConversation=conversationMode;renderedOmitted=earlierOmitted;renderedTerminal=conversationMode?"":terminalText;
  if(!conversationMode||messages==null){renderedFields=null;renderedSequences=null;return;}
  renderedFields=new String[messages.length()*3];renderedSequences=new long[messages.length()];
  for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(m==null)continue;
   renderedFields[i*3]=m.optString("text");renderedFields[i*3+1]=m.optString("role");renderedFields[i*3+2]=m.optString("phase");renderedSequences[i]=m.optLong("sequence");
  }
 }
 boolean readScheduled;final Runnable readVisible=()->{readScheduled=false;acknowledgeVisible();};
 void pauseReading(){removeCallbacks(readVisible);readScheduled=false;}
 void scheduleRead(){if(readScheduled)return;readScheduled=true;postDelayed(readVisible,450);}
 void acknowledgeVisible(){
  if(!readError.isEmpty()||!activity.resumed||activity.link==null||!conversationMode||!isShown()||!activity.hasWindowFocus()||pendingRestore!=null)return;
  if(tables.active()){java.util.HashSet<Long> seen=new java.util.HashSet<>();for(long[] range:answerRanges)if(tables.visible(range))seen.add(range[0]);if(!seen.isEmpty())activity.link.readAnswers(renderedSession,renderedThread,seen);return;}
  if(body.getLayout()==null)return;
  android.graphics.Rect visible=new android.graphics.Rect();if(!body.getLocalVisibleRect(visible))return;
  android.text.Layout layout=body.getLayout();int top=visible.top-body.getTotalPaddingTop(),bottom=visible.bottom-body.getTotalPaddingTop();
  java.util.HashSet<Long> visibleAnswers=new java.util.HashSet<>();
  for(long[] range:answerRanges){int first=layout.getLineTop(layout.getLineForOffset((int)range[1])),last=layout.getLineBottom(layout.getLineForOffset((int)range[2]));
   if(last>top&&first<bottom)visibleAnswers.add(range[0]);
  }
  if(!visibleAnswers.isEmpty())activity.link.readAnswers(renderedSession,renderedThread,visibleAnswers);
 }
 int fontSp;int sourceColumns,sourceRows;
 ViewTreeObserver.OnPreDrawListener pendingRestore;boolean restoreBottom;int restoreY;
 ReadingView(MainActivity a){
  super(a);activity=a;setOrientation(VERTICAL);setBackgroundColor(0xff101925);
  LinearLayout controls=a.row();
  conversationButton=a.button("대화",v->{conversationMode=true;modeChosen=true;render(true);});
  terminalButton=a.button("터미널",v->{conversationMode=false;modeChosen=true;render(true);});
  controls.addView(conversationButton);controls.addView(terminalButton);
  Button request=a.button("요청",v->a.actions.openRequest());request.setTag("request-controls");request.setTooltipText("Codex 질문·승인 요청 열기");controls.addView(request);
  controls.addView(a.button("A−",v->font(-1)));controls.addView(a.button("A+",v->font(1)));controls.addView(a.button("최신 ↓",v->latest()));
  HorizontalScrollView toolbar=new HorizontalScrollView(a);toolbar.setHorizontalScrollBarEnabled(false);toolbar.addView(controls);addView(toolbar,new LayoutParams(-1,a.dp(44)));
  scroll=new ScrollView(a){
   @Override protected void onSizeChanged(int w,int h,int oldw,int oldh){
    // Capture the previous viewport BEFORE IME/rotation changes its height.
    // Content may not update at all while the keyboard is being opened.
    boolean preserve=body!=null&&oldh>0;
    boolean bottom=preserve&&!hasSelection()&&(pendingRestore!=null?restoreBottom:getScrollY()+oldh>=content.getHeight()-activity.dp(72));
    int y=pendingRestore!=null?restoreY:getScrollY();
    super.onSizeChanged(w,h,oldw,oldh);
    if(preserve)restoreAfterLayout(bottom,y);
   }
   @Override public boolean requestChildRectangleOnScreen(View child,android.graphics.Rect rectangle,boolean immediate){
    // TextView resets its collapsed caret during setText. It must not scroll the viewer.
    // Keep Android's normal auto-scroll for an actual text selection/selection handle.
    return body!=null&&hasSelection()&&super.requestChildRectangleOnScreen(child,rectangle,immediate);
   }
  };scroll.setFillViewport(false);scroll.setClipToPadding(false);scroll.setSmoothScrollingEnabled(false);
  scroll.setOnScrollChangeListener((View.OnScrollChangeListener)(v,x,y,oldX,oldY)->scheduleRead());
  scroll.setOnTouchListener((v,event)->{if(event.getActionMasked()==MotionEvent.ACTION_DOWN)cancelRestore();return false;});
  body=new TextView(a);body.setTextColor(a.FG);fontSp=a.prefs.getInt("reading-font",16);body.setTextSize(fontSp);body.setPadding(a.dp(12),a.dp(12),a.dp(12),a.dp(20));body.setLineSpacing(a.dp(5),1f);body.setTextIsSelectable(true);body.setRevealOnFocusHint(false);body.setHorizontallyScrolling(false);body.setBreakStrategy(android.text.Layout.BREAK_STRATEGY_HIGH_QUALITY);body.setTag("reading-body");
  content=new LinearLayout(a);content.setOrientation(VERTICAL);content.addView(body,new LayoutParams(-1,-2));tables=new TableTranscript(this);content.addView(tables.root,new LayoutParams(-1,-2));scroll.addView(content,new ScrollView.LayoutParams(-1,-2));addView(scroll,new LayoutParams(-1,0,1));
  readNotice=a.row();readNotice.setTag("conversation-read-notice");readNoticeText=a.label("",12,0xffffd18a);readNoticeText.setMaxLines(3);readNoticeText.setEllipsize(TextUtils.TruncateAt.END);readNotice.addView(readNoticeText,new LayoutParams(0,-2,1));retryButton=a.button("다시 시도",v->retryConversation());retryButton.setTag("conversation-retry");readNotice.addView(retryButton,new LayoutParams(-2,-2));addView(readNotice,new LayoutParams(-1,-2));clear("PC에 연결하면 터미널 내용이 표시됩니다.");
 }
 void preserveLayout(){if(content==null)return;boolean bottom=pendingRestore!=null?restoreBottom:scroll.getScrollY()+scroll.getHeight()>=content.getHeight()-activity.dp(72);restoreAfterLayout(bottom,pendingRestore!=null?restoreY:scroll.getScrollY());}
 void showRequest(){if(messages!=null&&messages.length()>0){conversationMode=true;modeChosen=true;}render(false);restoreAfterLayout(true,0);}
 void showTerminal(){conversationMode=false;modeChosen=true;render(true);}
 boolean hasSelection(){return body.hasSelection()||(tables!=null&&tables.selected());}
 void latest(){tables.clearSelection();if(body.getText() instanceof Spannable)Selection.removeSelection((Spannable)body.getText());render(true);}
 void font(int delta){fontSp=Math.max(12,Math.min(28,fontSp+delta));preserveLayout();body.setTextSize(fontSp);tables.font();activity.prefs.edit().putInt("reading-font",fontSp).apply();}
 void clear(String message){activity.updateWeekly(null);tables.clear();body.setVisibility(VISIBLE);hasRendered=false;renderedFields=null;renderedSequences=null;renderedTerminal="";cancelRestore();if(activity.actions!=null)activity.actions.clear();thread="";session="";source="record";newConversation=false;startPending=false;readOnly=false;answerRanges.clear();terminalText="";messages=null;modeChosen=false;conversationMode=false;activity.updateInputTools(false);conversationButton.setEnabled(true);readError=ConversationStatus.error(null);readNotice.setVisibility(GONE);body.setText(message);}
 void retryConversation(){if(activity.ready&&activity.link!=null){activity.send(MainActivity.obj("op","conversationRetry","id",activity.selectedId));activity.setStatus("대화 기록을 다시 확인합니다.");}else activity.connect();}
 void failed(String reason){readError=reason;readOnly=true;render(false);}
 void update(JSONObject frame){
  String nextSession=frame.optString("id",activity.selectedId);boolean sameSession=session.equals(nextSession);session=nextSession;terminalText=frame.optString("text");sourceColumns=frame.optInt("columns");sourceRows=frame.optInt("rows");
  if(!sameSession){thread="";source="unavailable";originalMessages=null;messages=null;newConversation=false;startPending=false;earlierOmitted=false;readError=ConversationStatus.error(null);}
  if(frame.has("conversation")){
   JSONObject conversation=frame.optJSONObject("conversation");boolean original=ConversationStatus.available(conversation);newConversation=ConversationStatus.starting(conversation);startPending=newConversation&&conversation.optString("readState").equals("pending");boolean available=original||newConversation;
   readOnly=!available||conversation.optBoolean("readOnly");
   if(available){activity.updateWeekly(conversation);source=startPending?"starting":conversation.optString("source","record");thread=conversation.optString("id");originalMessages=conversation.optJSONArray("messages");messages=activity.link!=null?activity.link.sentMessages.project(session,thread,originalMessages):originalMessages;earlierOmitted=conversation.optBoolean("earlierOmitted");}
   else if(!sameSession||messages==null){source="unavailable";thread="";originalMessages=new JSONArray();messages=originalMessages;earlierOmitted=false;}
   // Retained rows are display-only. readError still revokes record-dependent actions.
   readError=available?"":ConversationStatus.error(conversation);
   if(!modeChosen&&conversation!=null)conversationMode=true;
  }
  conversationButton.setEnabled(true);
  render(false);
 }
 void render(boolean jump){
  activity.updateInputTools(conversationMode);
  readNoticeText.setText(readError);readNotice.setVisibility(conversationMode&&!readError.isEmpty()?VISIBLE:GONE);
  if(!jump&&hasSelection()&&readError.equals(renderedError))return;
  if(sameContent()){if(jump)restoreAfterLayout(true,0);else scheduleRead();return;}
  SpannableStringBuilder text=new SpannableStringBuilder();java.util.ArrayList<long[]> ranges=new java.util.ArrayList<>();java.util.ArrayList<MarkdownTables.Block> tableBlocks=new java.util.ArrayList<>();
  if(conversationMode&&messages!=null){
   if(messages.length()==0)text.append(!readError.isEmpty()?"아직 불러온 대화가 없습니다.\n":newConversation?(startPending?"새 대화의 원본 연결을 확인하고 있습니다.\n":"새 Codex 대화입니다. 아래에 첫 메시지를 입력해 주세요.\n"):"아직 기록된 대화가 없습니다.\n");
   else if(earlierOmitted)text.append("최근 대화가 표시됩니다. 이전 기록은 PC에 보관되어 있습니다.\n\n");
   for(int i=0;i<messages.length();i++){JSONObject m=messages.optJSONObject(i);if(m==null)continue;boolean user=m.optString("role").equals("user");String phase=m.optString("phase");int start=text.length();text.append(user?"나":phase.equals("partial")?"대화 일부":phase.equals("commentary")?"Codex · 진행":"Codex");text.setSpan(new ForegroundColorSpan(user?0xff73d8ce:0xff8fa3ba),start,text.length(),Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);text.setSpan(new StyleSpan(Typeface.BOLD),start,text.length(),Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);text.append("\n");int begin=text.length();String message=m.optString("text");text.append(message);if(!user)for(MarkdownTables.Block block:MarkdownTables.parse(message,source.equals("terminal")?sourceColumns:0)){tableBlocks.add(new MarkdownTables.Block(begin+block.start,begin+block.end,block.rows,block.align));}if(!user&&(phase.equals("final")||phase.equals("final_answer"))&&text.length()>begin)ranges.add(new long[]{m.optLong("sequence"),begin,text.length()-1});text.append("\n\n");}
  }else{text.append(terminalText.isEmpty()?"터미널 출력을 기다리고 있습니다.":terminalText);}
  boolean bottom=jump||(pendingRestore!=null?restoreBottom:scroll.getScrollY()+scroll.getHeight()>=content.getHeight()-activity.dp(72));
  int y=pendingRestore!=null?restoreY:scroll.getScrollY();
  Typeface face=conversationMode?Typeface.DEFAULT:Typeface.MONOSPACE;if(body.getTypeface()!=face)body.setTypeface(face);
  boolean sameSession=thread.equals(renderedThread)&&session.equals(renderedSession);
  if(tableBlocks.isEmpty()){tables.clear();body.setVisibility(VISIBLE);}else{tables.show(text,tableBlocks,sameSession);body.setVisibility(GONE);}
  answerRanges.clear();answerRanges.addAll(ranges);renderedThread=thread;renderedSession=session;
  body.setText(text);rememberContent();
  // A collapsed selection is a caret, not the user's copy selection.
  if(body.getText() instanceof Spannable&&!hasSelection())Selection.removeSelection((Spannable)body.getText());
  conversationButton.setAlpha(conversationMode?1f:.55f);terminalButton.setAlpha(conversationMode?.55f:1f);
  restoreAfterLayout(bottom,y);
 }
 void refreshSent(){if(activity.link!=null&&!thread.isEmpty()&&readError.isEmpty()){messages=activity.link.sentMessages.project(session,thread,originalMessages);render(true);}}
 void cancelRestore(){
  if(pendingRestore!=null){if(scroll.getViewTreeObserver().isAlive())scroll.getViewTreeObserver().removeOnPreDrawListener(pendingRestore);pendingRestore=null;}
 }
 void restoreAfterLayout(boolean bottom,int y){
  cancelRestore();restoreBottom=bottom;restoreY=y;
  pendingRestore=()->{
   boolean toBottom=restoreBottom;int position=restoreY;cancelRestore();
   // scrollTo changes position only. fullScroll also steals focus and resets TextView's caret.
   scroll.scrollTo(0,toBottom?Math.max(0,content.getHeight()-scroll.getHeight()):position);scheduleRead();return true;
  };
  scroll.getViewTreeObserver().addOnPreDrawListener(pendingRestore);scroll.invalidate();
 }
 @Override protected void onDetachedFromWindow(){pauseReading();cancelRestore();super.onDetachedFromWindow();}}
