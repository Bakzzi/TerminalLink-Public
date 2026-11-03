package com.terminallink.remote;
import android.app.*;
import android.content.*;
import org.json.*;
import java.io.*;
import java.util.UUID;
import java.util.concurrent.*;

// One downloader belongs to the connection, not to a recreated Activity. It uses
// the existing authenticated socket and never wakes a parked phone to poll.
final class AutoUpdates {
 final LinkService service;final ExecutorService disk=Executors.newSingleThreadExecutor();
 UpdateApk.Offer offer,readyOffer;File readyFile;UpdateApk.Download download;
 String request,status="";boolean supported,checking,closed,finishing,manualInstall;long lastChunk;
 final Runnable timeout=()->tick();
 AutoUpdates(LinkService s){service=s;disk.execute(()->{
  UpdateApk.Offer saved=null;File file=null;
  try{String value=s.prefs.getString("update-ready","");if(!value.isEmpty()){saved=new UpdateApk.Offer(new JSONObject(value));file=new File(UpdateApk.folder(s),saved.sha256+".apk");UpdateApk.verify(s,file,saved);}}catch(Exception e){s.prefs.edit().remove("update-ready").apply();saved=null;file=null;}
  final UpdateApk.Offer valid=saved;final File apk=file;s.ui.post(()->{if(closed)return;if(valid!=null){readyOffer=valid;readyFile=apk;}changed();tick();});
 });}
 void connected(boolean enabled){supported=enabled;if(enabled)service.send(MainActivity.obj("op","updateCheck"));}
 void check(){checking=true;service.send(MainActivity.obj("op","updateCheck"));}
 void changed(){if(service.viewer!=null&&service.viewer.updates!=null)service.viewer.updates.render();}
 boolean busy(){return request!=null||finishing;}
 void get(boolean manual){
  if(busy()||offer==null||!service.ready||!supported||closed)return;
  if(!manual&&(MainActivity.updateForeground||service.transferring())){schedule();return;}
  if(!manual&&service.prefs.getString("update-failed","").equals(offer.sha256)&&System.currentTimeMillis()-service.prefs.getLong("update-failed-at",0)<3600000)return;
  request=UUID.randomUUID().toString();lastChunk=System.currentTimeMillis();status="업데이트를 자동으로 받고 있습니다…";changed();
  service.send(MainActivity.obj("op","updateGet","request",request,"sha256",offer.sha256));schedule();
 }
 void message(JSONObject j){try{
  String type=j.optString("type");
  if(type.equals("updateInfo")){
   JSONObject value=j.optJSONObject("release");UpdateApk.Offer latest=value==null?null:new UpdateApk.Offer(value);
   offer=latest!=null&&latest.versionCode>UpdateApk.installedVersion(service)?latest:null;
   if(checking){checking=false;android.widget.Toast.makeText(service,offer==null?"현재 최신 버전입니다.":"새 버전 "+offer.versionName+"을 받습니다.",android.widget.Toast.LENGTH_SHORT).show();}
   changed();if(readyFile==null)get(false);return;
  }
  if(request==null||!request.equals(j.optString("request")))return;
  lastChunk=System.currentTimeMillis();
  if(type.equals("updateStart")){
   UpdateApk.Offer incoming=new UpdateApk.Offer(j.getJSONObject("release"));
   if(download!=null||offer==null||!incoming.json().toString().equals(offer.json().toString()))throw new IOException("업데이트 정보가 변경되었습니다.");
   download=new UpdateApk.Download(service,incoming);
  }else if(type.equals("updateChunk")){
   if(download==null||finishing)throw new IOException("잘못된 업데이트 순서입니다.");
   download.append(j.getLong("offset"),j.getString("data"));status="업데이트 받는 중 "+(download.received*100/download.offer.size)+"%";changed();
  }else if(type.equals("updateEnd")){
   if(download==null||finishing)throw new IOException("업데이트가 완성되지 않았습니다.");
   finishing=true;UpdateApk.Download done=download;download=null;String key=request;
   disk.execute(()->{try{File file=done.finish();done.close();service.ui.post(()->{if(closed||!key.equals(request))return;readyOffer=done.offer;readyFile=file;request=null;finishing=false;status="";service.prefs.edit().putString("update-ready",readyOffer.json().toString()).remove("update-failed").apply();changed();tick();service.schedulePark();});}catch(Exception e){done.close();service.ui.post(()->{if(key.equals(request))fail("업데이트 파일 검증에 실패했습니다.");});}});
  }else if(type.equals("updateError"))throw new IOException(j.optString("message","업데이트를 받을 수 없습니다."));
 }catch(Exception e){fail(e.getMessage());}}
 void schedule(){service.ui.removeCallbacks(timeout);if(!closed)service.ui.postDelayed(timeout,1000);}
 void tick(){
  if(closed)return;
  if(request!=null&&!finishing&&System.currentTimeMillis()-lastChunk>20000){fail("다운로드가 중단됐습니다. 연결 후 다시 시도합니다.");return;}
  if(readyFile==null&&!busy()&&offer!=null)get(false);
  if(readyFile!=null&&!manualInstall&&!MainActivity.updateForeground&&!service.transferring()&&safeToReplace(service.viewer)){
   MobileUpdateInstaller.start(service,readyOffer,readyFile);return;
  }
  if(busy()||readyFile!=null)schedule();
 }
 static boolean safeToReplace(MainActivity a){return a==null||!a.resumed&&!a.awaitingActivityResult&&a.compose.getText().length()==0&&(a.attachments==null||!a.attachments.hasPhotos()&&!a.attachments.loading&&!a.attachments.busy)&&(a.actions==null||!a.actions.pending&&!a.actions.expanded&&!a.actions.submitting&&a.actions.answer.getText().length()==0)&&a.creatingRequest.isEmpty()&&a.closingRequest.isEmpty();}
 void fail(String why){if(download!=null)download.close();download=null;request=null;finishing=false;status=why==null?"업데이트를 받지 못했습니다.":why;if(offer!=null)service.prefs.edit().putString("update-failed",offer.sha256).putLong("update-failed-at",System.currentTimeMillis()).apply();changed();service.schedulePark();}
 void disconnected(){supported=false;if(busy())fail("연결이 끊겨 업데이트를 보류했습니다.");}
 void close(){closed=true;service.ui.removeCallbacks(timeout);if(download!=null)download.close();download=null;disk.shutdown();}
}
