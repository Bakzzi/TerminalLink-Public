package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.net.Uri;
import android.os.*;
import android.provider.MediaStore;
import android.view.*;
import android.webkit.MimeTypeMap;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

// Explicitly published PC copies only. No conversation/path scanning and no background polling.
final class RemoteFiles {
 static final long MAX=512L*1024*1024;
 static final int CHUNK=192*1024;
 final MainActivity a;
 final ExecutorService disk=Executors.newSingleThreadExecutor();
 AlertDialog dialog;LinearLayout rows;TextView status;
 JSONArray items=new JSONArray();String listRequest="",deleteRequest="",info="";
 boolean supported,closed;Transfer transfer;Uri completed;String completedName="",completedMime="";
 long lastResponse;
 final Runnable deadline=new Runnable(){public void run(){if(transfer==null)return;if(SystemClock.elapsedRealtime()-lastResponse>35000){cancel("다운로드가 중단됐습니다. 다시 받기를 눌러주세요.");return;}a.ui.postDelayed(this,1000);}};
 RemoteFiles(MainActivity activity){a=activity;disk.execute(()->Transfer.cleanPending(a));}
 boolean busy(){return transfer!=null;}
 void connected(boolean value){supported=value;if(dialog!=null)refresh();}
 void show(){
  if(dialog!=null){dialog.show();return;}
  LinearLayout body=new LinearLayout(a);body.setOrientation(1);body.setPadding(a.dp(18),a.dp(6),a.dp(18),a.dp(8));
  TextView hint=a.label("요청해서 올린 파일만 표시돼요. 24시간 후 PC의 임시 복사본이 삭제됩니다.",13,a.MUTED);body.addView(hint);
  status=a.label(info,13,a.FG);body.addView(status);
  LinearLayout controls=a.row();controls.addView(a.button("새로고침",v->refresh()));controls.addView(a.button("받기 취소",v->cancel("다운로드를 취소했습니다.")));body.addView(controls);
  ScrollView scroll=new ScrollView(a);rows=new LinearLayout(a);rows.setOrientation(1);scroll.addView(rows);body.addView(scroll,new LinearLayout.LayoutParams(-1,a.dp(310)));
  dialog=new AlertDialog.Builder(a).setTitle("임시 파일함").setView(body).setNegativeButton("닫기",null).setNeutralButton("받은 파일",null).create();
  dialog.setOnDismissListener(d->{dialog=null;rows=null;status=null;});
  dialog.show();dialog.getButton(AlertDialog.BUTTON_NEUTRAL).setOnClickListener(v->{
   try{a.startActivity(new Intent(DownloadManager.ACTION_VIEW_DOWNLOADS));}catch(Exception e){toast("내 파일 → 다운로드 → TerminalLink에서 확인하세요.");}
  });render();refresh();
 }
 void setInfo(String text){info=text;if(status!=null)status.setText(text);}
 void refresh(){
  if(!a.ready){setInfo("PC에 연결한 뒤 파일함을 열어주세요.");return;}
  if(!supported){setInfo("파일함을 사용하려면 PC 프로그램을 업데이트하세요.");return;}
  listRequest=UUID.randomUUID().toString();setInfo(transfer==null?"파일 목록을 확인하고 있습니다…":info);
  send(MainActivity.obj("op","sharedList","request",listRequest));
 }
 void send(JSONObject j){if(a.ready&&a.link!=null)a.link.send(j);}
 String bytes(long n){return android.text.format.Formatter.formatShortFileSize(a,n);}
 void render(){
  if(rows==null)return;rows.removeAllViews();
  if(items.length()==0){rows.addView(a.label("올려둔 파일이 없습니다.",15,a.MUTED));return;}
  for(int i=0;i<items.length();i++){
   JSONObject item=items.optJSONObject(i);if(item==null)continue;
   LinearLayout card=new LinearLayout(a);card.setOrientation(1);card.setPadding(0,a.dp(12),0,a.dp(12));
   TextView name=a.label(item.optString("name"),16,a.FG);card.addView(name);
   String until=new java.text.SimpleDateFormat("MM-dd HH:mm",Locale.getDefault()).format(new Date(item.optLong("expires")));
   card.addView(a.label(bytes(item.optLong("size"))+" · "+until+"까지",12,a.MUTED));
   LinearLayout actions=a.row();
   Button get=a.button("받기",v->new AlertDialog.Builder(a).setTitle("파일 받기").setMessage(item.optString("name")+"\n"+bytes(item.optLong("size"))+"\n\n다운로드/TerminalLink에 저장합니다.").setNegativeButton("취소",null).setPositiveButton("받기",(d,w)->start(item)).show());get.setEnabled(transfer==null);get.setTag("shared-get-"+item.optString("id"));actions.addView(get);
   Button remove=a.button("삭제",v->new AlertDialog.Builder(a).setTitle("파일함에서 삭제").setMessage(item.optString("name")+"\n\nPC 원본과 휴대폰에 받은 파일은 유지됩니다.").setNegativeButton("취소",null).setPositiveButton("삭제",(d,w)->{deleteRequest=UUID.randomUUID().toString();send(MainActivity.obj("op","sharedDelete","request",deleteRequest,"file",item.optString("id"),"confirmed",true));}).show());remove.setEnabled(transfer==null);actions.addView(remove);card.addView(actions);rows.addView(card);
  }
 }
 void start(JSONObject item){
  if(transfer!=null){toast("현재 파일을 받은 뒤 다시 눌러주세요.");return;}
  if(!a.ready||!supported){toast("PC 연결을 확인해 주세요.");return;}
  try {
   transfer=new Transfer(a,UUID.randomUUID().toString(),item);lastResponse=SystemClock.elapsedRealtime();
   setInfo(item.getString("name")+" · 받기 준비 중");render();a.ui.removeCallbacks(deadline);a.ui.postDelayed(deadline,1000);
   send(MainActivity.obj("op","sharedOpen","request",transfer.request,"file",item.getString("id")));
  }catch(Exception e){transfer=null;setInfo("파일 정보를 확인할 수 없습니다.");}
 }
 void message(JSONObject j){
  String type=j.optString("type"),request=j.optString("request");
  if(type.equals("sharedList")&&request.equals(listRequest)){listRequest="";JSONArray files=j.optJSONArray("files");items=files==null?new JSONArray():files;if(transfer==null)setInfo("받은 파일은 다운로드/TerminalLink에 저장돼요.");render();return;}
  if(type.equals("sharedDeleted")&&request.equals(deleteRequest)){deleteRequest="";refresh();return;}
  if(type.equals("sharedError")&&(request.equals(listRequest)||request.equals(deleteRequest))){listRequest="";deleteRequest="";setInfo(j.optString("message","파일함을 확인할 수 없습니다."));return;}
  final Transfer current=transfer;if(current==null||!request.equals(current.request))return;
  lastResponse=SystemClock.elapsedRealtime();
  if(type.equals("sharedError")){cancel(j.optString("message","파일을 받을 수 없습니다."));return;}
  if(!type.equals("sharedStart")&&!type.equals("sharedChunk")&&!type.equals("sharedEnd"))return;
  disk.execute(()->{
   try{
    Uri done=null;
    if(type.equals("sharedStart"))current.begin(j.getJSONObject("file"));
    else if(type.equals("sharedChunk"))current.append(j.getLong("offset"),j.getString("data"));
    else done=current.finish(j.getString("sha256"));
    final Uri received=done;
    a.ui.post(()->{
     if(closed||transfer!=current)return;
     lastResponse=SystemClock.elapsedRealtime();
     if(received==null){setInfo(current.name+" · "+bytes(current.received)+" / "+bytes(current.length));send(MainActivity.obj("op","sharedRead","request",current.request,"offset",current.received));}
     else {transfer=null;a.ui.removeCallbacks(deadline);completed=received;completedName=current.name;completedMime=current.mime;setInfo(current.name+" · 저장 완료");render();if(a.link!=null)a.link.schedulePark();if(a.resumed)showCompleted();else toast(current.name+" 저장 완료");}
    });
   }catch(Exception e){current.abort();a.ui.post(()->{if(transfer==current)cancel(e.getMessage()==null?"파일 저장에 실패했습니다.":e.getMessage());});}
  });
 }
 void showCompleted(){
  if(completed==null)return;final Uri uri=completed;final String name=completedName,mime=completedMime;completed=null;
  new AlertDialog.Builder(a).setTitle("다운로드 완료").setMessage(name+"\n다운로드/TerminalLink에 저장했습니다.").setNegativeButton("닫기",null).setPositiveButton("열기",(d,w)->open(uri,mime,false)).setNeutralButton("공유",(d,w)->open(uri,mime,true)).show();
 }
 void open(Uri uri,String mime,boolean share){
  Intent intent=share?new Intent(Intent.ACTION_SEND).setType(mime).putExtra(Intent.EXTRA_STREAM,uri):new Intent(Intent.ACTION_VIEW).setDataAndType(uri,mime);
  intent.setClipData(ClipData.newRawUri("파일",uri));intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
  try{a.startActivity(share?Intent.createChooser(intent,"파일 공유"):intent);}catch(ActivityNotFoundException e){toast("파일은 저장됐습니다. 이 형식을 열 수 있는 앱이 필요합니다.");}
 }
 void cancel(String reason){
  Transfer current=transfer;transfer=null;a.ui.removeCallbacks(deadline);
  if(current!=null){current.cancelled=true;send(MainActivity.obj("op","sharedCancel","request",current.request));disk.execute(current::abort);}
  setInfo(reason);render();if(a.link!=null)a.link.schedulePark();
 }
 void disconnected(){supported=false;listRequest="";deleteRequest="";if(transfer!=null)cancel("연결이 끊겨 다운로드를 취소했습니다. 다시 연결한 뒤 받아주세요.");}
 void resume(){if(completed!=null)showCompleted();}
 void close(){if(closed)return;cancel("");closed=true;if(dialog!=null)dialog.dismiss();disk.shutdown();}
 void toast(String text){Toast.makeText(a,text,Toast.LENGTH_LONG).show();}

 static final class Transfer {
  final Context context;final String request,id,name,digest,mime;final long length;volatile boolean cancelled;
  long received;Uri uri;OutputStream out;MessageDigest hash;boolean finished;
  Transfer(Context context,String request,JSONObject item)throws Exception{
   this.context=context.getApplicationContext();this.request=request;id=item.getString("id");name=item.getString("name");length=item.getLong("size");digest=item.getString("sha256");
   if(!id.matches("[a-f0-9]{32}")||name.isEmpty()||name.length()>200||name.equals(".")||name.equals("..")||name.matches(".*[\\\\/\\p{Cntrl}].*")||length<0||length>MAX||!digest.matches("[A-F0-9]{64}"))throw new IOException("잘못된 파일 정보입니다.");
   for(int i=0;i<name.length();i++)if(Character.isISOControl(name.charAt(i)))throw new IOException("잘못된 파일 이름입니다.");
   int dot=name.lastIndexOf('.');String type=dot<0?null:MimeTypeMap.getSingleton().getMimeTypeFromExtension(name.substring(dot+1).toLowerCase(Locale.ROOT));mime=type==null?"application/octet-stream":type;
  }
  void check()throws IOException{if(cancelled)throw new IOException("다운로드가 취소되었습니다.");}
  synchronized void begin(JSONObject incoming)throws Exception{
   check();if(out!=null||uri!=null||!id.equals(incoming.getString("id"))||!name.equals(incoming.getString("name"))||length!=incoming.getLong("size")||!digest.equals(incoming.getString("sha256")))throw new IOException("파일 정보가 변경되었습니다. 다시 확인하세요.");
   if(new StatFs(Environment.getExternalStorageDirectory().getPath()).getAvailableBytes()<length+16*1024*1024)throw new IOException("휴대폰 저장 공간이 부족합니다.");
   ContentValues values=new ContentValues();values.put(MediaStore.Downloads.DISPLAY_NAME,name);values.put(MediaStore.Downloads.MIME_TYPE,mime);values.put(MediaStore.Downloads.RELATIVE_PATH,Environment.DIRECTORY_DOWNLOADS+"/TerminalLink");values.put(MediaStore.Downloads.IS_PENDING,1);
   uri=context.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI,values);if(uri==null)throw new IOException("다운로드 파일을 만들 수 없습니다.");
   context.getSharedPreferences("shared-files",Context.MODE_PRIVATE).edit().putString("pending",uri.toString()).commit();
   out=context.getContentResolver().openOutputStream(uri,"w");if(out==null)throw new IOException("파일을 저장할 수 없습니다.");hash=MessageDigest.getInstance("SHA-256");
  }
  synchronized void append(long offset,String data)throws Exception{
   check();if(out==null||offset!=received||data.length()>4*((CHUNK+2)/3))throw new IOException("파일 전송 순서가 맞지 않습니다.");
   byte[] bytes=java.util.Base64.getDecoder().decode(data);
   if(bytes.length==0||bytes.length>CHUNK||bytes.length>length-received)throw new IOException("파일 조각의 크기가 맞지 않습니다.");
   out.write(bytes);hash.update(bytes);received+=bytes.length;
  }
  synchronized Uri finish(String serverHash)throws Exception{
   check();if(out==null||received!=length||!digest.equals(serverHash)||!digest.equals(SecureClient.hex(hash.digest())))throw new IOException("다운로드 파일 검증에 실패했습니다. 다시 받아주세요.");
   out.flush();out.close();out=null;ContentValues values=new ContentValues();values.put(MediaStore.Downloads.IS_PENDING,0);
   if(context.getContentResolver().update(uri,values,null,null)!=1)throw new IOException("파일 저장을 완료하지 못했습니다.");
   finished=true;clearPending();return uri;
  }
  synchronized void abort(){try{if(out!=null)out.close();}catch(Exception ignored){}out=null;if(uri!=null&&!finished)try{context.getContentResolver().delete(uri,null,null);}catch(Exception ignored){}clearPending();}
  void clearPending(){android.content.SharedPreferences prefs=context.getSharedPreferences("shared-files",Context.MODE_PRIVATE);if(uri!=null&&uri.toString().equals(prefs.getString("pending","")))prefs.edit().remove("pending").commit();}
  static void cleanPending(Context context){
   android.content.SharedPreferences prefs=context.getSharedPreferences("shared-files",Context.MODE_PRIVATE);String value=prefs.getString("pending","");
   if(value.isEmpty())return;
   try{Uri uri=Uri.parse(value);if("content".equals(uri.getScheme())&&"media".equals(uri.getAuthority())&&uri.getPath()!=null&&uri.getPath().matches("/[^/]+/downloads/[0-9]+"))context.getContentResolver().delete(uri,null,null);}catch(Exception ignored){}
   prefs.edit().remove("pending").commit();
  }
 }
}
