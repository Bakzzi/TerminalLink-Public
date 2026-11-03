package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.content.pm.PackageManager;
import android.graphics.*;
import android.os.*;
import android.text.InputType;
import android.view.*;
import android.view.inputmethod.InputMethodManager;
import android.widget.*;
import org.json.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;

public final class MainActivity extends Activity {
 static volatile boolean updateForeground;
 boolean awaitingActivityResult;
 @Override public void startActivityForResult(Intent intent,int request){awaitingActivityResult=true;try{super.startActivityForResult(intent,request);}catch(RuntimeException e){awaitingActivityResult=false;throw e;}}
 final Handler ui=new Handler(Looper.getMainLooper());
 ConnectionSettings connectionSettings;ImageAttachments attachments;RemoteFiles remoteFiles;
 LinkService link;boolean serviceBound,refreshOnResume;String replySession="",replyPin="",replyKey="";boolean showReply,requestIntent;String openRequestSession="";
 final SecureClient.Listener serviceListener=new SecureClient.Listener(){
  public void json(JSONObject j){if(j.optString("type").equals("terminalText")&&!resumed){refreshOnResume=true;return;}ui.post(()->{if(link==null)return;client=link.client;wantConnected=link.enabled;connect.setText(wantConnected?"연결 끊기":"연결");if(j.optString("type").startsWith("update"))updates.message(j);else message(j,link.host,link.port);});}
  public void frame(byte[] bytes){ui.post(()->setStatus("PC 프로그램을 업데이트하세요."));}
  public void ended(String why){ui.post(()->{client=link==null?null:link.client;ready=false;if(attachments!=null)attachments.disconnected();if(remoteFiles!=null)remoteFiles.disconnected();wantConnected=link!=null&&link.enabled;creatingRequest="";closingRequest="";closingId="";dismissTerminalDialog();updateCreateButton();updates.disconnected();terminal.failed("연결이 끊겨 대화 업데이트를 멈췄습니다. 표시된 대화와 작성 중인 글은 유지됩니다.");setStatus("연결 종료: "+why);connect.setText(wantConnected?"연결 끊기":"연결");});}
 };
 final ServiceConnection serviceConnection=new ServiceConnection(){
  public void onServiceConnected(ComponentName name,IBinder binder){link=((LinkService.LocalBinder)binder).get();client=link.client;wantConnected=link.enabled;link.attach(serviceListener,MainActivity.this);refreshSendState();connect.setText(wantConnected?"연결 끊기":"연결");openReply();}
  public void onServiceDisconnected(ComponentName name){link=null;client=null;ready=false;refreshSendState();updateCreateButton();}
 };
 volatile SecureClient client;AppUpdates updates;
 android.content.SharedPreferences prefs;
 EditText address,port,compose;
 TextView status,deviceLabel,title,weeklyUsage;
 Button connect,newTerminal,closeTerminal;boolean canCreateTerminal=false,canCloseTerminal=false;String creatingRequest="",closingRequest="",closingId="";AlertDialog terminalDialog;
 Spinner windowList;UnreadTabs unreadTabs;Button submit;
 ReadingView terminal;ActionControls actions;
 LinearLayout root,header,entry,shortcutBar; TextView hint; boolean fullscreen=false;
 String pin="",pairToken="",selectedId="";
 String[] windowIds=new String[0],windowLabels=new String[0];
 boolean ready=false,wantConnected=false,resumed=false,settingSpinner=false;
 final AtomicReference<Bitmap> pendingImage=new AtomicReference<>();
 boolean framePosted=false;
 final int BG=Color.rgb(12,18,29),FG=Color.rgb(223,233,245),MUTED=Color.rgb(143,163,186);
 @Override public void onCreate(Bundle b){
  super.onCreate(b);updateForeground=true;getWindow().setDecorFitsSystemWindows(false);
  getWindow().setStatusBarColor(BG);getWindow().setNavigationBarColor(BG);
  prefs=getSharedPreferences("connection",MODE_PRIVATE);pin=prefs.getString("pin","");
  remoteFiles=new RemoteFiles(this);
  root=new LinearLayout(this);root.setOrientation(LinearLayout.VERTICAL);root.setBackgroundColor(BG);root.setPadding(dp(14),dp(10),dp(14),dp(8));
  root.setOnApplyWindowInsetsListener((v,insets)->{android.graphics.Insets i=insets.getInsets(WindowInsets.Type.systemBars());v.setPadding(dp(14)+i.left,dp(8)+i.top,dp(14)+i.right,dp(8)+Math.max(i.bottom,insets.getInsets(WindowInsets.Type.ime()).bottom));return insets;});
  header=row();title=label("TerminalLink",20,FG);title.setTypeface(null,Typeface.BOLD);header.addView(title,new LinearLayout.LayoutParams(0,dp(40),1));weeklyUsage=label("--%",12,MUTED);weeklyUsage.setGravity(Gravity.CENTER);weeklyUsage.setPadding(dp(4),0,dp(8),0);weeklyUsage.setContentDescription("Codex 주간 사용률 확인 중");header.addView(weeklyUsage,new LinearLayout.LayoutParams(dp(48),dp(40)));header.addView(button("파일함",v->remoteFiles.show()));header.addView(button("연결 설정",v->connectionSettings.show()));root.addView(header);
  status=label("PC에서 터미널을 선택하고 연결 대기를 시작하세요.",12,MUTED);root.addView(status);
  updates=new AppUpdates(this);root.addView(updates.banner);connectionSettings=new ConnectionSettings(this);
  windowList=new Spinner(this);windowList.setBackgroundTintList(android.content.res.ColorStateList.valueOf(MUTED));
  windowList.setOnItemSelectedListener(new android.widget.AdapterView.OnItemSelectedListener(){
   public void onNothingSelected(android.widget.AdapterView<?> p){}
   public void onItemSelected(android.widget.AdapterView<?> p,View v,int pos,long id){if(!settingSpinner&&pos>=0&&pos<windowIds.length&&!windowIds[pos].equals(selectedId)){selectedId=windowIds[pos];terminal.clear("터미널 내용을 연결하고 있습니다.");send(obj("op","select","id",selectedId));}}
  });LinearLayout sessionRow=row();sessionRow.addView(windowList,new LinearLayout.LayoutParams(0,dp(42),1));
  unreadTabs=new UnreadTabs(this,sessionRow);
  newTerminal=button("+",v->createTerminal());newTerminal.setTag("new-terminal");newTerminal.setTextSize(24);newTerminal.setContentDescription("새 터미널 추가");newTerminal.setTooltipText("PC에 새 터미널 탭 추가");newTerminal.setEnabled(false);sessionRow.addView(newTerminal,new LinearLayout.LayoutParams(dp(48),dp(42)));
  closeTerminal=button("닫기",v->closeTerminal());closeTerminal.setTag("close-terminal");closeTerminal.setContentDescription("선택한 PC 탭 닫기");closeTerminal.setEnabled(false);sessionRow.addView(closeTerminal,new LinearLayout.LayoutParams(dp(54),dp(42)));root.addView(sessionRow);
  terminal=new ReadingView(this);
  terminal.body.setOnKeyListener((v,k,event)->terminal.body.hasSelection()?false:hardware(k,event));
  root.addView(terminal,new LinearLayout.LayoutParams(-1,0,1));
  actions=new ActionControls(this);root.addView(actions);
  shortcutBar=row();shortcutBar.setTag("shortcut-bar");
  Button keyboard=button("⌨",v->showKeyboard());keyboard.setTextSize(19);keyboard.setContentDescription("키보드 열기");keyboard.setTooltipText("키보드 열기");
  shortcutBar.addView(keyboard,new LinearLayout.LayoutParams(dp(46),dp(48)));
  HorizontalScrollView tools=new HorizontalScrollView(this);tools.setTag("shortcut-scroll");tools.setHorizontalScrollBarEnabled(false);
  LinearLayout keys=row();
  addKey(keys,"Esc","ESC",false);
  addKey(keys,"Tab","TAB",false);
  addKey(keys,"↑","UP",false);addKey(keys,"↓","DOWN",false);
  keys.addView(shortcutText("/","/","Codex 명령 메뉴"));
  keys.addView(shortcutText("@","@","Codex 파일 검색"));
  addKey(keys,"Ctrl+R","R",true);
  Button shiftEnter=button("Shift+Enter",v->{if(flushDraftForShortcut())send(obj("op","key","key","ENTER","shift",true));});shiftEnter.setTag("shortcut-shift-enter");shiftEnter.setTooltipText("PC에 Shift+Enter 전달");keys.addView(shiftEnter);
  addKey(keys,"⌫","BACKSPACE",false);addKey(keys,"←","LEFT",false);addKey(keys,"→","RIGHT",false);
  addKey(keys,"Ctrl+C","C",true);addKey(keys,"Ctrl+A","A",true);addKey(keys,"Ctrl+L","L",true);
  addKey(keys,"Home","HOME",false);addKey(keys,"End","END",false);addKey(keys,"PgUp","PAGEUP",false);addKey(keys,"PgDn","PAGEDOWN",false);
  keys.addView(button("전체/복귀",v->full(!fullscreen)));
  keys.addView(button("목록 갱신",v->send(obj("op","list"))));
  addKey(keys,"Ctrl+D","D",true);addKey(keys,"Ctrl+Z","Z",true);
  tools.addView(keys);shortcutBar.addView(tools,new LinearLayout.LayoutParams(0,dp(48),1));
  Button enter=button("Enter ↵",v->sendEnter());enter.setTag("shortcut-enter");enter.setTextSize(14);enter.setTypeface(null,Typeface.BOLD);enter.setTextColor(BG);
  enter.setBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.rgb(115,216,206)));
  enter.setContentDescription("PC에 Enter 키 전달");enter.setTooltipText("Enter 키만 전달 · 작성 중인 글은 유지");
  LinearLayout.LayoutParams enterSize=new LinearLayout.LayoutParams(dp(86),dp(48));enterSize.leftMargin=dp(6);shortcutBar.addView(enter,enterSize);
  root.addView(shortcutBar,new LinearLayout.LayoutParams(-1,dp(48)));
  entry=row();compose=input("한글·명령 입력", "");compose.setSingleLine(false);compose.setMaxLines(3);
  compose.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_FLAG_MULTI_LINE|InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
  attachments=new ImageAttachments(this);root.addView(attachments.preview);entry.addView(attachments.add,new LinearLayout.LayoutParams(dp(46),dp(50)));
  entry.addView(compose,new LinearLayout.LayoutParams(0,dp(50),1));
  submit=button("입력",v->submitDraft());submit.setTag("submit-input");submit.setContentDescription("입력: 작성한 글 전송");submit.setTooltipText("작성한 글을 보내고 제출");entry.addView(submit,new LinearLayout.LayoutParams(dp(66),dp(50)));
  root.addView(entry);
  hint=label("글자를 길게 눌러 복사 · 질문·승인은 요청 버튼",10,MUTED);root.addView(hint);
  setContentView(root);attachments.accept(getIntent(),true);readReplyIntent(getIntent());serviceBound=bindService(new Intent(this,LinkService.class),serviceConnection,BIND_AUTO_CREATE);
  if(pin.isEmpty()||android.service.quicksettings.TileService.ACTION_QS_TILE_PREFERENCES.equals(getIntent().getAction()))ui.post(()->{if(!isFinishing())connectionSettings.show();});
 }
 int dp(int v){return (int)(v*getResources().getDisplayMetrics().density+.5f);}
 LinearLayout row(){LinearLayout r=new LinearLayout(this);r.setOrientation(LinearLayout.HORIZONTAL);r.setGravity(Gravity.CENTER_VERTICAL);return r;}
 TextView label(String t,int size,int color){TextView v=new TextView(this);v.setText(t);v.setTextSize(size);v.setTextColor(color);v.setPadding(0,dp(3),0,dp(3));return v;}
 EditText input(String hint,String value){EditText e=new EditText(this);e.setTextColor(FG);e.setHintTextColor(MUTED);e.setTextSize(14);e.setHint(hint);e.setText(value);e.setSingleLine(true);e.setSelectAllOnFocus(false);return e;}
 Button button(String text,View.OnClickListener click){Button b=new Button(this);b.setText(text);b.setTextSize(12);b.setAllCaps(false);b.setTextColor(FG);b.setMinWidth(dp(44));b.setMinimumWidth(dp(44));b.setPadding(dp(8),0,dp(8),0);b.setBackgroundTintList(android.content.res.ColorStateList.valueOf(Color.rgb(32,47,67)));b.setOnClickListener(click);return b;}
 void updateInputTools(boolean conversation){
  if(shortcutBar==null)return;
  int visibility=conversation?View.GONE:View.VISIBLE;
  if(shortcutBar.getVisibility()!=visibility){if(terminal!=null)terminal.preserveLayout();shortcutBar.setVisibility(visibility);}
 }
 void showKeyboard(){full(false);compose.requestFocus();((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).showSoftInput(compose,InputMethodManager.SHOW_IMPLICIT);}
 Button shortcutText(String label,String text,String description){Button b=button(label,v->{if(flushDraftForShortcut())send(obj("op","text","text",text));});b.setTag("shortcut-text-"+text);b.setTooltipText(description);b.setContentDescription(description);return b;}
 void addKey(LinearLayout r,String text,String key,boolean ctrl){Button b=button(text,v->{if(actions!=null&&actions.expanded&&actions.pending&&!ctrl){actions.sendKey(key,false);return;}if(key.equals("TAB")&&!flushDraftForShortcut())return;terminal.showTerminal();send(obj("op","key","key",key,"ctrl",ctrl));});b.setTag("shortcut-key-"+(ctrl?"CTRL-":"")+key);if(key.equals("TAB"))b.setTooltipText("작성한 내용 + Tab · Codex 작업 중 후속 입력 대기");if(ctrl&&key.equals("R"))b.setTooltipText("Codex 입력 이력 검색");r.addView(b);}
 static JSONObject obj(Object... fields){JSONObject o=new JSONObject();try{for(int i=0;i<fields.length;i+=2)o.put((String)fields[i],fields[i+1]);}catch(Exception ignored){}return o;}
 void setStatus(String s){if(!android.text.TextUtils.equals(status.getText(),s))status.setText(s);if(connectionSettings!=null)connectionSettings.refresh();}
 void send(JSONObject o){String op=o.optString("op");if(attachments!=null&&attachments.busy&&(op.equals("text")||op.equals("key")))return;if((!creatingRequest.isEmpty()||!closingRequest.isEmpty())&&(op.equals("text")||op.equals("key"))){setStatus("탭 작업 중입니다. 잠시 기다려주세요.");return;}if(ready&&link!=null)link.send(o);}
 void updateCreateButton(){
  boolean idle=(attachments==null||!attachments.busy)&&creatingRequest.isEmpty()&&closingRequest.isEmpty()&&(terminalDialog==null||!terminalDialog.isShowing());
  if(newTerminal!=null){newTerminal.setEnabled(ready&&canCreateTerminal&&!selectedId.isEmpty()&&idle);newTerminal.setText(creatingRequest.isEmpty()?"+":"…");}
  if(closeTerminal!=null){closeTerminal.setEnabled(ready&&canCloseTerminal&&!selectedId.isEmpty()&&idle);closeTerminal.setText(closingRequest.isEmpty()?"닫기":"…");}
  if(windowList!=null)windowList.setEnabled(idle);
 }
 void dismissTerminalDialog(){if(terminalDialog!=null){terminalDialog.dismiss();terminalDialog=null;}}
 void createTerminal(){confirmTerminal(false);}
 void closeTerminal(){confirmTerminal(true);}
 void confirmTerminal(boolean closing){
  if(terminalDialog!=null&&terminalDialog.isShowing())return;
  if(!inputReady())return;
  if(closing?!canCloseTerminal:!canCreateTerminal){setStatus(closing?"탭 닫기에는 PC 프로그램 1.5.0 이상이 필요합니다.":"새 탭 추가에는 PC 프로그램 1.4.0 이상이 필요합니다.");return;}
  final String target=selectedId;final SecureClient connection=client;
  int index=java.util.Arrays.asList(windowIds).indexOf(target);
  String name=index>=0?windowLabels[index]:"선택한 터미널";
  terminalDialog=new AlertDialog.Builder(this).setTitle(closing?"탭을 닫을까요?":"새 탭을 만들까요?")
   .setMessage(closing?name+"\n\n이 PC 탭에서 실행 중인 작업도 종료됩니다. 저장하지 않은 내용은 사라질 수 있어요.":"선택한 PC 창의 기본 프로필로 새 탭을 만들고 모바일에서 선택합니다.")
   .setNegativeButton("취소",null)
   .setPositiveButton(closing?"탭 닫기":"만들기",(d,w)->{
    if(!resumed||client!=connection||!ready||!selectedId.equals(target)||!java.util.Arrays.asList(windowIds).contains(target)){
     setStatus("연결 또는 선택한 탭이 변경되었습니다. 다시 확인하세요.");return;
    }
    if(!creatingRequest.isEmpty()||!closingRequest.isEmpty())return;
    if(closing){
     closingId=target;closingRequest=UUID.randomUUID().toString();updateCreateButton();setStatus("선택한 PC 탭을 닫고 있습니다…");
     send(obj("op","closeTerminal","request",closingRequest,"id",target,"confirmed",true));
    }else{
     creatingRequest=UUID.randomUUID().toString();updateCreateButton();setStatus("PC에 새 터미널 탭을 열고 있습니다…");
     send(obj("op","createTerminal","request",creatingRequest,"source",target));
    }
   }).create();
  terminalDialog.setOnDismissListener(d->{terminalDialog=null;updateCreateButton();});
  terminalDialog.show();updateCreateButton();
 }
 boolean inputReady(){
  if(link!=null&&link.messageSend.busy){setStatus("메시지를 전송하고 있습니다.");return false;}
  if(attachments!=null&&attachments.busy){setStatus("첨부를 보내고 있습니다. 잠시 기다려 주세요.");return false;}
  if(!creatingRequest.isEmpty()||!closingRequest.isEmpty()){setStatus("탭 작업 중입니다. 잠시 기다려주세요.");return false;}
  if(!ready||client==null){setStatus("먼저 PC에 연결하세요.");return false;}
  if(selectedId.isEmpty()){setStatus("터미널을 먼저 선택하세요.");return false;}
  return true;
 }
 void submitDraft(){
  if(attachments!=null&&(attachments.hasPhotos()||attachments.loading)){attachments.send();return;}
  if(!inputReady())return;
  String value=compose.getText().toString();
  if(value.length()>4096){setStatus("한 번에 4096자까지 입력할 수 있습니다.");return;}
  if(value.isEmpty())return;
  if(terminal.conversationMode){
   if(link!=null&&link.messageSend.nativeSubmit()){link.messageSend.start(selectedId,terminal.thread,value,SentEchoes.last(terminal.originalMessages));return;}
   if(link==null||terminal.thread.isEmpty()||!terminal.readError.isEmpty()){setStatus("대화 원본을 확인한 뒤 전송하세요.");return;}
   if(terminal.newConversation){if(terminal.startPending){setStatus("새 대화의 원본 연결을 확인하고 있습니다.");return;}link.messageSend.startNew(selectedId,terminal.thread,value);}
   else link.messageSend.start(selectedId,terminal.thread,value,SentEchoes.last(terminal.originalMessages));
  }else{send(obj("op","text","text",value));send(obj("op","key","key","ENTER"));compose.setText("");}
 }
 void refreshSendState(){
  boolean busy=link!=null&&link.messageSend.busy||attachments!=null&&attachments.busy;
  compose.setEnabled(!busy);if(submit!=null){submit.setEnabled(!busy);submit.setText(busy?"전송 중…":"입력");}
  if(attachments!=null)attachments.add.setEnabled(!busy&&!attachments.loading&&attachments.photos.size()<ImageAttachments.MAX);
  if(link!=null){MessageSend send=link.messageSend;if(send.busy&&compose.length()==0)compose.setText(send.text);if(!send.error.isEmpty())setStatus(send.error);}
 }
 void sentMessage(String session,String thread,String text,boolean pending){
  // Pending sends lock editing. A late ACK after timeout/disconnect cannot
  // identify a newly edited draft even when its text happens to be identical.
  if(pending&&selectedId.equals(session)&&compose.getText().toString().equals(text))compose.setText("");
  if(selectedId.equals(session)&&terminal.thread.equals(thread)){
   terminal.refreshSent();terminal.latest();
  }
  setStatus("메시지를 보냈습니다.");
 }
 void sendEnter(){
  if(inputReady())send(obj("op","key","key","ENTER"));
 }
 boolean flushDraftForShortcut(){
  if(!inputReady())return false;
  String value=compose.getText().toString();if(value.length()>4096){setStatus("한 번에 4096자까지 입력할 수 있습니다.");return false;}
  terminal.showTerminal();
  if(!value.isEmpty()){send(obj("op","text","text",value));compose.setText("");}
  return true;
 }
 boolean hardware(int code,KeyEvent e){
  if(link!=null&&link.messageSend.busy||attachments!=null&&attachments.busy)return true;
  if(!ready)return false;
  if(e.getAction()!=KeyEvent.ACTION_DOWN)return true;
  String key=null;
  switch(code){case KeyEvent.KEYCODE_ENTER:key="ENTER";break;case KeyEvent.KEYCODE_TAB:key="TAB";break;case KeyEvent.KEYCODE_ESCAPE:key="ESC";break;case KeyEvent.KEYCODE_DEL:key="BACKSPACE";break;case KeyEvent.KEYCODE_FORWARD_DEL:key="DELETE";break;case KeyEvent.KEYCODE_DPAD_UP:key="UP";break;case KeyEvent.KEYCODE_DPAD_DOWN:key="DOWN";break;case KeyEvent.KEYCODE_DPAD_LEFT:key="LEFT";break;case KeyEvent.KEYCODE_DPAD_RIGHT:key="RIGHT";break;case KeyEvent.KEYCODE_MOVE_HOME:key="HOME";break;case KeyEvent.KEYCODE_MOVE_END:key="END";break;case KeyEvent.KEYCODE_PAGE_UP:key="PAGEUP";break;case KeyEvent.KEYCODE_PAGE_DOWN:key="PAGEDOWN";break;}
  if(key==null&&(e.isCtrlPressed()||e.isAltPressed())&&code>=KeyEvent.KEYCODE_A&&code<=KeyEvent.KEYCODE_Z)key=String.valueOf((char)('A'+code-KeyEvent.KEYCODE_A));
  if(key!=null){send(obj("op","key","key",key,"ctrl",e.isCtrlPressed(),"alt",e.isAltPressed(),"shift",e.isShiftPressed()));return true;}
  if(code>=KeyEvent.KEYCODE_F1&&code<=KeyEvent.KEYCODE_F12){send(obj("op","key","key","F"+(code-KeyEvent.KEYCODE_F1+1),"ctrl",e.isCtrlPressed(),"alt",e.isAltPressed(),"shift",e.isShiftPressed()));return true;}int ch=e.getUnicodeChar();if(ch>0){send(obj("op","text","text",new String(Character.toChars(ch))));return true;}return false;
 }
 void connect(){
  if(!resumed)return;
  if(pin.isEmpty()){wantConnected=false;setStatus("먼저 PC의 QR 또는 등록 코드를 등록하세요.");return;}
  String host=address.getText().toString().trim();int n;
  try{n=Integer.parseInt(port.getText().toString());if(n<1||n>65535||host.isEmpty())throw new Exception();}catch(Exception e){wantConnected=false;setStatus("PC 주소와 포트를 확인하세요.");return;}
  ready=false;creatingRequest="";closingRequest="";closingId="";dismissTerminalDialog();updateCreateButton();connect.setText("연결 끊기");setStatus("PC에 직접 연결하고 있습니다…");
  try{startForegroundService(new Intent(this,LinkService.class).setAction(LinkService.CONNECT).putExtra("host",host).putExtra("port",n).putExtra("pin",pin).putExtra("token",pairToken));}
  catch(Exception e){wantConnected=false;setStatus("연결을 시작하지 못했습니다: "+e.getMessage());}
 }
 void notificationPermission(){
  if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED&&!prefs.getBoolean("notification-permission-asked",false)){
   prefs.edit().putBoolean("notification-permission-asked",true).apply();requestPermissions(new String[]{android.Manifest.permission.POST_NOTIFICATIONS},12);
  }
 }
 void quickSettings(){connectionSettings.showPage("quick");}
 void addQuickTile(){
  if(Build.VERSION.SDK_INT>=33){
   try{
    getSystemService(StatusBarManager.class).requestAddTileService(new ComponentName(this,ConnectionTile.class),
     "TerminalLink",ConnectionTile.icon(this),getMainExecutor(),result->{
      if(result==StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ADDED||result==StatusBarManager.TILE_ADD_REQUEST_RESULT_TILE_ALREADY_ADDED)
       setStatus("빠른 설정에서 TerminalLink 버튼을 눌러 연결을 켜고 끌 수 있습니다.");
      else setStatus("빠른 설정의 편집에서 TerminalLink 버튼을 추가할 수 있습니다.");
     });return;
   }catch(RuntimeException ignored){}
  }
  new AlertDialog.Builder(this).setTitle("빠른 설정에 추가")
   .setMessage("화면 위에서 빠른 설정을 펼친 뒤 편집(연필) → TerminalLink 버튼을 찾아 원하는 위치로 옮기세요.")
   .setPositiveButton("확인",null).show();
 }
 void connectionNotificationSettings(){
  try{startActivity(new Intent(android.provider.Settings.ACTION_CHANNEL_NOTIFICATION_SETTINGS)
   .putExtra(android.provider.Settings.EXTRA_APP_PACKAGE,getPackageName())
   .putExtra(android.provider.Settings.EXTRA_CHANNEL_ID,LinkService.CONNECTION_CHANNEL));}
  catch(ActivityNotFoundException e){notificationSettings();}
 }
 void notificationSettings(){connectionSettings.showPage("alerts");}
 void readReplyIntent(Intent intent){if(intent!=null&&intent.hasExtra("reply-session")){requestIntent=intent.getBooleanExtra("request-view",false);replyKey=intent.getStringExtra("reply-key");if(replyKey==null)replyKey="";replySession=intent.getStringExtra("reply-session");replyPin=intent.getStringExtra("reply-pin");if(replySession==null)replySession="";if(replyPin==null)replyPin="";}}
 void openReply(){
  if(replySession.isEmpty()||!resumed||link==null)return;
  if(!replyPin.equals(prefs.getString("pin",""))){replySession="";setStatus("이 알림은 현재 등록된 PC의 답변이 아닙니다.");return;}
  if(!replyKey.isEmpty()){link.acknowledge(replyKey);replyKey="";}
  if(!link.enabled){wantConnected=true;connect();return;}
  if(!ready)return;
  int index=java.util.Arrays.asList(windowIds).indexOf(replySession);
  if(index<0){replySession="";setStatus("답변이 있던 PC 탭이 닫혔거나 공유가 해제되었습니다.");return;}
  String target=replySession;replySession="";
  if(requestIntent){link.acknowledgeRequest(target);openRequestSession=target;showReply=false;windowList.setSelection(index);if(target.equals(selectedId)&&link.lastFrame!=null&&link.lastFrame.optString("id").equals(target)){updateActions(link.lastFrame);openNotifiedRequest();}return;}
  showReply=true;windowList.setSelection(index);
  if(target.equals(selectedId)&&terminal.messages!=null){terminal.conversationButton.performClick();terminal.latest();showReply=false;}
 }
 void updateActions(JSONObject frame){
  if(!selectedId.equals(frame.optString("id")))return;
  JSONObject current=frame.optJSONObject("conversation");
  if(current==null&&link!=null&&link.lastFrame!=null&&selectedId.equals(link.lastFrame.optString("id")))current=link.lastFrame.optJSONObject("conversation");
  actions.update(frame.optString("title"),frame.optString("text"),current);
 }
 void openNotifiedRequest(){
  if(!resumed||!selectedId.equals(openRequestSession))return;
  openRequestSession="";
  if(actions.pending)actions.openRequest();else setStatus("이 요청은 이미 처리되었거나 종료되었습니다.");
 }
 @Override protected void onNewIntent(Intent intent){super.onNewIntent(intent);setIntent(intent);attachments.accept(intent,true);if(intent.hasExtra("reply-session"))connectionSettings.close();readReplyIntent(intent);openReply();if(android.service.quicksettings.TileService.ACTION_QS_TILE_PREFERENCES.equals(intent.getAction())){full(false);connectionSettings.show();}}
 void updateWeekly(JSONObject conversation){
  JSONObject w=conversation==null?null:conversation.optJSONObject("weeklyUsage");long now=System.currentTimeMillis()/1000;
  double used=w==null?-1:w.optDouble("usedPercent",-1);long reset=w==null?0:w.optLong("resetsAt"),observed=w==null?0:w.optLong("observedAt");
  boolean valid=used>=0&&used<=100&&reset>now&&observed>0&&observed<=now+300;
  String value=valid?String.format(java.util.Locale.ROOT,"%.0f%%",used):"--%";
  weeklyUsage.setText(value);String description=valid?"Codex 주간 사용률 "+value+" · 확인 시각 "+android.text.format.DateFormat.format("MM-dd HH:mm",new java.util.Date(observed*1000)):"Codex 주간 사용률을 확인하지 못했습니다.";
  weeklyUsage.setContentDescription(description);weeklyUsage.setTooltipText(description);weeklyUsage.setOnClickListener(v->Toast.makeText(this,description,Toast.LENGTH_LONG).show());
 }
 void message(JSONObject j,String host,int n){
  String type=j.optString("type");
  if(type.startsWith("shared")){remoteFiles.message(j);return;}
  if(type.equals("notificationResult")){actions.result(j);return;}
  if(type.equals("imageResult")){attachments.message(j);return;}
  if(type.equals("connecting")){if(remoteFiles!=null)remoteFiles.disconnected();ready=false;updateCreateButton();setStatus("PC에 연결하고 있습니다…");return;}
  if(type.equals("ready")){if(j.optInt("protocol")<2){wantConnected=false;terminal.clear("텍스트 연결에는 PC 프로그램 1.2.0 이상이 필요합니다.");setStatus("PC 프로그램 업데이트 필요");return;}ready=true;remoteFiles.uploadSupported=j.optInt("sharedUpload")==1;remoteFiles.connected(j.optInt("sharedFiles")==1);attachments.supported=j.optInt("imageUpload")==1;attachments.filesSupported=j.optInt("fileUpload")==1;attachments.remoteSupported=j.optInt("remoteImageUpload")==1;attachments.localSupported=j.optInt("localImageUpload")==1;attachments.remoteFilesSupported=j.optInt("remoteFileUpload")==1;attachments.localFilesSupported=j.optInt("localFileUpload")==1;canCreateTerminal=j.optInt("createTerminal")==1;canCloseTerminal=j.optInt("closeTerminal")==1;updateCreateButton();updates.connected(j.optInt("updates")==1);if(getResources().getConfiguration().orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE)full(true);if(!pairToken.isEmpty())connectionSettings.close();pairToken="";prefs.edit().putString("host",host).putInt("port",n).putString("pin",pin).apply();deviceLabel.setText("등록된 PC · 기기 인증 완료");setStatus("연결됨 · 터미널 선택");if(resumed&&(prefs.getBoolean("answer-notifications",true)||prefs.getBoolean("request-notifications",true)))ui.postDelayed(()->{if(resumed)notificationPermission();},500);}
  else if(type.equals("windows")){
   JSONArray arr=j.optJSONArray("windows");if(arr==null)return;
   windowIds=new String[arr.length()];String[] labels=new String[arr.length()];windowLabels=labels;
   String desired=j.optString("selectId",selectedId);if(desired.isEmpty()||desired.equals("null"))desired=selectedId;
   int choose=0;for(int i=0;i<arr.length();i++){JSONObject w=arr.optJSONObject(i);windowIds[i]=w.optString("id");labels[i]=w.optString("title");if(windowIds[i].equals(desired))choose=i;}
   settingSpinner=true;ArrayAdapter<String> a=unreadTabs.adapter(labels);windowList.setAdapter(a);windowList.setSelection(choose);settingSpinner=false;
   if(windowIds.length==0){selectedId="";terminal.clear("PC에서 터미널 창을 열거나 공유 설정을 확인하세요.");}
   else{String next=windowIds[choose];if(!next.equals(selectedId)){dismissTerminalDialog();terminal.clear("터미널 내용을 연결하고 있습니다.");}selectedId=next;send(obj("op","select","id",selectedId));}updateCreateButton();openReply();
  }else if(type.equals("terminalCreated")||type.equals("terminalCreateError")){
   if(j.optString("request").equals(creatingRequest)){
    creatingRequest="";updateCreateButton();
    setStatus(type.equals("terminalCreated")?"새 터미널이 추가됐습니다.":j.optString("message"));
   }
  }else if(type.equals("terminalClosed")||type.equals("terminalCloseError")){
   if(j.optString("request").equals(closingRequest)){closingRequest="";closingId="";updateCreateButton();setStatus(type.equals("terminalClosed")?"선택한 PC 탭을 닫았습니다.":j.optString("message"));}
  }else if(type.equals("terminalText")){
   if(!j.optString("id").equals(selectedId))return;
   if(!resumed){refreshOnResume=true;return;}
   terminal.update(j);if(showReply&&terminal.messages!=null){terminal.conversationButton.performClick();terminal.latest();showReply=false;}String name=j.optString("title");updateActions(j);openNotifiedRequest();setStatus(name.isEmpty()?"연결됨 · 텍스트":name);
   int pos=java.util.Arrays.asList(windowIds).indexOf(selectedId);if(pos>=0&&!name.isEmpty()){ArrayAdapter<String> adapter=(ArrayAdapter<String>)windowList.getAdapter();if(adapter!=null&&!name.equals(adapter.getItem(pos))){settingSpinner=true;windowLabels[pos]=name;adapter.notifyDataSetChanged();settingSpinner=false;}}
  }else if(type.equals("selected")){setStatus(j.optString("title"));}
  else if(type.equals("closed")){String id=j.optString("id");if(!id.isEmpty()&&!id.equals(selectedId))return;if(!closingRequest.isEmpty()&&id.equals(closingId))return;dismissTerminalDialog();terminal.clear("PC 터미널이 닫혔습니다.");setStatus(j.optString("message"));selectedId="";updateCreateButton();}
  else if(type.equals("paused")){terminal.failed("PC 연결을 확인하지 못했습니다. 표시된 대화와 작성 중인 글은 유지됩니다.");setStatus(j.optString("message"));}
  else if(type.equals("status"))setStatus(j.optString("message"));
 }
 void full(boolean enabled){fullscreen=enabled;int show=enabled?View.GONE:View.VISIBLE;header.setVisibility(show);status.setVisibility(show);windowList.setVisibility(View.VISIBLE);entry.setVisibility(View.VISIBLE);hint.setVisibility(show);if(enabled)getWindow().getInsetsController().hide(WindowInsets.Type.systemBars());else getWindow().getInsetsController().show(WindowInsets.Type.systemBars());getWindow().getInsetsController().setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);}
@Override public void onConfigurationChanged(android.content.res.Configuration c){super.onConfigurationChanged(c);if(ready)full(c.orientation==android.content.res.Configuration.ORIENTATION_LANDSCAPE);if(connectionSettings!=null)connectionSettings.resize();}
@Override public void onBackPressed(){if(fullscreen){full(false);return;}super.onBackPressed();}
void disconnect(){if(attachments!=null)attachments.disconnected();if(remoteFiles!=null)remoteFiles.disconnected();wantConnected=false;if(link!=null)link.disconnect();client=null;ready=false;creatingRequest="";closingRequest="";closingId="";dismissTerminalDialog();updateCreateButton();if(updates!=null)updates.disconnected();connect.setText("연결");terminal.clear("연결되지 않음");setStatus("연결되지 않음");}
 void forgetRegistration(){new AlertDialog.Builder(this).setTitle("PC 등록을 삭제할까요?").setMessage("이 휴대폰에 저장된 PC 등록을 삭제합니다. 다시 연결하려면 QR이나 코드로 등록해야 합니다.").setPositiveButton("삭제",(d,w)->{wantConnected=false;disconnect();prefs.edit().clear().apply();pin="";pairToken="";address.setText("");port.setText("49443");deviceLabel.setText("등록된 PC 없음");connectionSettings.show();}).setNegativeButton("취소",null).show();}
 void codeDialog(){EditText e=input("tr1: 로 시작하는 등록 코드","");e.setSingleLine(false);new AlertDialog.Builder(this).setTitle("PC 등록 코드").setView(e).setPositiveButton("등록",(d,w)->pair(e.getText().toString().trim())).setNegativeButton("취소",null).show();}
 void pair(String code){
  try{
   if(!code.startsWith("tr1:")||code.length()>4096)throw new Exception();
   byte[] bytes=android.util.Base64.decode(code.substring(4),android.util.Base64.URL_SAFE|android.util.Base64.NO_WRAP);
   JSONObject j=new JSONObject(new String(bytes,java.nio.charset.StandardCharsets.UTF_8));
   String p=j.getString("pin"),t=j.getString("token"),h=j.getString("host");int n=j.getInt("port");
   if(j.getInt("v")!=1||!p.matches("[A-F0-9]{64}")||!t.matches("[A-F0-9]{64}")||h.length()>253||n<1||n>65535)throw new Exception();
   wantConnected=false;disconnect();pin=p;pairToken=t;address.setText(h);port.setText(""+n);
   deviceLabel.setText("PC에서 이 기기 승인 · "+SecureClient.fingerprint().substring(0,16));
   connectionSettings.show();wantConnected=true;connect();
  }catch(Exception e){setStatus("올바른 TerminalLink 등록 코드를 입력하세요.");}
 }
 void scan(){
  if(checkSelfPermission(android.Manifest.permission.CAMERA)!=PackageManager.PERMISSION_GRANTED){requestPermissions(new String[]{android.Manifest.permission.CAMERA},10);return;}
  startActivityForResult(new Intent(this,ScanActivity.class),11);
 }
 @Override public void onRequestPermissionsResult(int code,String[] p,int[] r){super.onRequestPermissionsResult(code,p,r);if(code==10&&r.length>0&&r[0]==PackageManager.PERMISSION_GRANTED)scan();}
 @Override protected void onActivityResult(int request,int result,Intent data){super.onActivityResult(request,result,data);awaitingActivityResult=false;if(request==FileBoxUpload.PICK)remoteFiles.upload.selected(result,data);if((request==ImageAttachments.PICK||request==ImageAttachments.FILE_PICK)&&result==RESULT_OK)attachments.accept(data,false);if(request==11&&result==RESULT_OK&&data!=null)ui.postDelayed(()->pair(data.getStringExtra("code")),150);}
 // Called on the UI thread before posting an answer/request notification.
 boolean viewingSession(String id){
  if(!resumed||id.isEmpty()||!id.equals(selectedId)||terminal==null||!terminal.isShown())return false;
  if(!getSystemService(PowerManager.class).isInteractive()||getSystemService(KeyguardManager.class).isKeyguardLocked())return false;
  if(hasWindowFocus())return true;
  // The request popup owns focus while the same conversation is being answered.
  return actions!=null&&actions.dialog!=null&&actions.dialog.isShowing()&&focused(actions.dialog.getWindow())
   ||terminalDialog!=null&&terminalDialog.isShowing()&&focused(terminalDialog.getWindow());
 }
 static boolean focused(Window window){return window!=null&&window.getDecorView().hasWindowFocus();}
 @Override public void onWindowFocusChanged(boolean focus){super.onWindowFocusChanged(focus);if(focus&&terminal!=null)terminal.scheduleRead();}

 @Override protected void onResume(){super.onResume();resumed=true;updateForeground=true;if(unreadTabs!=null)unreadTabs.refresh();if(link!=null)link.viewState();if(terminal!=null)terminal.scheduleRead();if(updates!=null)updates.resume();if(remoteFiles!=null)remoteFiles.resume();if(link!=null){client=link.client;wantConnected=link.enabled;}if(connectionSettings!=null)connectionSettings.refresh();if(refreshOnResume&&link!=null){refreshOnResume=false;if(link.ready&&link.lastFrame!=null)message(link.lastFrame,link.host,link.port);}openReply();}
 @Override protected void onPause(){resumed=false;updateForeground=false;if(link!=null)link.viewState();if(terminal!=null)terminal.pauseReading();dismissTerminalDialog();if(actions!=null)actions.dismissPopup();super.onPause();}
 @Override protected void onDestroy(){if(unreadTabs!=null)unreadTabs.close();if(remoteFiles!=null)remoteFiles.close();if(attachments!=null)attachments.close();if(connectionSettings!=null)connectionSettings.close();if(link!=null)link.detach(serviceListener);if(serviceBound){unbindService(serviceConnection);serviceBound=false;}if(updates!=null)updates.close();if(actions!=null)actions.clear();if(terminal!=null)terminal.pauseReading();ui.removeCallbacksAndMessages(null);super.onDestroy();}
}
