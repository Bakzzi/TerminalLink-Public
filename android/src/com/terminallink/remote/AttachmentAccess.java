package com.terminallink.remote;
final class AttachmentAccess {
 static boolean allowed(String source,String thread,boolean readOnly,boolean remoteImages,boolean localImages,boolean hasFiles){
  return allowed(source,thread,readOnly,remoteImages,localImages,hasFiles,false,false);
 }
 static boolean allowed(String source,String thread,boolean readOnly,boolean remoteImages,boolean localImages,boolean hasFiles,boolean remoteFiles,boolean localFiles){
  if(source.equals("remote")&&thread.startsWith("remote:"))return hasFiles?remoteFiles:remoteImages;
  if(!readOnly)return true;
  return source.equals("record")&&!thread.startsWith("remote:")&&!thread.isEmpty()&&(hasFiles?localFiles:localImages);
 }
}
