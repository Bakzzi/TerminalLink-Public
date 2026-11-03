package com.terminallink.remote;
import android.content.Context;
import android.content.pm.*;
import org.json.*;
import java.io.*;
import java.security.MessageDigest;
import java.util.*;

final class UpdateApk {
 static final long MAX_SIZE=32L*1024*1024;
 static final class Offer {
  final String packageName,versionName,sha256,notes;final long versionCode,size;
  Offer(JSONObject j)throws Exception{
   packageName=j.getString("packageName");versionName=j.getString("versionName");sha256=j.getString("sha256");notes=j.optString("notes","");
   versionCode=j.getLong("versionCode");size=j.getLong("size");
   if(!packageName.equals("com.terminallink.remote")||versionCode<1||versionCode>Integer.MAX_VALUE||!versionName.matches("\\d+\\.\\d+\\.\\d+")||!sha256.matches("[A-F0-9]{64}")||size<1||size>MAX_SIZE||notes.length()>1500)throw new IOException("잘못된 업데이트 정보");
  }
  JSONObject json(){return MainActivity.obj("packageName",packageName,"versionName",versionName,"versionCode",versionCode,"size",size,"sha256",sha256,"notes",notes);}
 }
 static long installedVersion(Context c)throws Exception{return c.getPackageManager().getPackageInfo(c.getPackageName(),0).getLongVersionCode();}
 static File folder(Context c)throws IOException{File dir=new File(c.getFilesDir(),"updates");if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("업데이트 저장 공간을 만들지 못했습니다.");return dir;}
 static String hash(File f)throws Exception{
  MessageDigest digest=MessageDigest.getInstance("SHA-256");try(InputStream in=new FileInputStream(f)){byte[] b=new byte[65536];int n;while((n=in.read(b))!=-1)digest.update(b,0,n);}return SecureClient.hex(digest.digest());
 }
 static void verify(Context c,File file,Offer offer)throws Exception{
  if(file.length()!=offer.size||!hash(file).equals(offer.sha256))throw new IOException("업데이트 파일 검증에 실패했습니다.");
  PackageManager pm=c.getPackageManager();
  PackageInfo current=pm.getPackageInfo(c.getPackageName(),PackageManager.GET_SIGNING_CERTIFICATES);
  PackageInfo next=pm.getPackageArchiveInfo(file.getAbsolutePath(),PackageManager.GET_SIGNING_CERTIFICATES);
  if(next==null||!c.getPackageName().equals(next.packageName)||next.getLongVersionCode()!=offer.versionCode||!offer.versionName.equals(next.versionName)||next.getLongVersionCode()<=current.getLongVersionCode())throw new IOException("이 앱의 최신 업데이트 파일이 아닙니다.");
  if(current.signingInfo==null||next.signingInfo==null)throw new IOException("앱 서명을 확인할 수 없습니다.");
  Set<String> oldKeys=new HashSet<>(),newKeys=new HashSet<>();
  for(Signature s:current.signingInfo.getApkContentsSigners())oldKeys.add(SecureClient.hex(MessageDigest.getInstance("SHA-256").digest(s.toByteArray())));
  for(Signature s:next.signingInfo.getApkContentsSigners())newKeys.add(SecureClient.hex(MessageDigest.getInstance("SHA-256").digest(s.toByteArray())));
  if(oldKeys.isEmpty()||!oldKeys.equals(newKeys))throw new IOException("등록된 앱 서명과 다른 업데이트입니다.");
 }
 static final class Download implements Closeable {
  final Context context;final Offer offer;final File part;FileOutputStream out;long received;
  Download(Context c,Offer offer)throws Exception{context=c;this.offer=offer;if(offer.versionCode<=installedVersion(c))throw new IOException("이전 버전은 설치할 수 없습니다.");part=new File(folder(c),"download-"+UUID.randomUUID()+".part");out=new FileOutputStream(part);}
  void append(long offset,String encoded)throws Exception{
   if(out==null||offset!=received||encoded.length()>65536)throw new IOException("업데이트 데이터 순서가 올바르지 않습니다.");
   byte[] bytes=android.util.Base64.decode(encoded,android.util.Base64.NO_WRAP);
   if(bytes.length<1||bytes.length>49152||received+bytes.length>offer.size)throw new IOException("업데이트 데이터 크기가 올바르지 않습니다.");
   out.write(bytes);received+=bytes.length;
  }
  File finish()throws Exception{
   if(out==null||received!=offer.size)throw new IOException("업데이트 파일이 아직 완성되지 않았습니다.");
   out.getFD().sync();out.close();out=null;verify(context,part,offer);
   File target=new File(folder(context),offer.sha256+".apk");
   if(target.exists()&&!target.delete())throw new IOException("이전 다운로드를 교체할 수 없습니다.");
   if(!part.renameTo(target))throw new IOException("업데이트 파일 저장에 실패했습니다.");return target;
  }
  public void close(){try{if(out!=null)out.close();}catch(IOException ignored){}out=null;part.delete();}
 }
}
