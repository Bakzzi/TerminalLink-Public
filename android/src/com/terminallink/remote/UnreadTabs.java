package com.terminallink.remote;
import android.content.SharedPreferences;
import android.graphics.Typeface;
import android.text.SpannableString;
import android.text.Spanned;
import android.text.TextUtils;
import android.text.style.ForegroundColorSpan;
import android.text.style.StyleSpan;
import android.view.*;
import android.widget.*;
import java.util.Arrays;

// Update only the selector, never the selected session, input draft or transcript.
final class UnreadTabs {
 static final int BLUE=0xff5ca8ff;
 final MainActivity activity;final SharedPreferences state;final TextView badge;
 final SharedPreferences.OnSharedPreferenceChangeListener listener=(p,key)->schedule();
 final Runnable update=()->{scheduled=false;refresh();};boolean scheduled,closed;boolean[] unread=new boolean[0];
 ArrayAdapter<String> adapter;
 UnreadTabs(MainActivity a,LinearLayout row){
  activity=a;state=UnreadAnswers.store(a);state.registerOnSharedPreferenceChangeListener(listener);
  badge=a.label("",12,BLUE);badge.setGravity(Gravity.CENTER);badge.setPadding(a.dp(4),0,a.dp(4),0);badge.setMinWidth(a.dp(36));badge.setTag("unread-tabs");badge.setVisibility(View.GONE);
  badge.setOnClickListener(v->a.windowList.performClick());row.addView(badge,new LinearLayout.LayoutParams(-2,a.dp(42)));
 }
 ArrayAdapter<String> adapter(String[] labels){
  adapter=new ArrayAdapter<String>(activity,android.R.layout.simple_spinner_dropdown_item,labels){
   @Override public View getView(int position,View convert,ViewGroup parent){return decorate(super.getView(position,convert,parent),position);}
   @Override public View getDropDownView(int position,View convert,ViewGroup parent){return decorate(super.getDropDownView(position,convert,parent),position);}
   View decorate(View view,int position){
    boolean pending=position<unread.length&&unread[position];String name=getItem(position);if(name==null)name="";
    SpannableString text=new SpannableString((pending?"●  ":"")+name);
    if(pending){text.setSpan(new ForegroundColorSpan(BLUE),0,1,Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);text.setSpan(new StyleSpan(Typeface.BOLD),3,text.length(),Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);}
    TextView label=(TextView)view;label.setText(text);label.setSingleLine(true);label.setEllipsize(TextUtils.TruncateAt.END);label.setContentDescription(name+(pending?", 읽지 않은 답변":""));return view;
   }
  };refresh();return adapter;
 }
 void schedule(){if(closed||scheduled)return;scheduled=true;activity.ui.post(update);}
 void refresh(){
  if(closed)return;String pc=activity.pin;boolean[] next=new boolean[activity.windowIds.length];int count=0;
  for(int i=0;i<next.length;i++){next[i]=UnreadAnswers.has(activity,pc,activity.windowIds[i]);if(next[i])count++;}
  if(!Arrays.equals(unread,next)){unread=next;if(adapter!=null)adapter.notifyDataSetChanged();}
  badge.setVisibility(count==0?View.GONE:View.VISIBLE);badge.setText("● "+count);String description="안 읽은 답변이 있는 탭 "+count+"개";badge.setContentDescription(description);badge.setTooltipText(description);
 }
 void close(){closed=true;state.unregisterOnSharedPreferenceChangeListener(listener);activity.ui.removeCallbacks(update);}
}
