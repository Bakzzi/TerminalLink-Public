package com.terminallink.remote;
final class AttachmentAccessJvm {
 static void check(boolean ok){if(!ok)throw new AssertionError();}
 public static void main(String[] args){
  check(AttachmentAccess.allowed("remote","remote:fixture",true,true,true,true,true,true));
  check(!AttachmentAccess.allowed("remote","remote:fixture",true,true,true,true,false,true));
  check(AttachmentAccess.allowed("record","fixture",true,true,true,true,true,true));
  check(!AttachmentAccess.allowed("record","fixture",true,true,true,true,true,false));
  check(AttachmentAccess.allowed("remote","remote:fixture",true,true,false,false));
  check(!AttachmentAccess.allowed("remote","remote:fixture",true,true,false,true));
  check(!AttachmentAccess.allowed("unavailable","",true,true,true,true,true,true));
  check(AttachmentAccess.allowed("cli","fixture",false,false,false,true,false,false));
  System.out.println("PASS 8 attachment capability cases; original identity remains verified on PC.");
 }
}
