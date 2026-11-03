package com.terminallink.remote;
import android.app.*;
import android.content.*;
import android.os.*;
final class NotificationSummary {
 static void update(Context context,int itemId,int summaryId,String channel,String group,String title,String dismissedNonce,String incomingTag,Notification incoming){
  // notify/cancel are asynchronous. Include the exact notification just posted instead of
  // copying an older action from getActiveNotifications() into the group summary.
  NotificationManager nm=context.getSystemService(NotificationManager.class);
  String pc=AnswerReceipts.pc(context);
  if(itemId==LinkService.ANSWER_ID&&incoming!=null&&!incoming.extras.getBoolean("tl-stale-reply")&&AnswerReceipts.dismissed(context,pc,incoming,incomingTag==null?"":incomingTag))incoming=null;
  Notification child=incoming;int count=incoming==null?0:1;
  for(android.service.notification.StatusBarNotification item:nm.getActiveNotifications())if(item.getId()==itemId){
   Notification n=item.getNotification();
   if(itemId==LinkService.ANSWER_ID&&!n.extras.getBoolean("tl-stale-reply")&&AnswerReceipts.dismissed(context,pc,n,item.getTag()==null?"":item.getTag()))continue;
   if(incomingTag!=null&&java.util.Objects.equals(incomingTag,item.getTag()))continue;
   if(dismissedNonce!=null&&dismissedNonce.equals(n.extras.getString("tl-nonce")))continue;
   count++;if(child==null||child.when<n.when)child=n;
  }
  if(child==null){nm.cancel(summaryId);return;}
  Bundle extras=new Bundle();extras.putString("tl-summary-nonce",child.extras.getString("tl-nonce",""));
  extras.putString("tl-state",child.extras.getString("tl-state","new"));
  extras.putBoolean("android.remoteInputSpinner","sending".equals(extras.getString("tl-state")));
  extras.putBoolean("android.hideSmartReplies","sending".equals(extras.getString("tl-state")));
  Notification.Builder summary=new Notification.Builder(context,channel).setSmallIcon(context.getResources().getIdentifier("ic_notification","drawable",context.getPackageName())).setContentTitle(NotificationTitles.summary(child.extras.getCharSequence(Notification.EXTRA_TITLE),title)).setContentText(child.extras.getCharSequence(Notification.EXTRA_TEXT))
   .setWhen(child.when).setSubText(count>1?count+"개":null).setContentIntent(child.contentIntent).setGroup(group).setGroupSummary(true).setGroupAlertBehavior(Notification.GROUP_ALERT_CHILDREN)
   .setOnlyAlertOnce(true).setAutoCancel(true).setVisibility(Notification.VISIBILITY_PRIVATE).setPublicVersion(child.publicVersion).setExtras(extras)
   .setRemoteInputHistory(child.extras.getCharSequenceArray(Notification.EXTRA_REMOTE_INPUT_HISTORY));
  if(child.actions!=null)for(Notification.Action action:child.actions)summary.addAction(action);
  nm.notify(summaryId,summary.build());

 }
}
