package com.terminallink.remote;
import java.util.*;

// Only the exact list confirmed by the user is eligible for deletion.
final class FileBoxClear {
 private final ArrayDeque<String> queue=new ArrayDeque<>();
 private String request="";int total,done;
 boolean active(){return !queue.isEmpty();}
 String id(){return queue.peekFirst();}
 String request(){return request;}
 boolean start(List<String> selected,List<String> current){
  if(active()||selected==null||selected.isEmpty()||!selected.equals(current))return false;
  HashSet<String> unique=new HashSet<>();
  for(String id:selected)if(id==null||!id.matches("[a-f0-9]{32}")||!unique.add(id))return false;
  queue.addAll(selected);total=selected.size();done=0;return true;
 }
 void requested(String value){request=value;}
 boolean accept(String reply,String id){
  if(request.isEmpty()||!request.equals(reply)||!Objects.equals(queue.peekFirst(),id))return false;
  request="";queue.removeFirst();done++;return true;
 }
 void stop(){queue.clear();request="";}
}
