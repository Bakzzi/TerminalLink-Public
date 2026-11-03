package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.os.Bundle;
import android.widget.Toast;
public final class TileConnectActivity extends Activity {
 boolean started;
 @Override public void onCreate(Bundle state){
  super.onCreate(state);
  getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_SECURE);
  if(state!=null){started=true;finish();}
 }
 @Override public void onResume(){
  super.onResume();if(started)return;started=true;
  // Some Android versions disallow starting a foreground service directly
  // from a cold TileService. This user-launched activity supplies the visible
  // start, then returns to the previous app without opening MainActivity.
  getWindow().getDecorView().post(()->{
   try{
    LinkService link=LinkService.current;
    if(link==null||!link.enabled){
     Intent intent=ConnectionTile.savedConnection(this);
     if(intent!=null)startForegroundService(intent);
     else startActivity(ConnectionTile.preferences(this));
    }
   }catch(RuntimeException e){Toast.makeText(this,"앱에서 PC 연결을 시작해 주세요.",Toast.LENGTH_LONG).show();startActivity(ConnectionTile.preferences(this));}
   finally{finish();overridePendingTransition(0,0);}
  });
 }
}
