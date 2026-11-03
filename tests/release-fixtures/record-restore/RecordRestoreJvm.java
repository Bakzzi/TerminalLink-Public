package com.terminallink.remote;
import org.json.*;import java.nio.file.*;import java.io.*;
public class RecordRestoreJvm {
 static void check(boolean value,String label){if(!value)throw new AssertionError(label);System.out.println("PASS "+label);}
 public static void main(String[] args)throws Exception {
  ConversationChunks receiver=new ConversationChunks();JSONObject decoded=null;
  try(BufferedReader r=Files.newBufferedReader(Paths.get(args[0]))){String line;while((line=r.readLine())!=null){JSONObject packet=new JSONObject(line);decoded=receiver.accept(packet);if(!packet.optBoolean("last")&&decoded!=null)throw new AssertionError("Partial history leaked");}}
  JSONArray m=decoded.getJSONObject("conversation").getJSONArray("messages");check(m.length()==1002&&m.getJSONObject(0).getString("text").startsWith("answer 0")&&m.getJSONObject(1000).getString("text").length()==8*1024*1024+7,"Real host wire to production Android receiver preserves all history");
  check(decoded.getJSONObject("conversation").getBoolean("readOnly")&&decoded.getJSONObject("conversation").getString("source").equals("remote"),"Original-source metadata preserved");
  JSONObject p=new JSONObject().put("type","conversationChunk").put("transfer","11111111111111111111111111111111").put("index",1).put("data","eA==").put("last",false);boolean bad=false;try{new ConversationChunks().accept(p);}catch(IOException e){bad=true;}check(bad,"Missing first fragment rejected");
  p.put("index",0).put("last",true).put("sha256","invalid");bad=false;try{new ConversationChunks().accept(p);}catch(IOException e){bad=true;}check(bad,"Incorrect content hash rejected");
  ConversationChunks a=new ConversationChunks();p.put("last",false);a.accept(p);p.put("index",1).put("transfer","22222222222222222222222222222222");bad=false;try{a.accept(p);}catch(IOException e){bad=true;}check(bad,"Interleaved conversation identities rejected");
 }
}
