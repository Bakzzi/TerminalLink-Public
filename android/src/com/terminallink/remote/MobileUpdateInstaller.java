package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.content.pm.PackageInstaller;
import android.os.Build;
import java.io.*;
import java.util.UUID;

public final class MobileUpdateInstaller extends BroadcastReceiver {
 static final String CHANNEL="app_updates",ACTION="com.terminallink.remote.UPDATE_RESULT";static final int NOTICE=7010;
 static void notice(Context c,String text,PendingIntent action){
  NotificationManager nm=c.getSystemService(NotificationManager.class);NotificationChannel channel=new NotificationChannel(CHANNEL,"앱 업데이트",NotificationManager.IMPORTANCE_DEFAULT);channel.setShowBadge(false);nm.createNotificationChannel(channel);
  if(action==null)action=PendingIntent.getActivity(c,NOTICE,new Intent(c,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
  nm.notify(NOTICE,new Notification.Builder(c,CHANNEL).setSmallIcon(c.getResources().getIdentifier("ic_notification","drawable",c.getPackageName())).setContentTitle("TerminalLink 업데이트").setContentText(text).setContentIntent(action).setAutoCancel(true).setOnlyAlertOnce(true).build());
 }
 static synchronized void start(Context c,UpdateApk.Offer offer,File apk){
  android.content.SharedPreferences prefs=c.getSharedPreferences("connection",Context.MODE_PRIVATE);
  if(prefs.getString("update-attempted","").equals(offer.sha256))return;
  if(Build.VERSION.SDK_INT<31||!c.getPackageManager().canRequestPackageInstalls()){
   if(!prefs.getString("update-notified","").equals(offer.sha256)){prefs.edit().putString("update-notified",offer.sha256).apply();notice(c,"새 버전이 준비됐어요. 눌러서 설치를 허용해 주세요.",null);}return;
  }
  String nonce=UUID.randomUUID().toString();prefs.edit().putString("update-attempted",offer.sha256).putString("update-nonce",nonce).commit();
  Context app=c.getApplicationContext();new Thread(()->{
   PackageInstaller installer=app.getPackageManager().getPackageInstaller();int id=-1;
   try{
    UpdateApk.verify(app,apk,offer);
    PackageInstaller.SessionParams p=new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);p.setAppPackageName(app.getPackageName());p.setSize(apk.length());p.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
    id=installer.createSession(p);prefs.edit().putInt("update-session",id).commit();
    Intent result=new Intent(app,MobileUpdateInstaller.class).setAction(ACTION).setData(android.net.Uri.parse("terminallink://install/"+nonce));
    PendingIntent callback=PendingIntent.getBroadcast(app,id,result,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_MUTABLE);
    try(PackageInstaller.Session session=installer.openSession(id)){
     try(InputStream in=new FileInputStream(apk);OutputStream out=session.openWrite("base.apk",0,apk.length())){byte[] b=new byte[65536];int n;while((n=in.read(b))!=-1)out.write(b,0,n);session.fsync(out);}session.commit(callback.getIntentSender());
    }
   }catch(Exception e){if(id>=0)try{installer.abandonSession(id);}catch(Exception ignored){}notice(app,"자동 설치를 완료하지 못했어요. 눌러서 다시 설치해 주세요.",null);}
  },"TerminalLink-auto-install").start();
 }
 @Override public void onReceive(Context c,Intent i){
  android.content.SharedPreferences prefs=c.getSharedPreferences("connection",Context.MODE_PRIVATE);
  if(Intent.ACTION_MY_PACKAGE_REPLACED.equals(i.getAction())){
   prefs.edit().remove("update-ready").remove("update-session").remove("update-nonce").remove("update-notified").apply();c.getSystemService(NotificationManager.class).cancel(NOTICE);
   if(c.getSharedPreferences("reply-state",Context.MODE_PRIVATE).getBoolean("enabled",false))try{c.startForegroundService(new Intent(c,LinkService.class));}catch(Exception ignored){}return;
  }
  if(!ACTION.equals(i.getAction())||i.getData()==null||!i.getData().getLastPathSegment().equals(prefs.getString("update-nonce",""))||i.getIntExtra(PackageInstaller.EXTRA_SESSION_ID,-2)!=prefs.getInt("update-session",-1))return;
  int status=i.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE);
  if(status==PackageInstaller.STATUS_PENDING_USER_ACTION){
   Intent confirm=i.getParcelableExtra(Intent.EXTRA_INTENT);if(confirm==null){notice(c,"앱에서 업데이트 설치를 다시 확인해 주세요.",null);return;}
   PendingIntent open=PendingIntent.getActivity(c,NOTICE,confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
   notice(c,"새 버전 설치 확인이 필요해요. 눌러서 완료해 주세요.",open);
  }else if(status==PackageInstaller.STATUS_SUCCESS){prefs.edit().remove("update-ready").remove("update-session").remove("update-nonce").apply();c.getSystemService(NotificationManager.class).cancel(NOTICE);}
  else notice(c,"설치를 완료하지 못했어요. 앱에서 다시 설치할 수 있어요.",null);
 }
}
