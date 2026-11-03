package com.terminallink.remote;
import org.json.*;import java.util.*;

// Real transport/send/notification code with OS-only stubs; no phone or live input.
public final class ScenarioJvm {
 static int passed,failed;
 static void check(boolean value,String name){if(!value)throw new AssertionError(name);passed++;}
 interface Case {void run()throws Exception;}
 static void scenario(String name,Case test){try{test.run();System.out.println("PASS "+name);}catch(Throwable e){failed++;System.out.println("FAIL "+name+": "+e);}}
 static JSONObject value(String label){return MainActivity.obj("type","fixture","label",label);}
 static JSONObject snapshot(long seq){return MainActivity.obj("type","eventSnapshot","epoch","epoch","sequence",seq,"states",new JSONArray());}
 static JSONObject batch(long... numbers){JSONArray items=new JSONArray();for(long n:numbers)items.put(MainActivity.obj("sequence",n,"value",value("event-"+n)));return MainActivity.obj("type","eventBatch","epoch","epoch","events",items);}
 public static void main(String[] args)throws Exception{
  scenario("missing event requests replay without skipping the gap",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(5));e.receive(batch(7));
   check(e.sequence==5&&s.sent.size()==1&&s.sent.get(0).optLong("after")==5,"gap advanced the durable cursor");
   e.receive(batch(6,7));check(e.sequence==7,"complete replay failed");
  });
  scenario("old snapshot cannot rewind the current stream",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(5));e.receive(batch(6));e.receive(snapshot(4));
   check(e.sequence==6&&s.state.getLong("event-sequence",0)==6,"old snapshot rewound cursor");
  });
  scenario("malformed snapshot cannot acknowledge absent state",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(5));
   e.receive(MainActivity.obj("type","eventSnapshot","epoch","epoch","sequence",20));check(e.sequence==5,"missing states skipped data");
  });
  scenario("partial batch stops at first missing entry",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(5));e.receive(batch(6,8,9));
   check(e.sequence==6&&s.sent.get(s.sent.size()-1).optLong("after")==6,"partial batch skipped missing entry");
  });
  scenario("repeated gaps request one resync and a complete snapshot recovers",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(3));
   for(int n=0;n<100;n++)e.receive(batch(9));check(e.sequence==3&&s.sent.size()==1,"gap caused a sync storm");
   e.receive(snapshot(9));e.receive(batch(10));check(e.sequence==10,"full resync did not recover");
  });
  scenario("missing event payload never consumes its sequence",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(3));
   e.receive(MainActivity.obj("type","eventBatch","epoch","epoch","events",new JSONArray().put(MainActivity.obj("sequence",4))));
   check(e.sequence==3&&s.sent.size()==1,"missing value acknowledged");e.receive(batch(4));check(e.sequence==4,"corrected value not recoverable");
  });
  scenario("server restart requires the new snapshot before new events",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(50));e.receive(batch(1).put("epoch","restarted"));
   check(e.sequence==50&&e.epoch.equals("epoch"),"unknown epoch applied early");
   e.receive(snapshot(0).put("epoch","restarted"));e.receive(batch(1).put("epoch","restarted"));
   check(e.sequence==1&&e.epoch.equals("restarted"),"restart could not reset sequence");
  });
  scenario("duplicate batches and reconnect preserve sequence",()->{
   LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(0));
   for(int n=1;n<=100;n++){e.receive(batch(n));e.receive(batch(n));check(e.sequence==n,"duplicate changed cursor");}
   EventStream restored=new EventStream(s);check(restored.sequence==100,"reconnect lost durable cursor");
   restored.receive(batch(101));check(restored.sequence==101,"reconnect did not continue");
  });
  scenario("reordered batches recover in 64 seeded traces",()->{
   for(int seed=0;seed<64;seed++){LinkService s=new LinkService();EventStream e=new EventStream(s);e.receive(snapshot(0));
    ArrayList<Integer> order=new ArrayList<>();for(int n=1;n<=12;n++)order.add(n);Collections.shuffle(order,new Random(seed));
    long model=0;for(int n:order){if(n==model+1)model=n;e.receive(batch(n));check(e.sequence==model,"seed "+seed+" skipped "+(model+1));}
    for(long n=model+1;n<=12;n++)e.receive(batch(n));check(e.sequence==12,"replay incomplete");
   }
  });
  scenario("late success from another tab keeps identical new draft",()->{
   MainActivity a=new MainActivity();a.selectedId="tab-b";a.terminal.thread="thread-b";a.compose.setText("same words");
   a.sentMessage("tab-a","thread-a","same words",true);check(a.compose.getText().equals("same words"),"another tab draft erased");
   check(a.terminal.latestCalls==0,"another tab scrolled");
  });
  scenario("late success from an earlier conversation keeps new draft",()->{
   MainActivity a=new MainActivity();a.selectedId="tab-a";a.terminal.thread="new-thread";a.compose.setText("same words");
   a.sentMessage("tab-a","old-thread","same words",false);check(a.compose.getText().equals("same words"),"new conversation draft erased");
  });
  scenario("current success clears only the unchanged draft",()->{
   MainActivity a=new MainActivity();a.selectedId="tab-a";a.terminal.thread="thread";a.compose.setText("sent");
   a.sentMessage("tab-a","thread","sent",true);check(a.compose.getText().isEmpty()&&a.terminal.latestCalls==1,"current receipt not projected");
   a.compose.setText("edited after timeout");a.sentMessage("tab-a","thread","sent",false);check(a.compose.getText().equals("edited after timeout"),"edited draft erased");
   a.compose.setText("sent");a.sentMessage("tab-a","thread","sent",false);check(a.compose.getText().equals("sent"),"identical new draft erased by late success");
   a.sentMessage("tab-a","start:native","sent",true);check(a.compose.getText().isEmpty(),"first original arriving before ACK left the sent draft behind");
  });
  scenario("timeout retry and late acknowledgements never duplicate a send",()->{
   LinkService s=new LinkService();s.viewer=new MainActivity();s.readyMessage.put("messageSubmit",1);MessageSend m=s.messageSend;
   m.start("a","t","first",0);String old=m.request;Runnable oldTimeout=m.timeout;oldTimeout.run();
   m.start("b","u","second",0);String current=m.request;oldTimeout.run();m.result(MainActivity.obj("request",old,"ok",true));
   check(m.busy&&m.request.equals(current)&&s.sent.size()==2,"old callback changed new send");
   m.result(MainActivity.obj("request",current,"ok",true));m.result(MainActivity.obj("request",current,"ok",true));
   check(!m.busy&&s.viewer.confirmed==1&&s.sent.size()==2,"duplicate ack or retry wrote again");
  });
  System.out.println("Scenarios: "+passed+" assertions, "+failed+" failing scenarios");if(failed>0)throw new AssertionError("scenario failures");
 }
}
