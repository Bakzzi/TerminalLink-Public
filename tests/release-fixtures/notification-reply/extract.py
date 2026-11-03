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
