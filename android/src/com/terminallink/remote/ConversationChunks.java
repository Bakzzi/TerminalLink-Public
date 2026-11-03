package com.terminallink.remote;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import android.util.Base64;
import org.json.*;

// Only lives within one authenticated TLS connection. Partial transfers never reach storage/UI.
final class ConversationChunks {
 private String transfer="";private int next;private ByteArrayOutputStream bytes;private MessageDigest hash;
 JSONObject accept(JSONObject packet)throws Exception{
  if(!packet.optString("type").equals("conversationChunk"))return packet;
  String id=packet.optString("transfer");int index=packet.optInt("index",-1);
  if(!id.matches("[0-9a-f]{32}"))throw new IOException("잘못된 대화 전송 식별자");
  if(bytes==null){if(index!=0)throw new IOException("대화 전송 시작 누락");transfer=id;next=0;bytes=new ByteArrayOutputStream();hash=MessageDigest.getInstance("SHA-256");}
  if(!id.equals(transfer)||index!=next)throw new IOException("대화 조각 순서가 바뀌었습니다.");
  String encoded=packet.optString("data");if(encoded.length()>87384)throw new IOException("대화 조각이 너무 큽니다.");
  byte[] part=Base64.decode(encoded,Base64.NO_WRAP);if(part.length==0||part.length>65536)throw new IOException("잘못된 대화 조각");
  bytes.write(part);hash.update(part);next++;
  if(!packet.optBoolean("last"))return null;
  if(!SecureClient.hex(hash.digest()).equals(packet.optString("sha256")))throw new IOException("대화 전송 검증에 실패했습니다.");
  String text=bytes.toString(StandardCharsets.UTF_8.name());bytes=null;hash=null;transfer="";next=0;
  JSONObject result=new JSONObject(text);if(!result.optString("type").equals("terminalText"))throw new IOException("지원하지 않는 대화 전송");return result;
 }
}
