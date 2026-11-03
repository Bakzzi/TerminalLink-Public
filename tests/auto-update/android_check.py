"""Compile the actual Android update coordinator against small deterministic stubs."""
from pathlib import Path
import shutil, subprocess, sys
root = Path(__file__).resolve().parents[2]
out = root / 'build/auto-update/android'
out.mkdir(parents=True, exist_ok=True)
def write(name, text):
    p = out / name
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(text, encoding='utf-8')
shutil.copyfile(root / 'android/src/com/terminallink/remote/AutoUpdates.java', out / 'AutoUpdates.java')
write('android/app/Placeholder.java', 'package android.app; public class Placeholder {}')
write('android/content/Placeholder.java', 'package android.content; public class Placeholder {}')
write('android/widget/Toast.java', 'package android.widget; public class Toast {public static final int LENGTH_SHORT=0; public static Toast makeText(Object c,String s,int l){return new Toast();}public void show(){}}')
write('CoordinatorCheck.java', r'''
package com.terminallink.remote;
import java.util.*;import java.util.concurrent.*;import java.io.*;import org.json.*;
class Ui {ConcurrentLinkedQueue<Runnable> tasks=new ConcurrentLinkedQueue<>();void post(Runnable r){tasks.add(r);}void postDelayed(Runnable r,long n){}void removeCallbacks(Runnable r){}void drain(){Runnable r;while((r=tasks.poll())!=null)r.run();}}
class Prefs {Map<String,Object> data=new HashMap<>();String getString(String k,String d){return (String)data.getOrDefault(k,d);}long getLong(String k,long d){return ((Number)data.getOrDefault(k,d)).longValue();}Prefs edit(){return this;}Prefs putString(String k,String v){data.put(k,v);return this;}Prefs putLong(String k,long v){data.put(k,v);return this;}Prefs remove(String k){data.remove(k);return this;}void apply(){}}
class Edit {String text="";String getText(){return text;}}
class Attachments {boolean loading,busy,photos;boolean hasPhotos(){return photos;}}
class Actions {boolean pending,expanded,submitting;Edit answer=new Edit();}
class AppUpdates {void render(){}}
class MainActivity {static boolean updateForeground;boolean resumed,awaitingActivityResult;Edit compose=new Edit();Attachments attachments=new Attachments();Actions actions=new Actions();String creatingRequest="",closingRequest="";AppUpdates updates=new AppUpdates();static JSONObject obj(Object... pairs){JSONObject j=new JSONObject();for(int n=0;n<pairs.length;n+=2)j.put((String)pairs[n],pairs[n+1]);return j;}}
class LinkService {Prefs prefs=new Prefs();Ui ui=new Ui();boolean ready=true,busy;MainActivity viewer;AutoUpdates updates;List<JSONObject> sent=new ArrayList<>();void send(JSONObject j){sent.add(j);}boolean transferring(){return busy||updates!=null&&updates.busy();}void schedulePark(){}}
class MobileUpdateInstaller {static Set<String> attempts=new HashSet<>();static void start(Object c,UpdateApk.Offer o,File f){attempts.add(o.sha256);}}
class UpdateApk {
 static long installedVersion(Object c){return 1;}static File folder(Object c){return new File(".");}static void verify(Object c,File f,Offer o){}
 static class Offer {long versionCode,size;String sha256,versionName;JSONObject value;Offer(JSONObject j){value=j;versionCode=j.getLong("versionCode");size=j.getLong("size");sha256=j.getString("sha256");versionName=j.getString("versionName");}JSONObject json(){return value;}}
 static class Download {Offer offer;long received;boolean closed;Download(Object c,Offer o){offer=o;}void append(long offset,String data)throws IOException{if(offset!=received)throw new IOException("offset");received+=Base64.getDecoder().decode(data).length;}File finish()throws IOException{if(received!=offer.size)throw new IOException("incomplete");return new File("verified.apk");}void close(){closed=true;}}
}
public class CoordinatorCheck {
 static int checks;static void check(boolean ok,String why){if(!ok)throw new AssertionError(why);checks++;}
 static JSONObject offer(String hash){return MainActivity.obj("versionCode",2,"versionName","0.0.2","size",3,"sha256",hash);}
 static JSONObject msg(String type,String key){return MainActivity.obj("type",type,"request",key);}
 static void drain(LinkService s)throws Exception{for(int n=0;n<50;n++){s.ui.drain();Thread.sleep(5);}}
 public static void main(String[] args)throws Exception{
  MainActivity a=new MainActivity();a.resumed=true;check(!AutoUpdates.safeToReplace(a),"foreground must defer");a.resumed=false;a.awaitingActivityResult=true;check(!AutoUpdates.safeToReplace(a),"file picker must defer");a.awaitingActivityResult=false;
  a.compose.text="draft";check(!AutoUpdates.safeToReplace(a),"draft must defer");a.compose.text="";
  a.attachments.photos=true;check(!AutoUpdates.safeToReplace(a),"attachment must defer");a.attachments.photos=false;
  a.actions.pending=true;check(!AutoUpdates.safeToReplace(a),"request must defer");a.actions.pending=false;
  a.actions.answer.text="answer draft";check(!AutoUpdates.safeToReplace(a),"request draft must defer");a.actions.answer.text="";
  a.creatingRequest="creating";check(!AutoUpdates.safeToReplace(a),"tab operation must defer");a.creatingRequest="";check(AutoUpdates.safeToReplace(a),"idle is safe");
  LinkService s=new LinkService();s.viewer=a;s.updates=new AutoUpdates(s);AutoUpdates u=s.updates;drain(s);u.connected(true);check(s.sent.get(0).optString("op").equals("updateCheck"),"connection checks release");
  MainActivity.updateForeground=true;u.message(MainActivity.obj("type","updateInfo","release",offer("A")));check(u.request==null,"foreground download deferred");
  MainActivity.updateForeground=false;s.busy=true;u.tick();check(u.request==null,"sending download deferred");s.busy=false;u.tick();String key=u.request;check(key!=null,"background starts once");int sent=s.sent.size();u.get(false);check(s.sent.size()==sent,"duplicate download blocked");
  u.message(msg("updateError","wrong"));check(key.equals(u.request),"foreign reply ignored");
  u.message(msg("updateStart",key).put("release",offer("A")));u.message(msg("updateChunk",key).put("offset",0).put("data","YWJj"));
  s.viewer=null;check(u.busy(),"activity detach keeps download");s.busy=true;u.message(msg("updateEnd",key));drain(s);check(u.readyFile!=null&&!u.busy(),"complete download persisted");check(MobileUpdateInstaller.attempts.isEmpty(),"notification send prevents install");
  s.busy=false;u.tick();check(MobileUpdateInstaller.attempts.contains("A"),"idle background installs");u.close();
  LinkService f=new LinkService();f.updates=new AutoUpdates(f);AutoUpdates bad=f.updates;drain(f);bad.connected(true);bad.message(MainActivity.obj("type","updateInfo","release",offer("B")));String k=bad.request;bad.message(msg("updateStart",k).put("release",offer("B")));bad.message(msg("updateEnd",k));drain(f);check(bad.readyFile==null&&!bad.busy(),"partial download cannot install");bad.get(false);check(bad.request==null,"automatic failure backoff");bad.get(true);check(bad.request!=null,"manual retry permitted");bad.disconnected();check(!bad.busy(),"disconnect clears transfer");bad.close();
  System.out.println("PASS "+checks+" actual Android coordinator checks");
 }
}
''')
java = Path(r'C:/Program Files/Android/Android Studio/jbr/bin')
jar = root / 'build/tests/json-20250517.jar'
assert jar.is_file(), 'Use the existing verified JSON-java fixture dependency'
import hashlib
assert hashlib.sha256(jar.read_bytes()).hexdigest() == '3ea61b2a06e31edf1c91134fe9106b0ebb16628be169f3db75bc7a2b06b45796'
classes=out/'classes';classes.mkdir(exist_ok=True)
sources=sorted(out.rglob('*.java'))
args=out/'sources.txt';args.write_text('\n'.join('"'+str(p).replace('\\','/')+'"' for p in sources),encoding='utf-8')
subprocess.run([str(java/'javac.exe'),'-J-Xmx128m','-encoding','UTF-8','-cp',str(jar),'-d',str(classes),'@'+str(args)],check=True)
subprocess.run([str(java/'java.exe'),'-Xmx128m','-cp',str(classes)+';'+str(jar),'com.terminallink.remote.CoordinatorCheck'],check=True,cwd=out)
