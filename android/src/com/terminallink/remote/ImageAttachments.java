package com.terminallink.remote;
import android.content.*;
import android.graphics.*;
import android.net.Uri;
import android.provider.MediaStore;
import android.view.*;
import android.widget.*;
import org.json.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;

// Decode and stream off the UI thread. A bounded chunk window prevents a slow connection from queuing whole photos.
final class ImageAttachments {
 static final int PICK=14,FILE_PICK=15,MAX=4,MAX_BYTES=8*1024*1024,MAX_FILE_BYTES=32*1024*1024;
 final MainActivity a;final LinearLayout tray;final HorizontalScrollView preview;final Button add;
 final ArrayList<Photo> photos=new ArrayList<>();
 final ExecutorService worker=Executors.newSingleThreadExecutor();
 final ConcurrentHashMap<String,CompletableFuture<JSONObject>> pending=new ConcurrentHashMap<>();
 volatile boolean closed,busy;boolean loading,supported,filesSupported,remoteSupported,localSupported;
 android.app.AlertDialog errorDialog,pickerDialog;
 String boundSession="",boundPin="";String progress="";
 static final class Photo {
  final File file;final Bitmap thumb;final String name;
  Photo(File f,Bitmap t){this(f,t,f.getName());}
  Photo(File f,Bitmap t,String n){file=f;thumb=t;name=n;}
  boolean image(){return thumb!=null;}
  void close(){file.delete();if(thumb!=null)thumb.recycle();}
 }
 ImageAttachments(MainActivity activity){
  a=activity;tray=a.row();tray.setTag("image-tray");tray.setVisibility(View.GONE);preview=new HorizontalScrollView(a);preview.setHorizontalScrollBarEnabled(false);preview.addView(tray);preview.setVisibility(View.GONE);
  add=a.button("＋",v->pick());add.setTag("attach-image");add.setTextSize(23);add.setContentDescription("사진·파일 첨부");add.setTooltipText("사진·파일 첨부");
  File dir=new File(a.getCacheDir(),"image-drafts");dir.mkdirs();
  File[] old=dir.listFiles();if(old!=null)for(File f:old)if(f.lastModified()<System.currentTimeMillis()-86400000L)f.delete();
 }
 boolean hasPhotos(){return !photos.isEmpty();}
 void pick(){
  if(busy||loading)return;
  pickerDialog=new android.app.AlertDialog.Builder(a).setTitle("첨부").setItems(new String[]{"사진","파일"},(dialog,which)->{if(which==0)pickImages();else pickFiles();}).show();
 }
 void pickFiles(){
  if(busy||loading)return;
  try{a.startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("*/*").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true),FILE_PICK);}
  catch(ActivityNotFoundException e){a.setStatus("파일 선택 앱을 찾을 수 없습니다.");}
 }
 void pickImages(){
  if(busy||loading)return;
  Intent intent;
  if(android.os.Build.VERSION.SDK_INT>=33)intent=new Intent(MediaStore.ACTION_PICK_IMAGES).setType("image/*").putExtra(MediaStore.EXTRA_PICK_IMAGES_MAX,MAX);
  else intent=new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("image/*").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true);
  try{a.startActivityForResult(intent,PICK);}catch(ActivityNotFoundException e){a.startActivityForResult(new Intent(Intent.ACTION_OPEN_DOCUMENT).setType("image/*").addCategory(Intent.CATEGORY_OPENABLE).putExtra(Intent.EXTRA_ALLOW_MULTIPLE,true),PICK);}
 }
 void accept(Intent intent,boolean shared){
  if(intent==null||busy||loading)return;
  String action=intent.getAction();
  if(shared&&!Intent.ACTION_SEND.equals(action)&&!Intent.ACTION_SEND_MULTIPLE.equals(action))return;
  ArrayList<Uri> uris=new ArrayList<>();
  try{
   if(shared){
    if(Intent.ACTION_SEND.equals(action)){Uri u=intent.getParcelableExtra(Intent.EXTRA_STREAM);if(u!=null)uris.add(u);}
    else {ArrayList<Uri> list=intent.getParcelableArrayListExtra(Intent.EXTRA_STREAM);if(list!=null)uris.addAll(list);}
   }
   ClipData clip=intent.getClipData();if(clip!=null)for(int i=0;i<clip.getItemCount()&&uris.size()<MAX+1;i++){Uri u=clip.getItemAt(i).getUri();if(u!=null&&!uris.contains(u))uris.add(u);}
   if(!shared&&intent.getData()!=null&&!uris.contains(intent.getData()))uris.add(intent.getData());
  }catch(RuntimeException e){a.setStatus("공유한 파일을 읽을 수 없습니다.");return;}
  if(uris.isEmpty())return;
  if(photos.size()+uris.size()>MAX){a.setStatus("사진과 파일은 합쳐서 4개까지 첨부할 수 있습니다.");return;}
  if(shared){a.full(false);a.connectionSettings.close();}
  if(photos.isEmpty()){boundSession=a.selectedId;boundPin=a.pin;}
  loading=true;progress="첨부 준비 중…";render();
  worker.execute(()->{
   ArrayList<Photo> added=new ArrayList<>();String error="";
   try{for(Uri uri:uris){if(closed||Thread.currentThread().isInterrupted())throw new IOException("첨부 준비가 취소됐습니다.");added.add(prepare(uri));}}
   catch(Exception e){error=e instanceof FileTooLarge?"파일은 개당 32MB까지 첨부할 수 있습니다.":"첨부를 읽지 못했습니다. 다른 파일을 선택해 주세요.";for(Photo p:added)p.close();added.clear();}
   if(closed){for(Photo p:added)p.close();return;}
   final String why=error;a.ui.post(()->{if(closed){for(Photo p:added)p.close();return;}photos.addAll(added);loading=false;progress=why;render();if(!why.isEmpty())a.setStatus(why);});
  });
 }
 static final class FileTooLarge extends IOException {}
 String displayName(Uri uri){
  String name="";
  try(android.database.Cursor cursor=a.getContentResolver().query(uri,new String[]{android.provider.OpenableColumns.DISPLAY_NAME},null,null,null)){
   if(cursor!=null&&cursor.moveToFirst())name=cursor.getString(0);
  }catch(RuntimeException ignored){}
  if(name==null||name.trim().isEmpty())name="attachment.bin";
  name=name.replaceAll("[\\p{Cntrl}]","").trim();return name.length()>160?name.substring(0,160):name;
 }
 Photo prepare(Uri uri)throws IOException{
  if(!"content".equals(uri.getScheme()))throw new IOException("Only granted content URIs are accepted");
  String name=displayName(uri),mime=a.getContentResolver().getType(uri);
  boolean image=mime!=null&&mime.startsWith("image/");
  if(!image)return prepareFile(uri,name);
  return preparePhoto(uri,name);
 }
 Photo prepareFile(Uri uri,String name)throws IOException{
  File file=File.createTempFile("file-",".draft",new File(a.getCacheDir(),"image-drafts"));boolean ok=false;
  try{
   try(InputStream in=a.getContentResolver().openInputStream(uri);OutputStream out=new FileOutputStream(file)){
    if(in==null)throw new IOException("Unreadable file");
    byte[] buffer=new byte[32768];long total=0;int n;
    while((n=in.read(buffer))!=-1){
     if(closed||Thread.currentThread().isInterrupted())throw new IOException("Cancelled");
     total+=n;if(total>MAX_FILE_BYTES)throw new FileTooLarge();out.write(buffer,0,n);
    }
   }
   ok=true;return new Photo(file,null,name);
  }finally{if(!ok)file.delete();}
 }
 Photo preparePhoto(Uri uri,String name)throws IOException{
  Bitmap bitmap=ImageDecoder.decodeBitmap(ImageDecoder.createSource(a.getContentResolver(),uri),(decoder,info,source)->{
   int w=info.getSize().getWidth(),h=info.getSize().getHeight();
   if(w<1||h<1||w>40000||h>40000)throw new IllegalArgumentException("Invalid image size");
   double scale=Math.min(1,3072.0/Math.max(w,h));decoder.setTargetSize(Math.max(1,(int)(w*scale)),Math.max(1,(int)(h*scale)));
   decoder.setAllocator(ImageDecoder.ALLOCATOR_SOFTWARE);
  });
  File file=File.createTempFile("photo-",".png",new File(a.getCacheDir(),"image-drafts"));
  Bitmap thumb=null;boolean ok=false;
  try{
   try(OutputStream out=new FileOutputStream(file)){if(!bitmap.compress(Bitmap.CompressFormat.PNG,100,out))throw new IOException("PNG");}
   if(file.length()>MAX_BYTES)try(OutputStream out=new FileOutputStream(file)){if(!bitmap.compress(Bitmap.CompressFormat.JPEG,92,out))throw new IOException("JPEG");}
   if(file.length()>MAX_BYTES)throw new IOException("Photo too large");
   float scale=Math.min(1f,160f/Math.max(bitmap.getWidth(),bitmap.getHeight()));
   thumb=Bitmap.createScaledBitmap(bitmap,Math.max(1,Math.round(bitmap.getWidth()*scale)),Math.max(1,Math.round(bitmap.getHeight()*scale)),true);
   if(thumb==bitmap)thumb=bitmap.copy(Bitmap.Config.ARGB_8888,false);
   ok=true;return new Photo(file,thumb,name);
  }finally{bitmap.recycle();if(!ok){file.delete();if(thumb!=null)thumb.recycle();}}
 }
 void render(){
  if(closed)return;
  if(a.terminal!=null)a.terminal.preserveLayout();
  tray.removeAllViews();int visibility=photos.isEmpty()&&!loading&&!busy&&progress.isEmpty()?View.GONE:View.VISIBLE;tray.setVisibility(visibility);preview.setVisibility(visibility);
  for(Photo p:photos){
   FrameLayout tile=new FrameLayout(a);
   if(p.image()){ImageView image=new ImageView(a);image.setImageBitmap(p.thumb);image.setScaleType(ImageView.ScaleType.CENTER_CROP);image.setContentDescription("첨부 사진 · "+p.name);tile.addView(image,new FrameLayout.LayoutParams(a.dp(58),a.dp(58)));}
   else{LinearLayout card=new LinearLayout(a);card.setOrientation(LinearLayout.VERTICAL);card.setPadding(a.dp(5),a.dp(2),a.dp(20),0);card.setBackgroundColor(0xff253449);
    TextView name=a.label(p.name,12,a.FG);name.setMaxLines(2);name.setEllipsize(android.text.TextUtils.TruncateAt.MIDDLE);card.addView(name);
    TextView size=a.label(android.text.format.Formatter.formatShortFileSize(a,p.file.length()),10,a.MUTED);card.addView(size);
    card.setContentDescription(p.name+" · "+size.getText());card.setTooltipText(p.name+" · "+size.getText());tile.addView(card,new FrameLayout.LayoutParams(a.dp(146),a.dp(58)));
   }
   Button remove=a.button("×",v->{if(!busy&&!loading){photos.remove(p);p.close();if(photos.isEmpty()){boundSession="";boundPin="";}progress="";render();}});
   remove.setContentDescription(p.name+" 첨부 삭제");remove.setTextSize(18);remove.setMinWidth(0);remove.setMinimumWidth(0);remove.setPadding(0,0,0,0);remove.setEnabled(!busy&&!loading);
   FrameLayout.LayoutParams lp=new FrameLayout.LayoutParams(a.dp(28),a.dp(28),Gravity.TOP|Gravity.RIGHT);tile.addView(remove,lp);tray.addView(tile,new LinearLayout.LayoutParams(a.dp(p.image()?64:152),a.dp(62)));
  }
  TextView label=a.label(progress.isEmpty()?photos.size()+"개":progress,11,a.MUTED);label.setTag("image-progress");tray.addView(label,new LinearLayout.LayoutParams(0,-2,1));
  add.setEnabled(!busy&&!loading&&photos.size()<MAX);a.compose.setEnabled(!busy);a.updateCreateButton();
 }
 void message(JSONObject j){CompletableFuture<JSONObject> wait=pending.remove(j.optString("request"));if(wait!=null)wait.complete(j);}
 static final class Exchange {final String id;final CompletableFuture<JSONObject> reply=new CompletableFuture<>();long offset;Exchange(String value){id=value;}}
 Exchange begin(SecureClient client,JSONObject j)throws Exception{
  if(closed||!client.running||Thread.currentThread().isInterrupted())throw new IOException("전송이 취소됐습니다.");
  Exchange exchange=new Exchange(UUID.randomUUID().toString());j.put("request",exchange.id);pending.put(exchange.id,exchange.reply);if(!client.running){pending.remove(exchange.id);throw new IOException("연결이 끊겼습니다. 대화에서 전송 여부를 확인하세요.");}client.send(j);return exchange;
 }
 JSONObject finish(Exchange exchange)throws Exception{return finish(exchange,20);}
 JSONObject finish(Exchange exchange,int seconds)throws Exception{
  try{JSONObject reply=exchange.reply.get(seconds,TimeUnit.SECONDS);if(!reply.optBoolean("ok"))throw new IOException(reply.optString("message","첨부를 보내지 못했습니다."));return reply.optJSONObject("data");}
  finally{pending.remove(exchange.id);}
 }
 JSONObject request(SecureClient client,JSONObject j)throws Exception{return finish(begin(client,j));}
 void send(){
  if(loading||busy){a.setStatus("첨부를 준비하거나 보내고 있습니다.");return;}
  if(!a.inputReady())return;
  boolean hasFiles=false;for(Photo p:photos)if(!p.image())hasFiles=true;
  if(hasFiles&&!filesSupported){a.setStatus("파일 첨부에는 PC 프로그램 1.9.3 이상이 필요합니다.");return;}
  if(!hasFiles&&!supported){a.setStatus("사진 첨부에는 PC 프로그램 1.9.0 이상이 필요합니다.");return;}
  if(!boundPin.isEmpty()&&!boundPin.equals(a.pin)||!boundSession.isEmpty()&&!boundSession.equals(a.selectedId)){a.setStatus("파일을 선택했던 탭으로 돌아가거나 다시 첨부하세요.");return;}
  String text=a.compose.getText().toString();if(text.length()>4096){a.setStatus("설명은 4096자 이내로 입력하세요.");return;}
  final boolean remote=a.terminal.source.equals("remote")&&a.terminal.thread.startsWith("remote:");
  if(!AttachmentAccess.allowed(a.terminal.source,a.terminal.thread,a.terminal.readOnly,remoteSupported,localSupported,hasFiles)){a.setStatus(remote?"SSH 원격 사진 첨부에는 PC·앱 0.9.16 이상이 필요합니다. 일반 파일은 아직 지원하지 않습니다.":"이 대화에서는 첨부를 보낼 수 없습니다.");return;}
  if(a.terminal.thread.isEmpty()){a.setStatus("첨부는 Codex 대화가 연결된 탭에서 보낼 수 있습니다.");return;}
  final SecureClient connection=a.client;final String target=a.selectedId,thread=a.terminal.thread;
  ArrayList<Photo> batch=new ArrayList<>(photos);
  busy=true;progress="전송 준비…";render();
  worker.execute(()->{
   ArrayList<String> ids=new ArrayList<>(),fileIds=new ArrayList<>();String error="";boolean committed=false;
   try{
    int index=0;
    for(Photo p:batch){
     String id=UUID.randomUUID().toString();if(p.image())ids.add(id);else fileIds.add(id);String prefix=p.image()?"image":"file";
     java.security.MessageDigest hash=java.security.MessageDigest.getInstance("SHA-256");
     byte[] buffer=new byte[12288];try(InputStream in=new FileInputStream(p.file)){int n;while((n=in.read(buffer))>0)hash.update(buffer,0,n);}
     request(connection,MainActivity.obj("op",prefix+"Begin",prefix,id,"size",p.file.length(),"sha256",SecureClient.hex(hash.digest()),"name",p.name));
     long offset=0;final int photoIndex=++index;
     try(InputStream in=new FileInputStream(p.file)){
      ArrayDeque<Exchange> window=new ArrayDeque<>();boolean eof=false;
      while(!eof||!window.isEmpty()){
       // At most 96 KiB of file data waits for acknowledgements even on a slow network.
       while(!eof&&window.size()<8){int n=in.read(buffer);if(n<0){eof=true;break;}
        Exchange e=begin(connection,MainActivity.obj("op",prefix+"Chunk",prefix,id,"offset",offset,"data",android.util.Base64.encodeToString(buffer,0,n,android.util.Base64.NO_WRAP)));offset+=n;e.offset=offset;window.add(e);
       }
       if(window.isEmpty())break;
       Exchange e=window.remove();JSONObject reply=finish(e);if(reply==null||reply.optLong("offset")!=e.offset)throw new IOException("첨부 전송 확인값이 다릅니다.");
       final int percent=(int)(100*e.offset/p.file.length());a.ui.post(()->{if(!closed&&busy){progress=photoIndex+"/"+batch.size()+" · "+percent+"%";TextView v=tray.findViewWithTag("image-progress");if(v!=null)v.setText(progress);}});
      }
     }
     request(connection,MainActivity.obj("op",prefix+"End",prefix,id));
    }
    committed=true;
    a.ui.post(()->{if(!closed&&busy){progress=remote?"원격 Codex에 사진 첨부 중…":"첨부 확인 중…";TextView v=tray.findViewWithTag("image-progress");if(v!=null)v.setText(progress);}});
    finish(begin(connection,MainActivity.obj("op",fileIds.isEmpty()?"imageSend":"attachmentSend","id",target,"thread",thread,"images",new JSONArray(ids),"files",new JSONArray(fileIds),"text",text)),remote?110:20);
   }catch(Exception e){error=e instanceof TimeoutException?"전송 결과를 확인하지 못했습니다. 대화와 터미널 입력칸을 확인하세요.":e.getMessage();if(error==null)error="연결이 끊겨 첨부를 보내지 못했습니다.";}
   finally{pending.clear();if(!committed||!error.isEmpty()){for(String id:ids)connection.send(MainActivity.obj("op","imageCancel","image",id,"request",UUID.randomUUID().toString()));for(String id:fileIds)connection.send(MainActivity.obj("op","fileCancel","file",id,"request",UUID.randomUUID().toString()));}}
   final String why=error;a.ui.post(()->{
    if(closed)return;busy=false;
    if(why.isEmpty()){for(Photo p:batch){photos.remove(p);p.close();}if(a.compose.getText().toString().equals(text))a.compose.setText("");boundSession="";boundPin="";progress="";a.setStatus("첨부와 메시지를 보냈습니다.");}
    else{progress="전송 실패 · 첨부는 유지됨";a.setStatus(why);errorDialog=new android.app.AlertDialog.Builder(a).setTitle("첨부 전송 확인").setMessage(why).setPositiveButton("확인",null).show();}
    render();
   });
  });
 }
 void disconnected(){supported=false;filesSupported=false;remoteSupported=false;localSupported=false;for(CompletableFuture<JSONObject> wait:pending.values())wait.completeExceptionally(new IOException("연결이 끊겼습니다. 대화에서 전송 여부를 확인하세요."));pending.clear();}
 void close(){closed=true;if(errorDialog!=null)errorDialog.dismiss();if(pickerDialog!=null)pickerDialog.dismiss();disconnected();worker.shutdownNow();for(Photo p:photos)p.close();photos.clear();tray.removeAllViews();}
}
