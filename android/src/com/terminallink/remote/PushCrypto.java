package com.terminallink.remote;
import android.content.*;
import android.security.keystore.*;
import android.util.Base64;
import java.security.*;
import javax.crypto.*;
import javax.crypto.spec.*;
import java.nio.charset.StandardCharsets;
import org.json.*;

final class PushCrypto {
 static final String ALIAS="terminallink-push-wrap-v1";
 static javax.crypto.SecretKey wrappingKey()throws Exception{
  KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
  if(!store.containsAlias(ALIAS)){
   KeyGenerator g=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");
   g.init(new KeyGenParameterSpec.Builder(ALIAS,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build());g.generateKey();
  }
  return (javax.crypto.SecretKey)store.getKey(ALIAS,null);
 }
 static byte[] key(Context context,String pc,boolean create)throws Exception{
  SharedPreferences prefs=context.getSharedPreferences("push-state",Context.MODE_PRIVATE);
  String sealed=prefs.getString("key:"+pc,"");
  if(!sealed.isEmpty()){
   byte[] b=Base64.decode(sealed,Base64.NO_WRAP);Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,wrappingKey(),new GCMParameterSpec(128,b,0,12));cipher.updateAAD(pc.getBytes(StandardCharsets.UTF_8));return cipher.doFinal(b,12,b.length-12);
  }
  if(!create)throw new GeneralSecurityException("No paired push key");
  byte[] key=new byte[32];new SecureRandom().nextBytes(key);
  Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,wrappingKey());cipher.updateAAD(pc.getBytes(StandardCharsets.UTF_8));
  byte[] encrypted=cipher.doFinal(key),all=new byte[12+encrypted.length];System.arraycopy(cipher.getIV(),0,all,0,12);System.arraycopy(encrypted,0,all,12,encrypted.length);
  if(!prefs.edit().putString("key:"+pc,Base64.encodeToString(all,Base64.NO_WRAP)).commit())throw new java.io.IOException("Could not save paired push key");
  return key;
 }
 static JSONObject decrypt(Context context,String pc,String envelope)throws Exception{
  if(envelope.length()>6000)throw new GeneralSecurityException("Push too large");
  byte[] data=Base64.decode(envelope,Base64.NO_WRAP),key=key(context,pc,false);
  try{
   if(data.length<28)throw new GeneralSecurityException("Invalid push");
   Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,new SecretKeySpec(key,"AES"),new GCMParameterSpec(128,data,0,12));cipher.updateAAD(("TerminalLink push v1:"+pc).getBytes(StandardCharsets.UTF_8));
   JSONObject value=new JSONObject(new String(cipher.doFinal(data,12,data.length-12),StandardCharsets.UTF_8));
   long now=System.currentTimeMillis()/1000;if(!pc.equals(value.optString("pc"))||value.optLong("expires")<now||value.optLong("expires")>now+86400)throw new GeneralSecurityException("Expired push");return value;
  }finally{java.util.Arrays.fill(key,(byte)0);}
 }
}
