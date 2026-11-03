from pathlib import Path
import sys,subprocess,shutil
r=Path(sys.argv[1]);out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
subprocess.run([sys.executable,str(r/'tests/release-fixtures/tab-notice-images/extract.py'),str(r),str(out)],check=True)
# Replace only operating-system surfaces. All reply methods are the production source.
for name in ['NotificationReplies','NotificationReplyAccess','EventStream']:
 shutil.copyfile(r/f'android/src/com/terminallink/remote/{name}.java',out/f'{name}.java')
(out/'com/terminallink/remote/NotificationReplies.java').unlink()
f=out/'LinkService.java';s=f.read_text(encoding='utf-8');s=s.replace('NotificationReplies replies=new NotificationReplies();','JSONObject readyMessage=MainActivity.obj("notificationReply",1,"nativeNotificationReply",1);boolean ready=true;Object client=new Object();android.os.Handler ui=new android.os.Handler();java.util.List<JSONObject> sent=new java.util.ArrayList<>();void send(JSONObject v){sent.add(v);}NotificationReplies replies=new NotificationReplies(this);')
s=s.replace('int icon(){','void receive(JSONObject packet){} void updateSummary(int id,int summary,String channel,String group,String title,String nonce){NotificationSummary.update(this,id,summary,channel,group,title,nonce,null,null);}PendingIntent open(String id,String key,String pc){return new PendingIntent();}int icon(){')
f.write_text(s,encoding='utf8')
for p in (r/'tests/release-fixtures/notification-reply/os').rglob('*.java'):
 q=out/p.relative_to(r/'tests/release-fixtures/notification-reply/os');q.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,q)

shutil.copyfile(r/'tests/release-fixtures/notification-reply/NotificationReplyJvm.java',out/'NotificationReplyJvm.java')
shutil.copyfile(r/'tests/release-fixtures/notification-reply/UnreadAnswersJvm.java',out/'UnreadAnswersJvm.java')
for name in ['MessageSend','SentEchoes']:
 shutil.copyfile(r/f'android/src/com/terminallink/remote/{name}.java',out/f'{name}.java')
f=out/'LinkService.java';s=f.read_text(encoding='utf8').replace('boolean ready=true;', 'boolean ready=true;final SentEchoes sentMessages=new SentEchoes();final MessageSend messageSend=new MessageSend(this);');f.write_text(s,encoding='utf8')
f=out/'com/terminallink/remote/MainActivity.java';s=f.read_text(encoding='utf8')
actual=(r/'android/src/com/terminallink/remote/MainActivity.java').read_text(encoding='utf8')
callback=actual[actual.index(' void sentMessage('):actual.index(' void sendEnter(')]
callback=callback.replace('void sentMessage(String session,String thread,String text,boolean pending){','void sentMessage(String session,String thread,String text,boolean pending){confirmed++;')
s=s[:s.rfind('}')]+'''int refreshes,confirmed;String selectedId="",status="";
static class Editor {String value="";String getText(){return value;}void setText(String s){value=s;}}
static class Terminal {String thread="";int refreshes,latestCalls;void refreshSent(){refreshes++;}void latest(){latestCalls++;}}
Editor compose=new Editor();Terminal terminal=new Terminal();void setStatus(String text){status=text;}void refreshSendState(){refreshes++;}
'''+callback+'\n}'
f.write_text(s,encoding='utf8')
shutil.copyfile(r/'tests/release-fixtures/notification-reply/MessageSendJvm.java',out/'MessageSendJvm.java')
shutil.copyfile(r/'tests/release-fixtures/notification-reply/ScenarioJvm.java',out/'ScenarioJvm.java')
# Exercise the actual acknowledgement/cache/notification cleanup together.
actual=(r/'android/src/com/terminallink/remote/LinkService.java').read_text(encoding='utf8')
f=out/'LinkService.java';s=f.read_text(encoding='utf8')
s=s.replace('void clearRequest(String id){requests.remove(id);nm.cancel(RequestTracker.digest(id),REQUEST_ID);}', '')
s=s.replace('void send(JSONObject v){sent.add(v);}', 'void send(JSONObject v){requestCompletions.track(v);sent.add(v);}')
body=actual[actual.index(' void requestResult('):actual.index(' void watch(')]
body+=actual[actual.index(' void observeRequest('):actual.index(' void postRequest(')]
s=s[:s.rfind('}')]+'''JSONObject lastFrame;Map<String,Runnable> requestClear=new HashMap<>();
static class Observer{int received;void json(JSONObject j){received++;}}Observer observer;
void updateSummary(int id,int summary,String channel,String group,String title,String nonce,String tag,Notification n){NotificationSummary.update(this,id,summary,channel,group,title,nonce,tag,n);}
'''+body+'\n}'
f.write_text(s,encoding='utf8')
