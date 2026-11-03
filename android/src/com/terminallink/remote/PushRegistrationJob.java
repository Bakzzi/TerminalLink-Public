package com.terminallink.remote;
import android.app.job.*;
import android.content.*;
import android.os.*;
import android.util.Base64;
import java.util.*;
import org.json.*;

// Runs only when Firebase rotates a token. No periodic job or permanent socket.
public final class PushRegistrationJob extends JobService {
 static final int ID=7011;static PushRegistrationJob current;
 final Handler ui=new Handler(Looper.getMainLooper());SecureClient client;JobParameters job;Runnable timeout;String pin="";
 static void schedule(Context c){
  if(!c.getSharedPreferences("reply-state",MODE_PRIVATE).getBoolean("enabled",false)&&!c.getSharedPreferences("push-state",MODE_PRIVATE).getBoolean("disablePending",false))return;
  c.getSystemService(JobScheduler.class).schedule(new JobInfo.Builder(ID,new ComponentName(c,PushRegistrationJob.class)).setRequiredNetworkType(JobInfo.NETWORK_TYPE_ANY).setMinimumLatency(1000).setBackoffCriteria(30000,JobInfo.BACKOFF_POLICY_EXPONENTIAL).build());
 }
 @Override public boolean onStartJob(JobParameters p){
  SharedPreferences state=getSharedPreferences("reply-state",MODE_PRIVATE);boolean disabling=!state.getBoolean("enabled",false)&&getSharedPreferences("push-state",MODE_PRIVATE).getBoolean("disablePending",false);if(!state.getBoolean("enabled",false)&&!disabling)return false;
  LinkService live=LinkService.current;if(live!=null&&live.client!=null){if(live.ready){live.pushRegistering=false;PushClient.register(live);return false;}return false;}
  job=p;current=this;pin=state.getString("pin","");
  client=new SecureClient(state.getString("host",""),state.getInt("port",49443),pin,"",new SecureClient.Listener(){
   public void frame(byte[] b){}
   public void ended(String s){ui.post(()->finish(true));}
   public void json(JSONObject j){ui.post(()->{
    if(client==null)return;
    if(j.optString("type").equals("ready"))try{
     if(j.optInt("push")!=1){finish(false);return;}
     if(disabling){client.send(MainActivity.obj("op","pushDisable"));return;}
     byte[] key=PushCrypto.key(PushRegistrationJob.this,pin,false);
     try{client.send(MainActivity.obj("op","pushRegister","token",getSharedPreferences("push-state",MODE_PRIVATE).getString("token",""),"platform","android","key",Base64.encodeToString(key,Base64.NO_WRAP)));}finally{Arrays.fill(key,(byte)0);}
    }catch(Exception e){finish(false);}
    else if(j.optString("type").equals("pushDisabled")){getSharedPreferences("push-state",MODE_PRIVATE).edit().putBoolean("disablePending",false).apply();finish(false);}
    else if(j.optString("type").equals("pushStatus")&&j.optBoolean("ready"))client.send(MainActivity.obj("op","park","epoch",state.getString("event-epoch",""),"after",state.getLong("event-sequence",0)));
    else if(j.optString("type").equals("parked"))finish(!j.optBoolean("ok"));
   });}
  });
  timeout=()->finish(true);ui.postDelayed(timeout,25000);client.start();return true;
 }
 void finish(boolean retry){if(timeout!=null)ui.removeCallbacks(timeout);timeout=null;if(client!=null){client.close();client=null;}if(current==this)current=null;JobParameters p=job;job=null;if(p!=null)jobFinished(p,retry);}
 @Override public boolean onStopJob(JobParameters p){job=null;finish(false);return true;}
}