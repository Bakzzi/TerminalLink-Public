from pathlib import Path
import sys, shutil

root, out = map(Path, sys.argv[1:])
out.mkdir(parents=True, exist_ok=True)
actual = (root/'android/src/com/terminallink/remote/ImageAttachments.java').read_text(encoding='utf8')
methods = actual[actual.index(' void message(JSONObject'):actual.index(' void disconnected()')]
header = '''package com.terminallink.remote;
import org.json.*;import java.io.*;import java.util.*;import java.util.concurrent.*;import android.widget.TextView;
final class ImageAttachments {
 final MainActivity a;final Tray tray=new Tray();final ArrayList<Photo> photos=new ArrayList<>();
 final ExecutorService worker=Executors.newSingleThreadExecutor();final ConcurrentHashMap<String,CompletableFuture<JSONObject>> pending=new ConcurrentHashMap<>();
 volatile boolean closed,busy;boolean loading,supported=true,filesSupported=true,remoteSupported=true,localSupported=true,remoteFilesSupported=true,localFilesSupported=true;
 String boundSession="",boundPin="",progress="";android.app.AlertDialog errorDialog;
 ImageAttachments(MainActivity activity){a=activity;}
 static class Tray {TextView findViewWithTag(String tag){return new TextView();}}
 static class Photo {final File file;final String name;final boolean image;Photo(File f,boolean i){file=f;name=f.getName();image=i;}boolean image(){return image;}void close(){file.delete();}}
 void render(){}
'''
(out/'ImageAttachments.java').write_text(header+methods+'}\n', encoding='utf8')
shutil.copyfile(root/'android/src/com/terminallink/remote/AttachmentAccess.java',out/'AttachmentAccess.java')
shutil.copyfile(root/'tests/release-fixtures/attachment-send/AttachmentSendJvm.java',out/'AttachmentSendJvm.java')
for name,body in {
 'android/widget/TextView.java':'package android.widget;public class TextView{public void setText(String s){}}',
 'android/util/Base64.java':'package android.util;public class Base64{public static final int NO_WRAP=2;public static String encodeToString(byte[] b,int o,int n,int flags){return java.util.Base64.getEncoder().encodeToString(java.util.Arrays.copyOfRange(b,o,o+n));}}',
 'android/app/AlertDialog.java':'package android.app;public class AlertDialog{public static class Builder{public Builder(Object c){}public Builder setTitle(String s){return this;}public Builder setMessage(String s){return this;}public Builder setPositiveButton(String s,Object x){return this;}public AlertDialog show(){return new AlertDialog();}}}',
}.items():
 p=out/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(body,encoding='utf8')
# Check the nested deadlines as a contract, not independent magic numbers.
import re
host=(root/'host/LocalImageTarget.cs').read_text(encoding='utf8')
pc=int(re.search(r'DeliverySeconds=(\d+)',host)[1])
mobile=int(re.search(r'remote\?110:(\d+)',actual)[1])
assert mobile>pc>=60, 'Mobile receipt timeout must contain native attachment preparation/delivery'
