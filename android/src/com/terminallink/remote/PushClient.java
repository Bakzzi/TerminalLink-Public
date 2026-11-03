package com.terminallink.remote;
import android.content.*;
import android.os.*;
import android.util.Base64;
import com.google.firebase.messaging.*;
import java.util.*;
import java.util.concurrent.*;
import org.json.*;

final class PushClient {
 static void register(LinkService service){
  if(!service.enabled||!service.ready||service.readyMessage==null||service.readyMessage.optInt("push")!=1||service.pushRegistering)return;
  service.pushRegistering=true;
  try{
   FirebaseMessaging.getInstance().setAutoInitEnabled(true);
   FirebaseMessaging.getInstance().getToken().addOnCompleteListener(task->{
    if(!task.isSuccessful()){service.pushRegistering=false;return;}
    if(!service.enabled||!service.ready)return;
    try{
     String token=task.getResult();service.getSharedPreferences("push-state",Context.MODE_PRIVATE).edit().putString("token",token).apply();
     byte[] key=PushCrypto.key(service,service.pin,true);
     try{service.send(MainActivity.obj("op","pushRegister","token",token,"platform","android","key",Base64.encodeToString(key,Base64.NO_WRAP)));}finally{Arrays.fill(key,(byte)0);}
    }catch(Exception e){service.pushRegistering=false;}
   });
  }catch(Exception e){service.pushRegistering=false;}
 }
 static void receive(Context context,Map<String,String> data){
  try{
   context.getSharedPreferences("push-state",Context.MODE_PRIVATE).edit().putLong("receivedAt",System.currentTimeMillis()).apply();
   SharedPreferences state=context.getSharedPreferences("reply-state",Context.MODE_PRIVATE);
   String pc=data.get("pc");if(!state.getBoolean("enabled",false)||pc==null||!pc.equals(state.getString("pin","")))return;
   JSONObject value=PushCrypto.decrypt(context,pc,data.get("tl"));
   context.getSharedPreferences("push-state",Context.MODE_PRIVATE).edit().putString("lastResult","received "+value.optString("kind")).apply();
   if(value.optString("kind").equals("probe")){
    LinkService live=LinkService.current;
    if(live!=null&&live.ready&&pc.equals(live.pin))live.send(MainActivity.obj("op","pushConfirm","challenge",value.optString("challenge")));
    else {PushRegistrationJob job=PushRegistrationJob.current;if(job!=null&&job.client!=null&&pc.equals(job.pin))job.client.send(MainActivity.obj("op","pushConfirm","challenge",value.optString("challenge")));}
    return;
   }
   PushNotifications.show(context,value);
  }catch(Exception e){context.getSharedPreferences("push-state",Context.MODE_PRIVATE).edit().putString("lastResult",e.getClass().getSimpleName()).apply();}
 }
}
