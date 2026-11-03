from pathlib import Path
import sys,shutil
r=Path(sys.argv[1]);out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
for p in (r/'tests/release-fixtures/request-loading-jvm/android').rglob('*.java'):
 q=out/'android'/p.relative_to(r/'tests/release-fixtures/request-loading-jvm/android');q.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,q)
for name in ['ActionControls','RecordRequest','RequestPrompt','ConversationStatus','NotificationReplyAccess','RequestCompletions']:
 shutil.copyfile(r/f'android/src/com/terminallink/remote/{name}.java',out/f'{name}.java')
s=(r/'tests/release-fixtures/request-loading-jvm/com/terminallink/remote/MainActivity.java').read_text(encoding='utf-8')
s=s.replace('import java.util.*;','import java.util.*;import org.json.*;').replace('List<Map<String,Object>>','List<JSONObject>').replace('void send(Map<String,Object> m)','void send(JSONObject m)')
a=s.index('static Map<String,Object> obj(');b=s.index('void send(',a)
s=s[:a]+'static JSONObject obj(Object... a){JSONObject m=new JSONObject();try{for(int i=0;i<a.length;i+=2)m.put((String)a[i],a[i+1]);}catch(Exception e){throw new RuntimeException(e);}return m;}'+s[b:]
s=s.replace('class MainActivity{','class MainActivity{LinkService link=new LinkService();ActionControls actions;boolean ready=true;')
s=s.replace('boolean inputReady(){return true;}','boolean inputReady(){return ready;}')
actual=(r/'android/src/com/terminallink/remote/MainActivity.java').read_text(encoding='utf-8')
a=actual.index(' void updateActions(');b=actual.index(' void openNotifiedRequest(',a)
s=s.replace('sent.add(m);','link.requestCompletions.track(m);sent.add(m);')
s=s.replace('Map<Runnable,Long> queue=new LinkedHashMap<>();','Map<Runnable,Long> queue=new LinkedHashMap<>();long now;void advance(long ms){long end=now+ms;while(true){Runnable next=null;long due=Long.MAX_VALUE;for(Map.Entry<Runnable,Long> e:queue.entrySet())if(e.getValue()<due){next=e.getKey();due=e.getValue();}if(next==null||due>end)break;queue.remove(next);now=due;next.run();}now=end;}').replace('queue.put(r,ms);','queue.put(r,now+ms);')
s=s[:-2]+actual[a:b]+'}\n'
(out/'MainActivity.java').write_text(s,encoding='utf-8')
(out/'LinkService.java').write_text('package com.terminallink.remote;import org.json.*;class LinkService{RequestCompletions requestCompletions=new RequestCompletions("{}");void completeRequest(String id,String thread,String item){requestCompletions.complete(id,thread,item);acknowledgeRequest(id);}JSONObject readyMessage=MainActivity.obj("notificationReply",1,"nativeNotificationReply",1),lastFrame;int acknowledged;void acknowledgeRequest(String id){acknowledged++;}}',encoding='utf-8')
shutil.copyfile(r/'tests/release-fixtures/record-request-popup/PopupJvm.java',out/'PopupJvm.java')
