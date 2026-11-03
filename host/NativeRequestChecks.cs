using System.Text.Json;
namespace TerminalLink;
internal static class NativeRequestChecks {
 internal static void Run(Action<bool,string> check){
  var feed=new CodexFeed();
  feed.AddRecord(JsonSerializer.SerializeToUtf8Bytes(new{type="session_meta",payload=new{id="fixture-many"}}));
  feed.AddRecord(JsonSerializer.SerializeToUtf8Bytes(new{type="event_msg",payload=new{type="item_completed",turn_id="fixture-turn",item=new{id="fixture-many",delivery="async",questions=Enumerable.Range(0,70).Select(i=>new{title="Question "+i,options=new[]{"Yes","No"}})}}}));feed.Publish();
  check(feed.Snapshot?.requests?.Length==70&&feed.Snapshot.requests.All(q=>q.turn=="fixture-turn"),"original reader preserves all 70 pending questions and their source turn");
  var q=new QueuedQuestion("fixture-call:0","Choose one",["One","Two"],"turn");
  var chat=new Conversation("fixture-thread",[new("user","","previous",1)],false,[q]);
  string Receipt(string item,string answer)=>"<send_user_message_question_reply>\n"+JsonSerializer.Serialize(new[]{new{answer,question=q.title,questionItemId=JsonSerializer.Serialize(new object[]{"request_user_input_async",item,0})}})+"\n</send_user_message_question_reply>";
  var record=chat with{messages=[..chat.messages,new("user","",Receipt("fixture-call","One"),2)]};
  check(NativeRequestReceipt.Accepted(record,chat.id,q.id,"One",1),"native exact original reply proves receipt");
  check(!NativeRequestReceipt.Accepted(record,chat.id,q.id,"Two",1)&&!NativeRequestReceipt.Accepted(record,chat.id,"other:0","One",1)&&!NativeRequestReceipt.Accepted(record,chat.id,q.id,"One",2),"native receipt rejects wrong answer, request and old sequence");
  check(NativeRequestReceipt.Read(Receipt("fixture-call","한글\n😀  공백"))[0].Answer=="한글\n😀  공백","receipt preserves Unicode and whitespace");
  int entered=0;bool broken=false;
  ConsoleFrame Frame()=>new("fixture","• Queued follow-up inputs\n\nChoose one\n\n› 1. One\n  2. Two\n  3. Other\n\nenter submit ctrl + ] skip alt + ↓ main prompt",80,25,0,0,[]);
  Conversation Current(){if(broken&&entered>0)throw new IOException("synthetic read loss after Enter");return entered==0?chat:record;}
  var command=JsonSerializer.SerializeToElement(new{request=Guid.NewGuid().ToString(),kind="request",thread=chat.id,requestKey=q.id,question=q.title,choices=new[]{"One","Two","직접 입력"},choice=0,text="",confirmReceipt=true});
  var result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Frame,Current,_=>{},(key,alt)=>{if(key=="ENTER")entered++;},command));
  check(result.Str("delivery")=="Accepted"&&entered==1,"native adapter accepts only original receipt and sends Enter once");
  entered=0;broken=true;result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Frame,Current,_=>{},(key,alt)=>{if(key=="ENTER")entered++;},command));
  check(result.Str("delivery")=="SentUnconfirmed"&&entered==1,"read loss after Enter remains unknown, never rejected or repeated");
 }
}
