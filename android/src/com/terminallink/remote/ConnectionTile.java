package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.graphics.drawable.Icon;
import android.os.Build;
import android.service.quicksettings.*;
import android.widget.Toast;

public final class ConnectionTile extends TileService {
 // Non-active mode refreshes from the live service whenever the panel opens,
 // including after process death. No connection is started just by viewing it.
 static ConnectionTile visible;
 @Override public void onStartListening(){super.onStartListening();visible=this;update();}
 @Override public void onStopListening(){if(visible==this)visible=null;super.onStopListening();}
 @Override public void onDestroy(){if(visible==this)visible=null;super.onDestroy();}
 static void refresh(){if(visible!=null)visible.update();}
 static Intent savedConnection(Context context){
  android.content.SharedPreferences p=context.getSharedPreferences("connection",MODE_PRIVATE);
  String host=p.getString("host","").trim(),pin=p.getString("pin","");int port=p.getInt("port",49443);
  if(host.isEmpty()||host.length()>253||!pin.matches("[A-F0-9]{64}")||port<1||port>65535)return null;
  return new Intent(context,LinkService.class).setAction(LinkService.CONNECT)
   .putExtra("host",host).putExtra("port",port).putExtra("pin",pin).putExtra("token","");
 }
 static Icon icon(Context context){return Icon.createWithResource(context,context.getResources().getIdentifier("ic_notification","drawable",context.getPackageName()));}
 static Intent preferences(Context context){return new Intent(context,MainActivity.class).setAction(ACTION_QS_TILE_PREFERENCES).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_CLEAR_TOP|Intent.FLAG_ACTIVITY_SINGLE_TOP);}
 void update(){
  Tile tile=getQsTile();if(tile==null)return;
  LinkService link=LinkService.current;boolean enabled=link!=null?link.enabled:getSharedPreferences("reply-state",MODE_PRIVATE).getBoolean("enabled",false);
  String state=enabled?(link!=null&&link.ready?"연결됨":getSharedPreferences("reply-state",MODE_PRIVATE).getBoolean("parked",false)?"알림 대기":"연결 중"):(savedConnection(this)==null?"PC 등록 필요":"꺼짐");
  tile.setLabel("TerminalLink");tile.setIcon(icon(this));tile.setSubtitle(state);
  tile.setContentDescription("TerminalLink, "+state);tile.setStateDescription(state);
  tile.setState(enabled?Tile.STATE_ACTIVE:Tile.STATE_INACTIVE);tile.updateTile();
 }
 @Override public void onClick(){super.onClick();if(isLocked())unlockAndRun(this::toggle);else toggle();}
 void toggle(){
  LinkService link=LinkService.current;
  if((link==null||!link.enabled)&&savedConnection(this)==null){openApp();return;}
  try{startForegroundService(new Intent(this,LinkService.class).setAction(LinkService.TOGGLE));}
  catch(RuntimeException e){
   launch(new Intent(this,TileConnectActivity.class).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK|Intent.FLAG_ACTIVITY_NO_ANIMATION));
  }
 }
 void openApp(){
  launch(preferences(this));
 }
 void launch(Intent intent){
  if(Build.VERSION.SDK_INT>=34)startActivityAndCollapse(PendingIntent.getActivity(this,7010,intent,PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE));
  else startActivityAndCollapse(intent);
 }
}
