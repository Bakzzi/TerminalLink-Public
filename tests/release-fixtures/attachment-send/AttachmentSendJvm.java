package com.terminallink.remote;
import org.json.*;import java.nio.file.*;import java.util.*;import java.util.concurrent.*;

public class AttachmentSendJvm {
 static void check(boolean value,String message){if(!value)throw new AssertionError(message);System.out.println("PASS "+message);}
 public static void main(String[] args)throws Exception{
  for(int count:new int[]{3,4})for(boolean mixed:new boolean[]{false,true})for(boolean remote:new boolean[]{false,true})for(boolean failure:new boolean[]{false,true})run(count,mixed,remote,failure);
 }
 static void run(int count,boolean mixed,boolean remote,boolean failure)throws Exception{
  MainActivity a=new MainActivity();if(remote){a.terminal.source="remote";a.terminal.thread="remote:fixture";}
  ImageAttachments images=new ImageAttachments(a);a.client.owner=images;a.client.failure=failure;a.client.expected=count;
  Path folder=Files.createTempDirectory("tl-attachment-fixture-");String caption="Three attachments\n한글 😀";
  try{
   for(int i=0;i<count;i++){Path p=folder.resolve(i==count-1&&mixed?"report.pdf":i+".png");Files.write(p,new byte[25000]);images.photos.add(new ImageAttachments.Photo(p.toFile(),!(mixed&&i==count-1)));}
   a.compose.value=caption;a.status="previous error";images.send();
   check(a.client.lastEndSeen.await(5,TimeUnit.SECONDS),"All "+count+" files reach their upload-completion handshake");
   check(images.busy&&a.client.deliveries==0&&images.photos.size()==count&&!a.status.equals("previous error"),"A pending upload receipt keeps one send active and clears the previous error");
   images.send();check(a.client.deliveries==0,"Second click during upload cannot start another submission");
   a.client.releaseEnd.countDown();check(a.client.deliverySeen.await(5,TimeUnit.SECONDS),"One click continues automatically from uploaded files into native delivery");
   JSONObject sent=a.client.delivery;
   check(a.client.deliveries==1&&a.client.ended.size()==count&&sent.optString("text").equals(caption)&&sent.optString("thread").equals(a.terminal.thread)&&sent.optJSONArray("images").length()==(mixed?count-1:count),"Delivery preserves the chosen target, all attachments and exact Unicode caption");
   check(sent.optString("op").equals(mixed?"attachmentSend":"imageSend"),"Mixed documents use the same ordered upload-then-delivery pipeline");
   check(images.busy&&images.photos.size()==count,"Native target preparation can wait without cancelling or re-uploading attachments");
   a.client.releaseDelivery.countDown();images.worker.submit(()->{}).get(5,TimeUnit.SECONDS);
   check(!images.busy&&a.client.deliveries==1&&(failure?images.photos.size()==count&&a.compose.value.equals(caption)&&a.confirmed==0:images.photos.isEmpty()&&a.confirmed==1&&a.link.sentMessages.count==1),failure?"Definite failure preserves attachments/caption without resending":"One successful receipt clears attachments and records the sent message once");
  }finally{
   a.client.releaseEnd.countDown();a.client.releaseDelivery.countDown();images.closed=true;images.worker.shutdownNow();a.client.worker.shutdownNow();
   images.worker.awaitTermination(2,TimeUnit.SECONDS);a.client.worker.awaitTermination(2,TimeUnit.SECONDS);
   for(Path p:Files.newDirectoryStream(folder))Files.deleteIfExists(p);Files.deleteIfExists(folder);
  }
 }
}
class MainActivity {
 String pin="fixture-pc",selectedId="fixture-console",status="";int confirmed;
 final UI ui=new UI();final Editor compose=new Editor();final Terminal terminal=new Terminal();final LinkService link=new LinkService();final SecureClient client=new SecureClient();
 static JSONObject obj(Object... fields){JSONObject o=new JSONObject();try{for(int i=0;i<fields.length;i+=2)o.put((String)fields[i],fields[i+1]);return o;}catch(Exception e){throw new RuntimeException(e);}}
 boolean inputReady(){return true;}void setStatus(String s){status=s;}void sentMessage(String id,String thread,String text,boolean pending){confirmed++;}
 static class UI{void post(Runnable r){r.run();}}static class Editor{String value="";String getText(){return value;}}
 static class Terminal{String source="record",thread="11111111-1111-4111-8111-111111111111";boolean readOnly=true;JSONArray originalMessages=new JSONArray();}
}
class SentEchoes {int count;static long last(JSONArray a){return 0;}void add(String id,String thread,long after,String text,String[] files){count++;}}
class LinkService {final SentEchoes sentMessages=new SentEchoes();}
class SecureClient {
 volatile boolean running=true,failure;ImageAttachments owner;volatile int deliveries;int expected;volatile JSONObject delivery;
 final ExecutorService worker=Executors.newSingleThreadExecutor();final Set<String> ended=new HashSet<>();
 final CountDownLatch lastEndSeen=new CountDownLatch(1),releaseEnd=new CountDownLatch(1),deliverySeen=new CountDownLatch(1),releaseDelivery=new CountDownLatch(1);
 static String hex(byte[] bytes){return java.util.HexFormat.of().formatHex(bytes);}
 void send(JSONObject j){worker.execute(()->{try{
  String op=j.optString("op");JSONObject data=MainActivity.obj("offset",0);boolean ok=true;
  if(op.endsWith("Chunk")){byte[] b=java.util.Base64.getDecoder().decode(j.optString("data"));data=MainActivity.obj("offset",j.optLong("offset")+b.length);}
  if(op.endsWith("End")){ended.add(j.optString(op.startsWith("file")?"file":"image"));if(ended.size()==expected){lastEndSeen.countDown();releaseEnd.await(5,TimeUnit.SECONDS);}data=MainActivity.obj("ready",true);}
  if(op.equals("imageSend")||op.equals("attachmentSend")){deliveries++;delivery=j;deliverySeen.countDown();releaseDelivery.await(5,TimeUnit.SECONDS);ok=!failure;}
  owner.message(MainActivity.obj("request",j.optString("request"),"ok",ok,"data",data,"message",ok?"sent":"fixture target changed"));
 }catch(Exception e){throw new RuntimeException(e);}});}
}
