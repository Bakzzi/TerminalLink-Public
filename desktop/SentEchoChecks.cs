namespace TerminalLink;
internal static class SentEchoChecks {
 internal static void Run(Action<bool,string> check){
  var target=new ConsoleTarget(new(0,0,"fixture","WindowsTerminal"),1,1,"fixture");var tab=new DesktopTab(target);var frame=new ConsoleFrame("fixture","",80,25,0,0,Array.Empty<int>());
  var original=new Conversation("echo",new[]{new ConversationMessage("assistant","final","Previous",1)},false);tab.Accept(frame,original);
  tab.ConfirmSent("echo",1,"Same text",Array.Empty<string>());check(tab.Messages.Count==2&&original.messages.Length==1,"confirmed echo appears without changing original history");
  var unconfirmed=tab.Messages[^1];
  check(unconfirmed.PendingReceipt&&unconfirmed.Speaker=="나 · 접수 확인 중","native write acknowledgement is visible but not presented as Codex acceptance");
  unconfirmed.RefreshDelivery(Environment.TickCount64+8001);
  check(unconfirmed.Speaker=="나 · 접수 미확인"&&unconfirmed.Text=="Same text","missing acceptance becomes explicit and preserves the full recoverable text");
  tab.Accept(frame,new("unavailable",[],false,source:"unavailable",readState:"failed"));
  check(ReferenceEquals(tab.Messages[^1],unconfirmed)&&tab.Messages[^1].PendingReceipt,"temporary read failure cannot clear the pending message or promote it to accepted");
  tab.Accept(frame,original);
  tab.ConfirmSent("echo",1,"Same text",Array.Empty<string>());
  var first=original with{messages=original.messages.Append(new("user","","Same text",2)).ToArray()};tab.Accept(frame,first);
  check(tab.Messages.Count(m=>m.Text=="Same text")==2,"one original user message consumes only one identical confirmed send");
  tab.Accept(frame,first with{});check(tab.Messages.Count(m=>m.Text=="Same text")==2,"repeated snapshots cannot consume the second identical send");
  var second=first with{messages=first.messages.Append(new("user","","Same text",3)).ToArray()};tab.Accept(frame,second);
  check(tab.Messages.Count(m=>m.Text=="Same text")==2&&tab.Messages.All(m=>m.Sequence>0),"original records replace both echoes without duplicates");
  check(tab.Messages.All(m=>!m.PendingReceipt)&&tab.Messages[^1].Speaker=="나","only matching original messages remove the unconfirmed submission status");
  tab.ConfirmSent("echo",1,"Same text",Array.Empty<string>());check(tab.Messages.Count==4,"later identical send cannot match an already consumed record");
  tab.Accept(frame,new("other",Array.Empty<ConversationMessage>(),false));check(tab.Messages.Count==0,"confirmed identity change cannot display an old conversation echo");
  var beforeAck=new DesktopTab(target);beforeAck.Accept(frame,first);beforeAck.ConfirmSent("echo",1,"Same text",Array.Empty<string>());check(beforeAck.Messages.Count==2,"record arriving before send acknowledgement is not duplicated");
  check(SentEchoes.Matches("[Image #1] Caption",new("t",1,"Caption",new[]{"photo.png"},-1)),"image caption can reconcile its confirmed echo");
 }
}
