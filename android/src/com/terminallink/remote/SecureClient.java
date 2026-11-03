package com.terminallink.remote;

import android.content.Context;
import android.os.Build;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import java.io.*;
import java.net.*;
import java.security.*;
import java.security.cert.*;
import java.util.*;
import java.util.concurrent.*;
import javax.net.ssl.*;
import javax.security.auth.x500.X500Principal;
import org.json.*;

final class SecureClient {
 interface Listener {void json(JSONObject json);void frame(byte[] bytes);void ended(String reason);}
 final Listener listener;
 final String host,pin,token;
 final int port;
 volatile boolean running=true;volatile boolean modern;volatile long lastSent,lastReceived;
 volatile SSLSocket socket;
 final Object lifecycle=new Object();Socket transport;boolean started;
 DataInputStream input;
 DataOutputStream output;
 final ExecutorService writer=Executors.newSingleThreadExecutor();
 final ScheduledExecutorService timer=Executors.newSingleThreadScheduledExecutor();
 private static final String ALIAS="terminallink-device-v1-tls";
 SecureClient(String host,int port,String pin,String token,Listener l){this.host=host;this.port=port;this.pin=pin;this.token=token;listener=l;}
 static KeyStore keys() throws Exception {
  KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
  if(!store.containsAlias(ALIAS)){
   KeyPairGenerator g=KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC,"AndroidKeyStore");
   Calendar end=Calendar.getInstance();end.add(Calendar.YEAR,10);
   g.initialize(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_SIGN|KeyProperties.PURPOSE_VERIFY)
    .setAlgorithmParameterSpec(new java.security.spec.ECGenParameterSpec("secp256r1"))
    .setDigests(KeyProperties.DIGEST_NONE,KeyProperties.DIGEST_SHA256,KeyProperties.DIGEST_SHA384,KeyProperties.DIGEST_SHA512)
    .setUserAuthenticationRequired(false).setCertificateSubject(new X500Principal("CN=TerminalLink Android"))
    .setCertificateSerialNumber(new java.math.BigInteger(120,new SecureRandom()))
    .setCertificateNotBefore(new Date(System.currentTimeMillis()-86400000L)).setCertificateNotAfter(end.getTime()).build());
   g.generateKeyPair();store.load(null);
  }
  return store;
 }
 static String hex(byte[] value){StringBuilder b=new StringBuilder();for(byte v:value)b.append(String.format(Locale.ROOT,"%02X",v&255));return b.toString();}
 static String fingerprint() throws Exception{return hex(MessageDigest.getInstance("SHA-256").digest(keys().getCertificate(ALIAS).getEncoded()));}
 void start(){synchronized(lifecycle){if(!running||started)return;started=true;}new Thread(this::run,"TerminalLink-network").start();}
 boolean own(Socket candidate){synchronized(lifecycle){if(!running)return false;if(candidate instanceof SSLSocket)socket=(SSLSocket)candidate;else transport=candidate;return true;}}
 private void run(){
  String reason="연결 종료";
  try{
   if(!running)return;
   KeyStore store=keys();
   final X509Certificate cert=(X509Certificate)store.getCertificate(ALIAS);
   final PrivateKey key=(PrivateKey)store.getKey(ALIAS,null);
   X509KeyManager km=new X509KeyManager(){
    public String[] getClientAliases(String k,Principal[] i){return new String[]{ALIAS};}
    public String chooseClientAlias(String[] k,Principal[] i,Socket s){for(String a:k)if(a.startsWith("EC"))return ALIAS;return null;}
    public String[] getServerAliases(String k,Principal[] i){return null;}
    public String chooseServerAlias(String k,Principal[] i,Socket s){return null;}
    public X509Certificate[] getCertificateChain(String a){return new X509Certificate[]{cert};}
    public PrivateKey getPrivateKey(String a){return key;}
   };
   X509TrustManager tm=new X509TrustManager(){
    public X509Certificate[] getAcceptedIssuers(){return new X509Certificate[0];}
    public void checkClientTrusted(X509Certificate[] c,String a)throws CertificateException{throw new CertificateException("Not a server");}
    public void checkServerTrusted(X509Certificate[] chain,String auth)throws CertificateException{
     try{if(chain==null||chain.length==0||!hex(MessageDigest.getInstance("SHA-256").digest(chain[0].getEncoded())).equals(pin))throw new CertificateException("PC 인증서가 등록 정보와 다릅니다.");chain[0].checkValidity();}
     catch(CertificateException e){throw e;}catch(Exception e){throw new CertificateException(e);}
    }
   };
   SSLContext context=SSLContext.getInstance("TLS");context.init(new KeyManager[]{km},new TrustManager[]{tm},new SecureRandom());
   Socket tcp=new Socket();if(!own(tcp)){tcp.close();return;}
   tcp.connect(new InetSocketAddress(host,port),10000);tcp.setTcpNoDelay(true);
   SSLSocket tls=(SSLSocket)context.getSocketFactory().createSocket(tcp,host,port,true);
   if(!own(tls)){tls.close();return;}
   tls.setEnabledProtocols(new String[]{"TLSv1.3","TLSv1.2"});tls.setSoTimeout(65000);
   tls.startHandshake();
   if(!running)return;
   input=new DataInputStream(new BufferedInputStream(tls.getInputStream()));output=new DataOutputStream(new BufferedOutputStream(tls.getOutputStream()));
   sendNow(new JSONObject().put("op","hello").put("view","text").put("updates",1).put("events",1).put("conversationChunks",1).put("token",token==null?"":token).put("name",Build.MANUFACTURER+" "+Build.MODEL));
   scheduleHeartbeat(5000);
   ConversationChunks chunks=new ConversationChunks();
   while(running){
    int length=input.readInt();
    if(length<1||length>32*1024*1024)throw new IOException("잘못된 메시지 길이");
    int type=input.readUnsignedByte();
    if(type!=1&&type!=2)throw new IOException("지원하지 않는 메시지");
    if(type==1&&length>8*1024*1024)throw new IOException("메시지가 너무 큽니다.");
    byte[] data=new byte[length-1];input.readFully(data);lastReceived=android.os.SystemClock.elapsedRealtime();
    if(type==1){JSONObject message=chunks.accept(new JSONObject(new String(data,java.nio.charset.StandardCharsets.UTF_8)));if(message==null)continue;if(message.optString("type").equals("ready")&&message.optInt("events")==1){modern=true;tls.setSoTimeout(150000);}if(!message.optString("type").equals("ping"))listener.json(message);}else listener.frame(data);
   }
  }catch(Exception e){reason=e.getMessage()==null?e.getClass().getSimpleName():e.getMessage();}
  finally{boolean notify=running;close();if(notify)listener.ended(reason);}
 }
 void scheduleHeartbeat(long delay){try{timer.schedule(()->{if(!running)return;long interval=modern?45000:5000;long remaining=interval-(android.os.SystemClock.elapsedRealtime()-Math.min(lastSent,lastReceived));if(remaining<=0){send(MainActivity.obj("op","ping"));remaining=interval;}scheduleHeartbeat(remaining);},delay,TimeUnit.MILLISECONDS);}catch(RejectedExecutionException ignored){}}
 void send(JSONObject value){
  if(!running)return;
  try{writer.execute(()->{try{sendNow(value);}catch(Exception e){boolean notify=running;close();if(notify)listener.ended(e.getMessage());}});}catch(RejectedExecutionException ignored){}
 }
 private synchronized void sendNow(JSONObject value)throws Exception{
  if(!running||output==null)return;
  byte[] data=value.toString().getBytes(java.nio.charset.StandardCharsets.UTF_8);
  if(data.length>32767)throw new IOException("입력 내용이 너무 깁니다.");
  output.writeInt(data.length+1);output.writeByte(1);output.write(data);output.flush();lastSent=android.os.SystemClock.elapsedRealtime();
 }
 void sendAndClose(JSONObject value){try{writer.execute(()->{try{sendNow(value);}catch(Exception ignored){}finally{close();}});}catch(RejectedExecutionException e){close();}}
 void close(){
  Socket tcp;SSLSocket tls;
  // Do not use the send lock here: closing the socket must interrupt a blocked write/handshake.
  synchronized(lifecycle){running=false;tcp=transport;transport=null;tls=socket;socket=null;}
  try{if(tcp!=null)tcp.close();}catch(Exception ignored){}
  try{if(tls!=null)tls.close();}catch(Exception ignored){}
  timer.shutdownNow();writer.shutdownNow();
 }
}