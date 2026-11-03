package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.net.Uri;
import android.provider.Settings;
import android.view.View;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.util.UUID;

final class AppUpdates {
 final MainActivity activity;final LinearLayout banner;final TextView label;final Button action;
 UpdateApk.Offer offer,readyOffer;File readyFile;UpdateApk.Download download;
 String request,status="";boolean supported,manualCheck,waitingPermission,installing;long lastChunk;
 AppUpdates(MainActivity a){
  activity=a;banner=a.row();banner.setVisibility(View.GONE);
  label=a.label("",12,a.FG);banner.addView(label,new LinearLayout.LayoutParams(0,a.dp(44),1));
  action=a.button("업데이트",v->clicked());action.setTag("app-update-action");banner.addView(action,new LinearLayout.LayoutParams(a.dp(100),a.dp(44)));
  try{String saved=a.prefs.getString("update-ready","");if(!saved.isEmpty()){readyOffer=new UpdateApk.Offer(new JSONObject(saved));readyFile=new File(UpdateApk.folder(a),readyOffer.sha256+".apk");UpdateApk.verify(a,readyFile,readyOffer);}}catch(Exception e){readyFile=null;readyOffer=null;a.prefs.edit().remove("update-ready").apply();}
  render();
 }
 synchronized void connected(boolean enabled){supported=enabled;render();}
 synchronized void check(){
  if(!activity.ready){toast("PC에 연결한 뒤 확인하세요.");return;}
  if(!supported){toast("PC 연결 프로그램을 1.3.0 이상으로 업데이트하세요.");return;}
  manualCheck=true;activity.send(MainActivity.obj("op","updateCheck"));
 }
 synchronized void clicked(){
  if(readyFile!=null){install();return;}
  if(request!=null||installing)return;
  if(!activity.ready||!supported){toast("PC에 연결한 뒤 업데이트하세요.");return;}
  if(offer==null){check();return;}
  request=UUID.randomUUID().toString();lastChunk=System.currentTimeMillis();status="업데이트를 받고 있습니다…";render();
  activity.send(MainActivity.obj("op","updateGet","request",request,"sha256",offer.sha256));
  activity.ui.postDelayed(this::timeout,1000);
 }
 synchronized void timeout(){
  if(request==null)return;
  if(System.currentTimeMillis()-lastChunk>20000){fail("다운로드가 중단됐습니다. 다시 시도하세요.");return;}
  activity.ui.postDelayed(this::timeout,1000);
 }
 synchronized void message(JSONObject j){
  String type=j.optString("type");
  try{
   if(type.equals("updateInfo")){
    JSONObject value=j.optJSONObject("release");UpdateApk.Offer latest=value==null?null:new UpdateApk.Offer(value);
    offer=latest!=null&&latest.versionCode>UpdateApk.installedVersion(activity)?latest:null;
    if(manualCheck){toast(latest==null?"PC에 등록된 업데이트가 없습니다.":offer==null?"현재 최신 버전입니다.":"새 버전 "+offer.versionName+"을 받을 수 있습니다.");manualCheck=false;}
    render();return;
   }
   if(request==null||!request.equals(j.optString("request")))return;
   lastChunk=System.currentTimeMillis();
   if(type.equals("updateStart")){
    UpdateApk.Offer incoming=new UpdateApk.Offer(j.getJSONObject("release"));
    if(download!=null||offer==null||!incoming.json().toString().equals(offer.json().toString()))throw new IOException("업데이트 정보가 변경되었습니다. 다시 확인하세요.");
    download=new UpdateApk.Download(activity,incoming);
   }else if(type.equals("updateChunk")){
    if(download==null)throw new IOException("업데이트 다운로드가 시작되지 않았습니다.");
    download.append(j.getLong("offset"),j.getString("data"));status="업데이트 받는 중 "+(download.received*100/download.offer.size)+"%";render();
   }else if(type.equals("updateEnd")){
    if(download==null)throw new IOException("업데이트 다운로드가 시작되지 않았습니다.");
    readyFile=download.finish();readyOffer=download.offer;download.close();download=null;request=null;status="";
    activity.prefs.edit().putString("update-ready",readyOffer.json().toString()).apply();render();
    activity.ui.post(()->{synchronized(AppUpdates.this){if(activity.resumed)install();}});
   }else if(type.equals("updateError"))throw new IOException(j.optString("message","업데이트를 받을 수 없습니다."));
  }catch(Exception e){fail(e.getMessage()==null?"업데이트 검증에 실패했습니다.":e.getMessage());}
 }
 synchronized void close(){if(download!=null)download.close();download=null;request=null;manualCheck=false;}
 synchronized void disconnected(){supported=false;manualCheck=false;if(request!=null)fail("연결이 끊겼습니다. 다시 연결한 뒤 업데이트하세요.");else render();}
 synchronized void fail(String why){if(download!=null)download.close();download=null;request=null;status=why;render();}
 synchronized void render(){Runnable draw=()->{synchronized(AppUpdates.this){
  boolean visible=offer!=null||readyFile!=null||request!=null||!status.isEmpty();banner.setVisibility(visible?View.VISIBLE:View.GONE);
  label.setText(!status.isEmpty()?status:readyOffer!=null?"새 버전 "+readyOffer.versionName+" · 설치 준비 완료":offer!=null?"새 버전 "+offer.versionName+" 사용 가능":"");
  action.setText(installing?"설치 준비":readyFile!=null?"설치":request!=null?"받는 중":"업데이트");
  action.setEnabled(!installing&&request==null&&(readyFile!=null||activity.ready));
 }};if(android.os.Looper.myLooper()==activity.ui.getLooper())draw.run();else activity.ui.post(draw);}
 void toast(String text){activity.ui.post(()->Toast.makeText(activity,text,Toast.LENGTH_LONG).show());}
 synchronized void resume(){
  if(installing){installing=false;status="";}
  if(waitingPermission){waitingPermission=false;if(activity.getPackageManager().canRequestPackageInstalls())install();}
  render();
 }
 synchronized void install(){
  if(readyFile==null||readyOffer==null||installing)return;
  if(!activity.getPackageManager().canRequestPackageInstalls()){
   waitingPermission=true;toast("TerminalLink의 ‘이 출처 허용’을 켠 뒤 돌아오세요.");
   try{activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+activity.getPackageName())));}
   catch(Exception e){waitingPermission=false;toast("설정에서 TerminalLink의 앱 설치를 허용하세요.");}
   return;
  }
  if(!activity.resumed)return;
  installing=true;status="설치 화면을 여는 중…";render();
  try{activity.startActivity(new Intent(activity,UpdateInstallActivity.class));}
  catch(Exception e){installing=false;fail("설치 화면을 열지 못했습니다: "+e.getMessage());}
 }
}
