package com.terminallink.remote;
final class AttachmentAccess {
 static boolean allowed(String source,String thread,boolean readOnly,boolean remoteImages,boolean localImages,boolean hasFiles){
  if(!readOnly)return true;
  if(hasFiles)return false;
  return source.equals("remote")&&thread.startsWith("remote:")&&remoteImages
   ||source.equals("record")&&!thread.startsWith("remote:")&&!thread.isEmpty()&&localImages;
 }
}
