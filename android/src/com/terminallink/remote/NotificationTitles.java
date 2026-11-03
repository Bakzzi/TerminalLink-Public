package com.terminallink.remote;
final class NotificationTitles {
 static String answer(String title){
  String tab=title==null?"":title.replaceAll("[\\p{Cntrl}\\s]+"," ").replaceFirst("^[\\s\\u2800-\\u28ff]+","").trim();
  return (tab.isEmpty()?"터미널":tab)+" 답변 도착";
 }
 static CharSequence summary(CharSequence child,String fallback){return child==null||child.length()==0?fallback:child;}
}
