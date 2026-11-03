package com.terminallink.remote;
import android.app.Activity;
import android.content.*;
import android.database.Cursor;
import android.net.Uri;
import org.json.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.concurrent.*;

public class UploadJvm {
 static void check(boolean ok,String message){if(!ok)throw new AssertionError(message);System.out.println("PASS "+message);}
 public static void main(String[] args)throws Exception{
  String one="11111111111111111111111111111111",two="22222222222222222222222222222222",later="33333333333333333333333333333333";
  FileBoxClear clear=new FileBoxClear();
  check(!clear.start(Arrays.asList(one,two),Arrays.asList(one,later)),"Changed file list cannot be cleared after confirmation");
  check(!clear.start(Arrays.asList(one,one),Arrays.asList(one,one)),"Duplicate IDs cannot enter a clear operation");
  check(clear.start(Arrays.asList(one,two),Arrays.asList(one,two))&&clear.id().equals(one),"Clear begins with the exact confirmed list");
  clear.requested("first");check(!clear.accept("first",two)&&!clear.accept("old",one)&&clear.id().equals(one),"Unexpected replies never advance deletion");
  check(clear.accept("first",one)&&clear.id().equals(two),"Confirmed deletions advance one file at a time");
  clear.requested("second");check(clear.accept("second",two)&&!clear.active()&&clear.done==2,"All confirmed files finish without selecting later uploads");
  check(clear.start(Arrays.asList(one),Arrays.asList(one)),"A later clear can start separately");clear.stop();check(!clear.active()&&clear.request().isEmpty(),"Interrupted clear stops without retrying");
  MainActivity a=new MainActivity();RemoteFiles box=new RemoteFiles(a);a.client.owner=box.upload;
  try{
   box.upload.pick();check(box.upload.busy()&&a.picker==FileBoxUpload.PICK,"Picker keeps connection active");
   box.upload.selected(0,null);check(!box.upload.busy(),"Picker cancellation releases connection");
   Uri[] selected=new Uri[4];
   for(int i=0;i<4;i++){selected[i]=new Uri("photo-"+i+".png");byte[] bytes=new byte[35000+i];new Random(i).nextBytes(bytes);a.resolver.files.put(selected[i],bytes);}
   Intent result=new Intent("fixture");result.clips=new ClipData(selected);box.upload.selected(Activity.RESULT_OK,result);
   box.upload.worker.submit(()->{}).get(15,TimeUnit.SECONDS);a.client.worker.submit(()->{}).get(5,TimeUnit.SECONDS);
   check(a.client.saved.size()==4&&box.info.contains("4개 파일"),"Four photos upload in order with a single selection");
   for(Uri uri:selected)check(Arrays.equals(a.resolver.files.get(uri),a.client.saved.get(uri.value)),"Uploaded bytes match source "+uri.value);
   check(!box.upload.busy()&&a.client.cancels==0,"Successful upload finishes without cancellation or repeat");
   Uri fail=new Uri("fail.txt");a.resolver.files.put(fail,new byte[]{1,2,3});a.client.failChunk=true;
   Intent bad=new Intent("fixture");bad.uri=fail;box.upload.selected(Activity.RESULT_OK,bad);
   box.upload.worker.submit(()->{}).get(10,TimeUnit.SECONDS);a.client.worker.submit(()->{}).get(5,TimeUnit.SECONDS);
   check(!a.client.saved.containsKey(fail.value)&&box.info.contains("실패")&&a.client.cancels==1,"Failed upload keeps original and cancels incomplete server staging");
   a.client.failChunk=false;CountDownLatch arrived=new CountDownLatch(1),release=new CountDownLatch(1);a.client.arrived=arrived;a.client.release=release;
   Uri cancel=new Uri("cancel.txt");a.resolver.files.put(cancel,new byte[200]);Intent cancelled=new Intent("fixture");cancelled.uri=cancel;box.upload.selected(Activity.RESULT_OK,cancelled);
   check(arrived.await(5,TimeUnit.SECONDS),"Cancellation fixture reaches an active chunk");box.upload.cancel();release.countDown();
   box.upload.worker.submit(()->{}).get(10,TimeUnit.SECONDS);a.client.worker.submit(()->{}).get(5,TimeUnit.SECONDS);
   check(!a.client.saved.containsKey(cancel.value)&&a.client.cancels==2&&!box.upload.busy(),"Cancellation never publishes partial content or resends");
   arrived=new CountDownLatch(1);release=new CountDownLatch(1);a.client.arrived=arrived;a.client.release=release;
   Uri lost=new Uri("lost.txt");a.resolver.files.put(lost,new byte[200]);Intent lostIntent=new Intent("fixture");lostIntent.uri=lost;box.upload.selected(Activity.RESULT_OK,lostIntent);
   check(arrived.await(5,TimeUnit.SECONDS),"Disconnect fixture reaches an active chunk");a.ready=false;box.supported=false;box.upload.disconnected();release.countDown();
   box.upload.worker.submit(()->{}).get(10,TimeUnit.SECONDS);a.client.worker.submit(()->{}).get(5,TimeUnit.SECONDS);
   check(!a.client.saved.containsKey(lost.value)&&box.info.contains("연결")&&!box.upload.busy(),"Disconnect preserves the failure notice and does not publish partial content");
  }finally{box.upload.close();a.client.worker.shutdownNow();a.client.worker.awaitTermination(2,TimeUnit.SECONDS);}
 }
}
class MainActivity {
 final UI ui=new UI();final Resolver resolver=new Resolver();final SecureClient client=new SecureClient();final LinkService link=new LinkService();boolean ready=true;int picker;
 void startActivityForResult(Intent i,int code){picker=code;}Resolver getContentResolver(){return resolver;}
 static JSONObject obj(Object... fields){JSONObject j=new JSONObject();try{for(int i=0;i<fields.length;i+=2)j.put((String)fields[i],fields[i+1]);return j;}catch(Exception e){throw new RuntimeException(e);}}
 static class UI{void post(Runnable r){r.run();}}
 static class Resolver{final Map<Uri,byte[]> files=new HashMap<>();Cursor query(Uri u,String[] columns,Object x,Object y,Object z){return new Cursor(){public boolean moveToFirst(){return true;}public String getString(int n){return u.value;}public void close(){}};}InputStream openInputStream(Uri u){return new ByteArrayInputStream(files.get(u));}}
}
class RemoteFiles {
 static final long MAX=512L*1024*1024;final MainActivity a;final FileBoxUpload upload;Object transfer;boolean supported=true,uploadSupported=true;volatile String info="";
 RemoteFiles(MainActivity a){this.a=a;upload=new FileBoxUpload(this);}void setInfo(String s){info=s;}void render(){}void refresh(boolean p){}void toast(String s){info=s;}String bytes(long n){return Long.toString(n);}
}
class LinkService {void schedulePark(){}}
class SecureClient {
 volatile boolean running=true,failChunk;volatile CountDownLatch arrived,release;FileBoxUpload owner;int cancels;
 final ExecutorService worker=Executors.newSingleThreadExecutor();final Map<String,byte[]> saved=new LinkedHashMap<>();ByteArrayOutputStream current;String name,sha,token;long size;
 static String hex(byte[] bytes){return java.util.HexFormat.of().withUpperCase().formatHex(bytes);}
 void send(JSONObject j){worker.execute(()->{try{
  String op=j.optString("op"),request=j.optString("request");JSONObject reply=MainActivity.obj("type","sharedUploadResult","request",request,"ok",true);
  if(op.equals("sharedUploadBegin")){token=j.optString("upload");name=j.optString("name");size=j.optLong("size");sha=j.optString("sha256");current=new ByteArrayOutputStream();reply.put("offset",0);}
  else if(op.equals("sharedUploadChunk")){
   if(arrived!=null){arrived.countDown();release.await(5,TimeUnit.SECONDS);arrived=null;release=null;}
   if(failChunk){reply.put("ok",false);reply.put("message","시험 실패");}
   else{byte[] bytes=java.util.Base64.getDecoder().decode(j.optString("data"));if(j.optLong("offset")!=current.size())throw new IOException("offset");current.write(bytes);reply.put("offset",current.size());}
  }else if(op.equals("sharedUploadEnd")){byte[] bytes=current.toByteArray();if(bytes.length!=size||!hex(MessageDigest.getInstance("SHA-256").digest(bytes)).equals(sha))throw new IOException("digest");saved.put(name,bytes);reply.put("file",MainActivity.obj("size",size,"sha256",sha));current=null;}
  else if(op.equals("sharedUploadCancel")){cancels++;current=null;}
  owner.message(reply);
 }catch(Exception e){throw new RuntimeException(e);}});}
}
