package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.content.pm.PackageInstaller;
import android.os.*;
import android.view.*;
import android.widget.*;
import org.json.JSONObject;
import java.io.*;
import java.util.UUID;

// Opened directly by the user's foreground activity, before staging the APK.
// Installer status is delivered as a broadcast; it never has to launch this screen.
public final class UpdateInstallActivity extends Activity {
 final Handler ui=new Handler(Looper.getMainLooper());
 PackageInstaller installer;BroadcastReceiver receiver;PendingIntent callback;
 TextView state;ProgressBar progress;
 volatile int sessionId=-1;volatile boolean cancelled;
 boolean resumed,confirmLaunched,completed;Intent confirmation;
 final Runnable timeout=()->fail("설치 화면을 준비하지 못했습니다. 돌아가서 다시 시도하세요.");
 @Override public void onCreate(Bundle saved){
  super.onCreate(saved);getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
  // A callback from an update started by an older installed version can arrive after replacement.
  if(getIntent().hasExtra(PackageInstaller.EXTRA_STATUS)){
   int status=getIntent().getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE);
   Intent confirm=getIntent().getParcelableExtra(Intent.EXTRA_INTENT);
   if(status==PackageInstaller.STATUS_PENDING_USER_ACTION&&confirm!=null)startActivity(confirm);
   else {Toast.makeText(this,status==PackageInstaller.STATUS_SUCCESS?"업데이트가 완료됐습니다.":"설치가 취소되었거나 완료되지 않았습니다.",Toast.LENGTH_LONG).show();startActivity(new Intent(this,MainActivity.class).addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP));}
   finish();return;
  }
  installer=getPackageManager().getPackageInstaller();
  LinearLayout content=new LinearLayout(this);content.setOrientation(LinearLayout.VERTICAL);content.setGravity(Gravity.CENTER);content.setPadding(36,48,36,48);content.setBackgroundColor(0xff0c121d);
  state=new TextView(this);state.setText("업데이트를 확인하고 있습니다…");state.setTextColor(0xffdfe9f5);state.setTextSize(18);state.setGravity(Gravity.CENTER);content.addView(state);
  progress=new ProgressBar(this);content.addView(progress);
  Button back=new Button(this);back.setText("돌아가기");back.setOnClickListener(v->onBackPressed());content.addView(back);setContentView(content);
  if(saved!=null){sessionId=saved.getInt("session",-1);fail("설치 준비가 중단됐습니다. 돌아가서 다시 시도하세요.");return;}
  try{
   if(!getPackageManager().canRequestPackageInstalls())throw new IOException("앱 설치 권한을 허용한 뒤 다시 시도하세요.");
   int pending=getSharedPreferences("connection",MODE_PRIVATE).getInt("update-session",-1);if(pending>=0)try{installer.abandonSession(pending);}catch(Exception ignored){}
   getSharedPreferences("connection",MODE_PRIVATE).edit().remove("update-session").remove("update-nonce").apply();
   UpdateApk.Offer offer=new UpdateApk.Offer(new JSONObject(getSharedPreferences("connection",MODE_PRIVATE).getString("update-ready","")));
   File apk=new File(UpdateApk.folder(this),offer.sha256+".apk");
   String action=getPackageName()+".INSTALL_STATUS."+UUID.randomUUID();
   receiver=new BroadcastReceiver(){@Override public void onReceive(Context c,Intent intent){
    if(!action.equals(intent.getAction())||cancelled||completed)return;
    if(intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID,sessionId)!=sessionId)return;
    int status=intent.getIntExtra(PackageInstaller.EXTRA_STATUS,PackageInstaller.STATUS_FAILURE);
    ui.removeCallbacks(timeout);
    if(status==PackageInstaller.STATUS_PENDING_USER_ACTION){
     confirmation=intent.getParcelableExtra(Intent.EXTRA_INTENT);
     if(confirmation==null){fail("안드로이드 설치 확인 화면을 받지 못했습니다.");return;}
     showConfirmation();
    }else{
     completed=true;
     Toast.makeText(UpdateInstallActivity.this,status==PackageInstaller.STATUS_SUCCESS?"업데이트가 완료됐습니다.":status==PackageInstaller.STATUS_FAILURE_ABORTED?"설치를 취소했습니다.":"설치를 완료하지 못했습니다. 다시 시도하세요.",Toast.LENGTH_LONG).show();
     finish();
    }
   }};
   if(Build.VERSION.SDK_INT>=33)registerReceiver(receiver,new IntentFilter(action),Context.RECEIVER_NOT_EXPORTED);
   else registerReceiver(receiver,new IntentFilter(action));
   int flags=PendingIntent.FLAG_UPDATE_CURRENT;if(Build.VERSION.SDK_INT>=31)flags|=PendingIntent.FLAG_MUTABLE;
   callback=PendingIntent.getBroadcast(this,0,new Intent(action).setPackage(getPackageName()),flags);
   state.setText("새 버전 "+offer.versionName+"\n안드로이드 설치 화면을 준비합니다…");
   ui.postDelayed(timeout,30000);
   new Thread(()->prepare(apk,offer),"TerminalLink-update-install").start();
  }catch(Exception e){fail("설치를 시작하지 못했습니다: "+e.getMessage());}
 }
 void prepare(File apk,UpdateApk.Offer offer){
  try{
   UpdateApk.verify(this,apk,offer);if(cancelled)return;
   PackageInstaller.SessionParams params=new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
   params.setAppPackageName(getPackageName());params.setSize(apk.length());
   if(Build.VERSION.SDK_INT>=31)params.setRequireUserAction(PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED);
   sessionId=installer.createSession(params);
   if(cancelled){abandon();return;}
   try(PackageInstaller.Session session=installer.openSession(sessionId)){
    try(InputStream in=new FileInputStream(apk);OutputStream out=session.openWrite("base.apk",0,apk.length())){
     byte[] buffer=new byte[65536];int n;while((n=in.read(buffer))!=-1){if(cancelled)throw new IOException("취소됨");out.write(buffer,0,n);}session.fsync(out);
    }
    if(cancelled){abandon();return;}
    session.commit(callback.getIntentSender());
   }
  }catch(Exception e){ui.post(()->{if(!cancelled)fail("설치를 시작하지 못했습니다: "+e.getMessage());});}
 }
 void showConfirmation(){
  if(!resumed||confirmation==null||confirmLaunched||cancelled)return;
  Intent confirm=confirmation;confirmation=null;confirmLaunched=true;
  state.setText("안드로이드 화면에서 설치를 확인하세요.");
  try{startActivity(confirm);}catch(Exception e){fail("설치 확인 화면을 열지 못했습니다: "+e.getMessage());}
 }
 void abandon(){if(installer!=null&&sessionId>=0)try{installer.abandonSession(sessionId);}catch(Exception ignored){}}
 void fail(String message){cancelled=true;ui.removeCallbacks(timeout);abandon();if(state!=null){state.setText(message);progress.setVisibility(View.GONE);}else{Toast.makeText(this,message,Toast.LENGTH_LONG).show();finish();}}
 @Override protected void onResume(){super.onResume();resumed=true;showConfirmation();}
 @Override protected void onPause(){resumed=false;super.onPause();}
 @Override protected void onSaveInstanceState(Bundle out){out.putInt("session",sessionId);super.onSaveInstanceState(out);}
 @Override public void onBackPressed(){cancelled=true;abandon();finish();}
 @Override protected void onDestroy(){cancelled=true;ui.removeCallbacks(timeout);if(receiver!=null)try{unregisterReceiver(receiver);}catch(Exception ignored){}if(callback!=null)callback.cancel();if(!completed)abandon();if(LinkService.current!=null&&LinkService.current.updates!=null)LinkService.current.updates.manualInstall=false;super.onDestroy();}
}
