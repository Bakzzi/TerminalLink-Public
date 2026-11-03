package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.provider.Settings;
import android.text.InputType;
import android.util.TypedValue;
import android.view.*;
import android.widget.*;

final class ConnectionSettings {
 final MainActivity a;
 AlertDialog dialog;
 LinearLayout body;
 ScrollView scroll;
 String page="home";
 TextView state,endpoint,alertSummary;
 final int CARD=Color.rgb(24,35,50),ACCENT=Color.rgb(115,216,206);
 ConnectionSettings(MainActivity a){
  this.a=a;
  a.address=a.input("PC 주소",a.prefs.getString("host",""));
  a.address.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_URI);
  a.port=a.input("포트",String.valueOf(a.prefs.getInt("port",49443)));
  a.port.setInputType(InputType.TYPE_CLASS_NUMBER);
  a.deviceLabel=a.label(a.pin.isEmpty()?"등록된 PC 없음":"등록된 PC · 자동 인증",12,a.MUTED);
  a.connect=a.button("연결",v->{
   if(a.pin.isEmpty()){showPage("register");return;}
   if(a.wantConnected)a.disconnect();else{a.wantConnected=true;a.connect();}
   refresh();
  });
  a.connect.setTag("connection-toggle");
 }
 void show(){showPage("home");}
 void showPage(String next){
  page=next;
  if(dialog==null){
   body=new LinearLayout(a);body.setOrientation(1);body.setPadding(a.dp(20),a.dp(4),a.dp(20),a.dp(8));body.setFocusableInTouchMode(true);
   scroll=new ScrollView(a);scroll.setTag("connection-settings-scroll");scroll.setFillViewport(false);scroll.addView(body);
   dialog=new AlertDialog.Builder(a).setTitle("연결 설정").setView(scroll).setNegativeButton("닫기",null).create();
   dialog.setOnKeyListener((d,key,event)->{
    if(key==KeyEvent.KEYCODE_BACK&&!page.equals("home")){
     if(event.getAction()==KeyEvent.ACTION_UP)showPage("home");
     return true;
    }return false;
   });
   dialog.setOnDismissListener(d->{dialog=null;body=null;scroll=null;state=null;endpoint=null;alertSummary=null;});
  }
  render();
  if(!dialog.isShowing())dialog.show();
  dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setText(page.equals("home")?"닫기":"뒤로");
  dialog.getButton(AlertDialog.BUTTON_NEGATIVE).setOnClickListener(v->{if(page.equals("home"))close();else showPage("home");});
  dialog.getWindow().setBackgroundDrawable(background(a.BG));
  dialog.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
  dialog.getWindow().setSoftInputMode(WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE|WindowManager.LayoutParams.SOFT_INPUT_STATE_ALWAYS_HIDDEN);
  resize();body.requestFocus();scroll.post(()->{if(scroll!=null)scroll.scrollTo(0,0);});refresh();
 }
 void close(){if(dialog!=null)dialog.dismiss();}
 void resize(){if(dialog!=null&&dialog.isShowing()){int width=a.getWindowManager().getCurrentWindowMetrics().getBounds().width();dialog.getWindow().setLayout(Math.min(a.dp(440),width-a.dp(32)),WindowManager.LayoutParams.WRAP_CONTENT);}}
 void add(LinearLayout parent,View view,LinearLayout.LayoutParams size){
  if(view.getParent() instanceof ViewGroup)((ViewGroup)view.getParent()).removeView(view);
  parent.addView(view,size);
 }
 GradientDrawable background(int color){GradientDrawable bg=new GradientDrawable();bg.setColor(color);bg.setCornerRadius(a.dp(12));return bg;}
 void heading(String text){TextView v=a.label(text,12,a.MUTED);v.setPadding(0,a.dp(16),0,a.dp(6));body.addView(v);}
 void help(String text){TextView v=a.label(text,12,a.MUTED);v.setPadding(0,a.dp(6),0,a.dp(10));body.addView(v);}
 LinearLayout group(){LinearLayout v=new LinearLayout(a);v.setOrientation(1);v.setBackground(background(CARD));body.addView(v,new LinearLayout.LayoutParams(-1,-2));return v;}
 void divider(LinearLayout parent){View v=new View(a);v.setBackgroundColor(Color.rgb(43,55,71));LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,a.dp(1));p.leftMargin=a.dp(16);p.rightMargin=a.dp(16);parent.addView(v,p);}
 TextView row(LinearLayout parent,String title,String detail,String tag,Runnable action){
  LinearLayout row=a.row();row.setPadding(a.dp(16),a.dp(10),a.dp(14),a.dp(10));row.setMinimumHeight(a.dp(66));row.setTag(tag);
  TypedValue ripple=new TypedValue();a.getTheme().resolveAttribute(android.R.attr.selectableItemBackground,ripple,true);row.setBackgroundResource(ripple.resourceId);row.setFocusable(true);row.setOnClickListener(v->action.run());
  LinearLayout text=new LinearLayout(a);text.setOrientation(1);TextView name=a.label(title,15,a.FG);text.addView(name);
  TextView subtitle=a.label(detail,12,a.MUTED);if(!detail.isEmpty())text.addView(subtitle);
  row.addView(text,new LinearLayout.LayoutParams(0,-2,1));TextView arrow=a.label("›",23,a.MUTED);row.addView(arrow,new LinearLayout.LayoutParams(a.dp(20),-2));
  parent.addView(row,new LinearLayout.LayoutParams(-1,-2));return subtitle;
 }
 void toggle(LinearLayout parent,String title,String key,String tag){
  Switch v=new Switch(a);v.setText(title);v.setTextSize(15);v.setTextColor(a.FG);v.setPadding(a.dp(16),a.dp(6),a.dp(16),a.dp(6));v.setMinimumHeight(a.dp(56));v.setTag(tag);
  v.setChecked(a.prefs.getBoolean(key,true));v.setOnCheckedChangeListener((button,checked)->{a.prefs.edit().putBoolean(key,checked).apply();if(checked)a.notificationPermission();});
  parent.addView(v,new LinearLayout.LayoutParams(-1,-2));
 }
 void primary(String title,String tag,Runnable action){
  Button button=a.button(title,v->action.run());button.setTag(tag);button.setTextSize(14);
  button.setTextColor(a.BG);button.setBackgroundTintList(android.content.res.ColorStateList.valueOf(ACCENT));
  LinearLayout.LayoutParams p=new LinearLayout.LayoutParams(-1,a.dp(50));p.topMargin=a.dp(12);body.addView(button,p);
 }
 String version(){try{return a.getPackageManager().getPackageInfo(a.getPackageName(),0).versionName;}catch(Exception e){return "";}}
 void render(){
  body.removeAllViews();state=null;endpoint=null;alertSummary=null;
  if(page.equals("home")){
   dialog.setTitle("연결 설정");
   LinearLayout card=group();card.setPadding(a.dp(16),a.dp(14),a.dp(16),a.dp(12));
   state=a.label("",20,a.FG);state.setTypeface(null,Typeface.BOLD);card.addView(state);
   endpoint=a.label("",13,a.MUTED);card.addView(endpoint);
   add(card,a.deviceLabel,new LinearLayout.LayoutParams(-1,-2));
   LinearLayout.LayoutParams action=new LinearLayout.LayoutParams(-1,a.dp(50));action.topMargin=a.dp(10);add(card,a.connect,action);
   heading("설정");
   LinearLayout menu=group();
   row(menu,"PC 관리","연결 주소 · PC 등록","pc-management",()->showPage("pc"));divider(menu);
   alertSummary=row(menu,"알림","답변·요청 알림","notification-preferences",()->showPage("alerts"));divider(menu);
   row(menu,"빠른 설정","패널에서 연결 켜기·끄기","quick-settings",a::quickSettings);divider(menu);
   row(menu,"업데이트 확인","현재 버전 "+version(),"app-update-check",()->{a.updates.check();if(a.ready&&a.updates.supported)close();});
  }else if(page.equals("pc")){
   dialog.setTitle("PC 관리");heading("연결 주소");a.address.setEnabled(!a.wantConnected);a.port.setEnabled(!a.wantConnected);
   LinearLayout inputs=group();inputs.setPadding(a.dp(12),a.dp(8),a.dp(12),a.dp(8));
   inputs.addView(a.label("PC 주소",12,a.MUTED));a.address.setTag("pc-address");add(inputs,a.address,new LinearLayout.LayoutParams(-1,a.dp(48)));
   inputs.addView(a.label("포트",12,a.MUTED));a.port.setTag("pc-port");add(inputs,a.port,new LinearLayout.LayoutParams(-1,a.dp(48)));
   if(a.wantConnected)help("주소를 바꾸려면 먼저 연결을 꺼주세요.");
   if(!a.pin.isEmpty()&&!a.wantConnected)primary("이 주소로 연결","apply-endpoint",()->{show();a.wantConnected=true;a.connect();});
   heading("등록");
   LinearLayout registration=group();row(registration,a.pin.isEmpty()?"PC 등록":"다른 PC 등록","QR 스캔 또는 등록 코드","register-pc",()->showPage("register"));
   if(!a.pin.isEmpty()){divider(registration);TextView remove=row(registration,"PC 등록 삭제","현재 저장된 PC 정보 삭제","forget-pc",a::forgetRegistration);((TextView)((LinearLayout)remove.getParent()).getChildAt(0)).setTextColor(Color.rgb(244,157,155));}
  }else if(page.equals("register")){
   dialog.setTitle("PC 등록");help("PC 프로그램에서 표시한 QR이나 등록 코드를 사용하세요.");
   LinearLayout methods=group();row(methods,"QR로 등록","PC의 QR 코드 스캔","register-qr",()->{close();a.scan();});divider(methods);
   row(methods,"등록 코드 입력","복사한 코드를 붙여넣기","register-code",()->{close();a.codeDialog();});
  }else if(page.equals("alerts")){
   dialog.setTitle("알림");
   LinearLayout alerts=group();toggle(alerts,"최종 답변","answer-notifications","answer-notifications");divider(alerts);toggle(alerts,"질문·승인 요청","request-notifications","request-notifications");
   help("현재 보고 있는 대화는 알리지 않아요. 다른 탭이나 앱 밖에서는 알려줘요.");
   heading("휴대폰 설정");LinearLayout system=group();
   row(system,"연결 알림 숨기기","‘PC 연결 유지’ 항목만 끄기","connection-notification-settings",a::connectionNotificationSettings);divider(system);
   row(system,"앱 알림 설정","알림 권한 · 소리 · 진동","system-notifications",()->a.startActivity(new Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE,a.getPackageName())));divider(system);
   row(system,"백그라운드 사용","알림이 늦을 때 배터리 설정 확인","battery-settings",()->a.startActivity(new Intent(Settings.ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS)));
  }else if(page.equals("quick")){
   dialog.setTitle("빠른 설정");help("와이파이·블루투스가 있는 패널에 TerminalLink 버튼을 추가하세요.");
   LinearLayout card=group();card.setPadding(a.dp(16),a.dp(12),a.dp(16),a.dp(12));
   card.addView(a.label("누르면 연결 켜기·끄기",16,a.FG));card.addView(a.label("꺼짐 · 연결 중 · 연결됨 표시\n길게 누르면 앱 열기",13,a.MUTED));
   primary("빠른 설정에 추가","add-quick-tile",a::addQuickTile);
  }
 }
 void refresh(){
  if(dialog==null||!dialog.isShowing())return;
  if(state!=null){
   boolean registered=!a.pin.isEmpty();state.setText(!registered?"PC 등록 필요":a.ready?"PC 연결됨":a.wantConnected?"PC 연결 중":"연결 꺼짐");
   String host=a.ready&&a.link!=null?a.link.host:a.address.getText().toString().trim();endpoint.setText(registered?host:"처음 한 번 PC를 등록하세요.");
   a.deviceLabel.setVisibility(a.pairToken.isEmpty()?View.GONE:View.VISIBLE);
   a.connect.setText(!registered?"PC 등록":a.wantConnected?"연결 끊기":"연결");
   a.connect.setTextSize(14);a.connect.setTextColor(a.wantConnected?a.FG:a.BG);a.connect.setBackgroundTintList(android.content.res.ColorStateList.valueOf(a.wantConnected?Color.rgb(44,59,78):ACCENT));
  }
  if(alertSummary!=null){boolean answers=a.prefs.getBoolean("answer-notifications",true),requests=a.prefs.getBoolean("request-notifications",true);alertSummary.setText(answers&&requests?"답변·요청 알림 켜짐":answers?"답변 알림 켜짐":requests?"요청 알림 켜짐":"답변·요청 알림 꺼짐");}
 }
}
