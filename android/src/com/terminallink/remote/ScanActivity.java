package com.terminallink.remote;
import android.app.Activity;
import android.content.Intent;
import android.hardware.Camera;
import android.os.Bundle;
import android.view.*;
import android.widget.*;
import com.google.zxing.*;
import com.google.zxing.common.HybridBinarizer;
import java.util.*;
@SuppressWarnings("deprecation")
public final class ScanActivity extends Activity implements SurfaceHolder.Callback,Camera.PreviewCallback {
 Camera camera;long last;boolean done;
 final MultiFormatReader reader=new MultiFormatReader();
 @Override public void onCreate(Bundle b){
  super.onCreate(b);getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
  LinearLayout root=new LinearLayout(this);root.setOrientation(1);root.setBackgroundColor(0xff0c121d);
  TextView label=new TextView(this);label.setText("PC의 TerminalLink 등록 QR을 비춰 주세요.");label.setTextColor(0xffffffff);label.setTextSize(18);label.setPadding(24,50,24,24);root.addView(label);
  SurfaceView preview=new SurfaceView(this);root.addView(preview,new LinearLayout.LayoutParams(-1,0,1));preview.getHolder().addCallback(this);
  Button cancel=new Button(this);cancel.setText("취소");cancel.setOnClickListener(v->finish());root.addView(cancel);setContentView(root);
  Map<DecodeHintType,Object> hints=new EnumMap<>(DecodeHintType.class);hints.put(DecodeHintType.POSSIBLE_FORMATS,Collections.singletonList(BarcodeFormat.QR_CODE));hints.put(DecodeHintType.TRY_HARDER,true);reader.setHints(hints);
 }
 public void surfaceCreated(SurfaceHolder holder){
  try{camera=Camera.open();Camera.Parameters p=camera.getParameters();
   for(Camera.Size s:p.getSupportedPreviewSizes())if(s.width==640&&s.height==480){p.setPreviewSize(s.width,s.height);break;}
   if(p.getSupportedFocusModes().contains(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE))p.setFocusMode(Camera.Parameters.FOCUS_MODE_CONTINUOUS_PICTURE);
   camera.setParameters(p);camera.setDisplayOrientation(90);camera.setPreviewDisplay(holder);camera.setPreviewCallback(this);camera.startPreview();
  }catch(Exception e){Toast.makeText(this,"카메라를 열 수 없습니다. 코드 입력을 사용하세요.",Toast.LENGTH_LONG).show();finish();}
 }
 public void surfaceChanged(SurfaceHolder h,int f,int w,int height){}
 public void surfaceDestroyed(SurfaceHolder h){release();}
 void release(){if(camera!=null){camera.setPreviewCallback(null);camera.stopPreview();camera.release();camera=null;}}
 public void onPreviewFrame(byte[] data,Camera c){
  if(done||System.currentTimeMillis()-last<350)return;last=System.currentTimeMillis();
  Camera.Size s=c.getParameters().getPreviewSize();
  try{
   PlanarYUVLuminanceSource source=new PlanarYUVLuminanceSource(data,s.width,s.height,0,0,s.width,s.height,false);
   Result result=reader.decodeWithState(new BinaryBitmap(new HybridBinarizer(source)));
   if(result.getText().startsWith("tr1:")){done=true;setResult(RESULT_OK,new Intent().putExtra("code",result.getText()));finish();}
  }catch(NotFoundException ignored){}finally{reader.reset();}
 }
 @Override protected void onPause(){release();super.onPause();}
}