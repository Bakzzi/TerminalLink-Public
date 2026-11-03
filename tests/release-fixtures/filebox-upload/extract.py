from pathlib import Path
import shutil
import sys

root, out = map(Path, sys.argv[1:])
out.mkdir(parents=True, exist_ok=True)
shutil.copyfile(root / 'android/src/com/terminallink/remote/FileBoxUpload.java', out / 'FileBoxUpload.java')
shutil.copyfile(root / 'android/src/com/terminallink/remote/FileBoxClear.java', out / 'FileBoxClear.java')
shutil.copyfile(root / 'tests/release-fixtures/filebox-upload/UploadJvm.java', out / 'UploadJvm.java')
stubs = {
 'android/app/Activity.java': 'package android.app;public class Activity{public static final int RESULT_OK=-1;}',
 'android/content/ActivityNotFoundException.java': 'package android.content;public class ActivityNotFoundException extends RuntimeException{}',
 'android/content/ClipData.java': 'package android.content;import android.net.Uri;public class ClipData{public final Uri[] uris;public ClipData(Uri...u){uris=u;}public int getItemCount(){return uris.length;}public Item getItemAt(int i){return new Item(uris[i]);}public static class Item{final Uri uri;Item(Uri u){uri=u;}public Uri getUri(){return uri;}}}',
 'android/content/Intent.java': 'package android.content;import android.net.Uri;public class Intent{public static final String ACTION_OPEN_DOCUMENT="open",CATEGORY_OPENABLE="openable",EXTRA_ALLOW_MULTIPLE="multiple";public ClipData clips;public Uri uri;public Intent(String s){}public Intent setType(String s){return this;}public Intent addCategory(String s){return this;}public Intent putExtra(String k,boolean v){return this;}public ClipData getClipData(){return clips;}public Uri getData(){return uri;}}',
 'android/database/Cursor.java': 'package android.database;public interface Cursor extends AutoCloseable{boolean moveToFirst();String getString(int i);void close();}',
 'android/net/Uri.java': 'package android.net;public class Uri{public final String value;public Uri(String v){value=v;}public String getScheme(){return "content";}public boolean equals(Object x){return x instanceof Uri&&value.equals(((Uri)x).value);}public int hashCode(){return value.hashCode();}}',
 'android/os/SystemClock.java': 'package android.os;public class SystemClock{public static long elapsedRealtime(){return System.nanoTime()/1000000;}}',
 'android/provider/OpenableColumns.java': 'package android.provider;public class OpenableColumns{public static final String DISPLAY_NAME="name";}',
 'android/util/Base64.java': 'package android.util;public class Base64{public static final int NO_WRAP=2;public static String encodeToString(byte[] b,int o,int n,int f){return java.util.Base64.getEncoder().encodeToString(java.util.Arrays.copyOfRange(b,o,o+n));}}',
}
for name, body in stubs.items():
 p=out/name
 p.parent.mkdir(parents=True, exist_ok=True)
 p.write_text(body, encoding='utf8')
