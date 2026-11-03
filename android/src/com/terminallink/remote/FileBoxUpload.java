package com.terminallink.remote;

import android.app.Activity;
import android.content.*;
import android.database.Cursor;
import android.net.Uri;
import android.os.SystemClock;
import android.provider.OpenableColumns;
import org.json.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

// One user-initiated upload at a time; never writes to a terminal or conversation.
final class FileBoxUpload {
 static final int PICK=16,CHUNK=12288;
 final RemoteFiles box;final MainActivity a;
 final ExecutorService worker=Executors.newSingleThreadExecutor();
 final ConcurrentHashMap<String,CompletableFuture<JSONObject>> pending=new ConcurrentHashMap<>();
 volatile boolean uploading,cancelled,closed;boolean picking;volatile String uploadId="";volatile SecureClient active;
 long progressAt;
 FileBoxUpload(RemoteFiles box){this.box=box;a=box.a;}
 boolean busy(){return picking||uploading;}
 void pick(){
  if(box.transfer!=null||busy()){box.toast("진행 중인 파일 전송을 마친 뒤 다시 눌러주세요.");return;}
  if(!a.ready||!box.uploadSupported){box.toast("PC 연결 프로그램을 업데이트한 뒤 파일을 올려주세요.");return;}
  picking=true;
  try{a.startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true),PICK);}
  catch(RuntimeException e){picking=false;box.toast("파일 선택 화면을 열지 못했습니다.");}
 }
 void selected(int result,Intent data){
  picking=false;if(result!=Activity.RESULT_OK||data==null){park();return;}
  if(closed||uploading||box.transfer!=null||!a.ready||!box.uploadSupported){box.setInfo("PC 연결을 확인한 뒤 파일을 다시 선택해 주세요.");park();return;}
  ArrayList<Uri> uris=new ArrayList<>();ClipData clips=data.getClipData();
  if(clips!=null){for(int i=0;i<clips.getItemCount();i++){Uri u=clips.getItemAt(i).getUri();if(u!=null&&!uris.contains(u))uris.add(u);}}
  else if(data.getData()!=null)uris.add(data.getData());
  if(uris.isEmpty()){park();return;}
  if(uris.size()>32){box.setInfo("한 번에 최대 32개까지 선택할 수 있습니다.");park();return;}
  SecureClient client=a.client;if(client==null||!client.running){box.setInfo("PC 연결을 확인한 뒤 다시 선택해 주세요.");park();return;}
  uploading=true;cancelled=false;active=client;box.setInfo("파일을 준비하고 있습니다…");box.render();
  worker.execute(()->run(client,uris));
 }
 void run(SecureClient client,ArrayList<Uri> uris){
  int completed=0;String error="";
  try{for(Uri uri:uris){one(client,uri,completed+1,uris.size());completed++;}}
  catch(TimeoutException e){error="전송 결과를 확인하지 못했습니다. 다시 올리기 전에 파일함을 확인해 주세요.";}
  catch(Exception e){error=e.getMessage()==null?"파일을 올리지 못했습니다.":e.getMessage();}
  finally{
   String id=uploadId;if(!id.isEmpty()&&client.running)client.send(MainActivity.obj("op","sharedUploadCancel","upload",id,"request",UUID.randomUUID().toString()));
   uploadId="";active=null;for(CompletableFuture<JSONObject> f:pending.values())f.completeExceptionally(new IOException("업로드가 끝났습니다."));pending.clear();
  }
  final int done=completed;final String why=error;
  a.ui.post(()->{uploading=false;if(closed)return;box.setInfo(why.isEmpty()?done+"개 파일을 올렸습니다.":done+"개 완료 · "+why);box.render();if(a.ready&&box.supported)box.refresh(true);park();});
 }
 String name(Uri uri){
  try(Cursor c=a.getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){
   if(c!=null&&c.moveToFirst()){String value=c.getString(0);if(value!=null&&!value.trim().isEmpty())return value.length()>200?value.substring(0,200):value;}
  }catch(Exception ignored){}return "attachment.bin";
 }
 void check(SecureClient client)throws IOException{
  if(closed||cancelled||!client.running||client!=a.client)throw new IOException("업로드가 중단됐습니다. 파일함을 확인한 뒤 다시 올려주세요.");
 }
 static final class Exchange {final String id=UUID.randomUUID().toString();final CompletableFuture<JSONObject> future=new CompletableFuture<>();long offset;}
 Exchange begin(SecureClient client,JSONObject data)throws Exception{
  check(client);Exchange x=new Exchange();data.put("request",x.id);pending.put(x.id,x.future);
  if(!client.running){pending.remove(x.id);throw new IOException("PC 연결이 끊겼습니다.");}client.send(data);return x;
 }
 JSONObject finish(Exchange x)throws Exception{
  try{JSONObject reply=x.future.get(30,TimeUnit.SECONDS);if(!reply.optBoolean("ok"))throw new IOException(reply.optString("message","파일을 올리지 못했습니다."));return reply;}
  catch(ExecutionException e){Throwable cause=e.getCause();throw new IOException(cause==null?"업로드가 중단됐습니다.":cause.getMessage(),cause);}
  finally{pending.remove(x.id);}
 }
 void progress(int index,int count,long offset,long total,String stage){
  long now=SystemClock.elapsedRealtime();if(now-progressAt<250)return;progressAt=now;
  String text=index+"/"+count+" · "+stage+" "+box.bytes(offset)+(total>=0?" / "+box.bytes(total):"");
  a.ui.post(()->{if(!closed&&uploading)box.setInfo(text);});
 }
 void one(SecureClient client,Uri uri,int index,int count)throws Exception{
  if(!"content".equals(uri.getScheme()))throw new IOException("선택한 파일을 읽을 수 없습니다.");
  String name=name(uri);MessageDigest digest=MessageDigest.getInstance("SHA-256");byte[] buffer=new byte[CHUNK];long length=0;
  try(InputStream in=a.getContentResolver().openInputStream(uri)){
   if(in==null)throw new IOException("선택한 파일을 읽을 수 없습니다.");int n;
   while((n=in.read(buffer))!=-1){check(client);if(n==0)continue;length+=n;if(length>RemoteFiles.MAX)throw new IOException("파일 하나는 최대 512MB까지 올릴 수 있습니다.");digest.update(buffer,0,n);progress(index,count,length,-1,"준비 중");}
  }
  final long size=length;String sha=SecureClient.hex(digest.digest());uploadId=UUID.randomUUID().toString();
  finish(begin(client,MainActivity.obj("op","sharedUploadBegin","upload",uploadId,"name",name,"size",size,"sha256",sha)));
  try(InputStream in=a.getContentResolver().openInputStream(uri)){
   if(in==null)throw new IOException("선택한 파일을 다시 읽을 수 없습니다.");long offset=0;boolean eof=false;ArrayDeque<Exchange> window=new ArrayDeque<>();
   while(!eof||!window.isEmpty()){
    while(!eof&&window.size()<8){check(client);int n=in.read(buffer);if(n<0){eof=true;break;}if(n==0)continue;
     Exchange x=begin(client,MainActivity.obj("op","sharedUploadChunk","upload",uploadId,"offset",offset,"data",android.util.Base64.encodeToString(buffer,0,n,android.util.Base64.NO_WRAP)));offset+=n;x.offset=offset;window.add(x);}
    if(window.isEmpty())break;Exchange x=window.remove();if(finish(x).optLong("offset",-1)!=x.offset)throw new IOException("파일 전송 순서가 맞지 않습니다.");progress(index,count,x.offset,size,"올리는 중");
   }
  }
  JSONObject result=finish(begin(client,MainActivity.obj("op","sharedUploadEnd","upload",uploadId)));
  JSONObject file=result.optJSONObject("file");
  if(file==null||file.optLong("size",-1)!=size||!sha.equals(file.optString("sha256")))throw new IOException("파일함 등록 결과를 확인하지 못했습니다. 목록을 확인해 주세요.");
  uploadId="";
 }
 void message(JSONObject reply){CompletableFuture<JSONObject> f=pending.remove(reply.optString("request"));if(f!=null)f.complete(reply);}
 void cancel(){
  if(!uploading)return;cancelled=true;for(CompletableFuture<JSONObject> f:pending.values())f.completeExceptionally(new IOException("업로드 취소를 요청했습니다. 파일함을 확인해 주세요."));box.setInfo("업로드를 취소하고 있습니다…");
 }
 void disconnected(){cancelled=true;for(CompletableFuture<JSONObject> f:pending.values())f.completeExceptionally(new IOException("연결이 끊겼습니다. 파일함을 확인해 주세요."));}
 void close(){closed=true;cancel();worker.shutdown();}
 void park(){if(a.link!=null)a.link.schedulePark();}
}
