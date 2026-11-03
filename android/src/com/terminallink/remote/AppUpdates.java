package com.terminallink.remote;
import android.content.*;
import android.net.Uri;
import android.provider.Settings;
import android.view.View;
import android.widget.*;
import org.json.JSONObject;

// Activity presentation only; downloads survive Activity recreation.
final class AppUpdates {
 final MainActivity activity;final LinearLayout banner;final TextView label;final Button action;
 boolean waitingPermission,supported;
 AppUpdates(MainActivity a){activity=a;banner=a.row();banner.setVisibility(View.GONE);label=a.label("",12,a.FG);banner.addView(label,new LinearLayout.LayoutParams(0,a.dp(44),1));action=a.button("업데이트",v->clicked());action.setTag("app-update-action");banner.addView(action,new LinearLayout.LayoutParams(a.dp(100),a.dp(44)));}
 AutoUpdates model(){return activity.link==null?null:activity.link.updates;}
 void connected(boolean enabled){supported=enabled;render();}
 void check(){AutoUpdates u=model();if(u==null||!activity.ready){toast("PC에 연결한 뒤 확인하세요.");return;}if(u.readyFile!=null){install();return;}u.check();}
 void clicked(){AutoUpdates u=model();if(u==null)return;if(u.readyFile!=null)install();else if(u.offer!=null)u.get(true);else check();}
 void message(JSONObject ignored){render();}
 void close(){}
 void disconnected(){supported=false;render();}
 void render(){AutoUpdates u=model();if(u==null){banner.setVisibility(View.GONE);return;}
  boolean visible=u.offer!=null||u.readyFile!=null||u.busy()||!u.status.isEmpty();banner.setVisibility(visible?View.VISIBLE:View.GONE);
  label.setText(!u.status.isEmpty()?u.status:u.readyOffer!=null?"새 버전 "+u.readyOffer.versionName+" · 앱을 사용하지 않을 때 자동 설치":u.offer!=null?"새 버전 "+u.offer.versionName+" 사용 가능":"");
  action.setText(u.readyFile!=null?"지금 설치":u.busy()?"받는 중":"다시 받기");action.setEnabled(!u.busy());
 }
 void toast(String text){Toast.makeText(activity,text,Toast.LENGTH_LONG).show();}
 void resume(){if(waitingPermission){waitingPermission=false;if(activity.getPackageManager().canRequestPackageInstalls())install();}render();}
 void install(){AutoUpdates u=model();if(u==null||u.readyFile==null||u.readyOffer==null)return;
  if(!activity.getPackageManager().canRequestPackageInstalls()){
   waitingPermission=true;toast("TerminalLink의 ‘이 출처 허용’을 켠 뒤 돌아오세요.");
   try{activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+activity.getPackageName())));}catch(Exception e){waitingPermission=false;toast("설정에서 TerminalLink의 앱 설치를 허용하세요.");}return;
  }
  if(!activity.resumed)return;
  if(activity.link.transferring()){toast("전송이 끝난 뒤 설치하세요.");return;}
  if(activity.compose.getText().length()>0||activity.attachments.hasPhotos()||activity.attachments.loading||activity.actions.expanded||activity.actions.answer.getText().length()>0){toast("작성 중인 글과 요청 답변을 마친 뒤 설치하세요.");return;}
  u.manualInstall=true;activity.prefs.edit().putString("update-attempted",u.readyOffer.sha256).apply();
  try{activity.startActivity(new Intent(activity,UpdateInstallActivity.class));}catch(Exception e){u.manualInstall=false;toast("설치 화면을 열지 못했습니다.");}
 }
}
