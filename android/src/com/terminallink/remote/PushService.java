package com.terminallink.remote;
import android.content.*;
import android.os.*;
import com.google.firebase.messaging.*;
import java.util.concurrent.*;

public final class PushService extends FirebaseMessagingService {
 @Override public void onMessageReceived(RemoteMessage message){
  // Finish notification work while Firebase still grants this callback execution time.
  CountDownLatch done=new CountDownLatch(1);
  new Handler(Looper.getMainLooper()).post(()->{try{PushClient.receive(this,message.getData());}finally{done.countDown();}});
  try{done.await(6,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}
 }
 @Override public void onNewToken(String token){
  getSharedPreferences("push-state",MODE_PRIVATE).edit().putString("token",token).apply();
  new Handler(Looper.getMainLooper()).post(()->{LinkService live=LinkService.current;if(live!=null&&live.ready){live.pushRegistering=false;PushClient.register(live);}else PushRegistrationJob.schedule(this);});
 }
}
