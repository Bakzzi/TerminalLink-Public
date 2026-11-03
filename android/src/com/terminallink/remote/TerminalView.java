package com.terminallink.remote;
import android.content.Context;
import android.graphics.*;
import android.view.*;

final class TerminalView extends View {
 interface Actions {void point(double x,double y,String action,int delta); void drag(double sx,double sy,double x,double y);}
 final Actions actions;
 final Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);
 Bitmap bitmap;
 float zoom=1f,dx=0,dy=0,startX,startY,lastX,lastY;
 boolean moved,scaling; boolean dragMode=false;
 String placeholder="PC 터미널을 선택하세요.";
 final ScaleGestureDetector scale;
 final GestureDetector gesture;
 TerminalView(Context c,Actions actions){
  super(c);this.actions=actions;setBackgroundColor(Color.rgb(5,10,18));
  scale=new ScaleGestureDetector(c,new ScaleGestureDetector.SimpleOnScaleGestureListener(){
   public boolean onScale(ScaleGestureDetector d){zoom=Math.max(1f,Math.min(6f,zoom*d.getScaleFactor()));scaling=true;invalidate();return true;}
  });
  gesture=new GestureDetector(c,new GestureDetector.SimpleOnGestureListener(){
   public boolean onDown(android.view.MotionEvent e){return true;}
   public boolean onDoubleTap(android.view.MotionEvent e){zoom=zoom>1?1:2;dx=dy=0;invalidate();return true;}
   public void onLongPress(android.view.MotionEvent e){if(!moved&&!scaling)point(e.getX(),e.getY(),"right",0);}
   public boolean onSingleTapConfirmed(android.view.MotionEvent e){if(!moved&&!scaling)point(e.getX(),e.getY(),"left",0);return true;}
  });
 }
 void image(Bitmap b){bitmap=b;placeholder="";invalidate();}
 void clear(String text){bitmap=null;placeholder=text;invalidate();}
 void reset(){zoom=1;dx=dy=0;invalidate();}
 float factor(){return bitmap==null?1:Math.min((float)getWidth()/bitmap.getWidth(),(float)getHeight()/bitmap.getHeight())*zoom;}
 float left(){return bitmap==null?0:(getWidth()-bitmap.getWidth()*factor())/2+dx;}
 float top(){return bitmap==null?0:(getHeight()-bitmap.getHeight()*factor())/2+dy;}
 @Override protected void onDraw(Canvas c){
  super.onDraw(c);
  if(bitmap!=null)c.drawBitmap(bitmap,null,new RectF(left(),top(),left()+bitmap.getWidth()*factor(),top()+bitmap.getHeight()*factor()),paint);
  else{paint.setColor(Color.rgb(147,165,187));paint.setTextSize(15*getResources().getDisplayMetrics().scaledDensity);paint.setTextAlign(Paint.Align.CENTER);c.drawText(placeholder,getWidth()/2f,getHeight()/2f,paint);}
 }
 void point(float x,float y,String action,int delta){
  if(bitmap==null)return;
  double nx=(x-left())/(bitmap.getWidth()*factor()),ny=(y-top())/(bitmap.getHeight()*factor());
  if(nx>=0&&nx<=1&&ny>=0&&ny<=1)actions.point(nx,ny,action,delta);
 }
 void wheel(int delta){actions.point(.5,.5,"wheel",delta);}
 @Override public boolean onTouchEvent(MotionEvent e){
  scale.onTouchEvent(e);gesture.onTouchEvent(e);
  if(e.getActionMasked()==MotionEvent.ACTION_DOWN){startX=lastX=e.getX();startY=lastY=e.getY();moved=false;scaling=false;}
  if(e.getActionMasked()==MotionEvent.ACTION_MOVE&&!scale.isInProgress()){
   if(Math.abs(e.getX()-startX)+Math.abs(e.getY()-startY)>12)moved=true;
   if(moved&&!dragMode){dx+=e.getX()-lastX;dy+=e.getY()-lastY;invalidate();}
   lastX=e.getX();lastY=e.getY();
  }
  if(e.getActionMasked()==MotionEvent.ACTION_UP&&moved&&dragMode&&!scaling&&bitmap!=null){double sx=(startX-left())/(bitmap.getWidth()*factor()),sy=(startY-top())/(bitmap.getHeight()*factor()),x=(e.getX()-left())/(bitmap.getWidth()*factor()),y=(e.getY()-top())/(bitmap.getHeight()*factor());if(sx>=0&&sx<=1&&sy>=0&&sy<=1&&x>=0&&x<=1&&y>=0&&y<=1)actions.drag(sx,sy,x,y);}
  return true;
 }
}