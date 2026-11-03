#if TESTING
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace TerminalLink;
internal static class PublicChecks {
 internal static void Run(){
  var lines=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);lines.Add("PASS "+name);}
  var plain="Hello\n\n한국어 👋  ";
  Check(DisplayText.Assistant(plain)==plain,"Plain Unicode and whitespace preserved");
  string tag="<oai-mem-citation>\n<citation_entries>\nexample\n</citation_entries>\n</oai-mem-citation>";
  Check(DisplayText.Assistant(plain+tag)==plain,"Internal citation footer omitted in presentation");
  string code="```xml\n"+tag+"\n```";Check(DisplayText.Assistant(code)==code,"Code examples preserved");
  var original=new ConversationMessage("assistant","final",plain+tag,1);
  Check(DisplayText.Message(original).text==plain&&original.text==plain+tag,"Original record unchanged");
  string key=JsonSerializer.Serialize(new object[]{"request_user_input_async","call_fixture",0});
  string reply="<send_user_message_question_reply>"+JsonSerializer.Serialize(new[]{new{question="Fixture question",answer=plain,questionItemId=key}})+"</send_user_message_question_reply>";
  Check(DisplayText.User(reply)=="질문: Fixture question\n\n답변: "+plain,"Reply envelope is display-only text");
  Check(DisplayText.User("Example "+reply)=="Example "+reply,"Quoted envelope retained");
  Check(DisplayText.User("<send_user_message_question_reply>{}</send_user_message_question_reply>").Contains("<send_"),"Malformed envelope not silently changed");
  var first=new Conversation("fixture",new[]{original},false);
  var second=first with{messages=new[]{original,new ConversationMessage("user","",plain,2)}};
  Check(EventHub.ReplySequence(first)==0&&EventHub.ReplySequence(second)==2,"Only a recorded user reply advances answer acknowledgement");
  var nextAnswer=second with{messages=second.messages.Append(original with{sequence=3}).ToArray()};
  Check(EventHub.ReplySequence(nextAnswer)==2,"New assistant answer stays beyond the reply acknowledgement boundary");
  Check(EventHub.ReplySequence(second with{source="terminal"})==0&&EventHub.ReplySequence(second with{readState="unavailable"})==0,"Unverified or unavailable history never clears answer notifications");
  Check(EventHub.ReplySequence(second with{source="remote"})==2,"Remote and local original records share the same acknowledgement rule");
  var receipt=new EventHub.Notice("replyEvent","fixture-tab","Fixture",nextAnswer,1,null,false,"",123,EventHub.ReplySequence(nextAnswer));
  using(var wire=JsonDocument.Parse(JsonSerializer.Serialize(receipt)))Check(wire.RootElement.GetProperty("repliedThrough").GetInt64()==2&&wire.RootElement.GetProperty("conversation").GetProperty("messages")[2].GetProperty("sequence").GetInt64()==3,"Event receipt and newer answer retain separate sequence boundaries on the wire");
  using(var delta=JsonDocument.Parse(JsonSerializer.Serialize(ConversationWire.Delta(first,second)))){
   Check(delta.RootElement.GetProperty("messages").GetArrayLength()==1&&delta.RootElement.GetProperty("baseSequence").GetInt64()==1,"Append-only delta has correct base sequence");
  }
  var payload=Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(plain,10000)));using var joined=new MemoryStream();int ix=0;
  var remoteFrame=new ConsoleFrame("fixture","• The monitoring report preserves the original conversation while independently validating each response and every durable reconnect.\n  A second distinct line verifies the selected record and the active remote terminal identity.\n\n› Ask Codex to do anything\n? for shortcuts",180,6,2,4,Array.Empty<int>(),"fixture");
  Check(RemoteCodexFeed.Anchors(remoteFrame).Length==0&&RemoteCodexFeed.AnswerAnchors(remoteFrame).Single().Length==2,"Answer-only remote viewport supplies prose evidence without inventing a user question");
  var remoteTools=remoteFrame with{text="› Restart this\n    + 139 lines (^t to expand)\n\n• Ran a diagnostic\n  └ "+new string('x',200)+"\n\n› Ask Codex to do anything\n? for shortcuts",cursorY=6};
  Check(RemoteCodexFeed.Anchors(remoteTools).Length==0&&RemoteCodexFeed.AnswerAnchors(remoteTools).Length==0,"Folded question controls and long tool output never become remote identity evidence");
  Check(RemoteCodexFeed.Anchors(remoteTools with{text=remoteTools.text.Replace("^t","ctrl+t")}).Length==0,"Both remote expansion-hint spellings are excluded from exact question matching");
  foreach(var chunk in ConversationWire.Fragments(payload)){
   using var doc=JsonDocument.Parse(JsonSerializer.Serialize(chunk));var c=doc.RootElement;
   Check(c.GetProperty("index").GetInt32()==ix++,"Chunk ordering");joined.Write(Convert.FromBase64String(c.GetProperty("data").GetString()!));
   if(c.GetProperty("last").GetBoolean())Check(c.GetProperty("sha256").GetString()==Convert.ToHexString(SHA256.HashData(payload)),"Payload digest");
  }
  Check(joined.ToArray().SequenceEqual(payload),"Large UTF-8 payload round-trip");
  var feed=new CodexFeed();
  void Add(object item){feed.AddRecord(JsonSerializer.SerializeToUtf8Bytes(item));feed.Publish();}
  Add(new{type="session_meta",payload=new{id="11111111-2222-4333-8444-555555555555",source="cli"}});
  for(int i=0;i<80;i++)Add(new{type="response_item",payload=new{type="message",role=i%2==0?"user":"assistant",phase="final",content=new[]{new{type="output_text",text="Synthetic message "+i}}}});
  Check(feed.Snapshot?.messages.Length==80,"History is not capped at a recent 30 messages");
  Check(feed.Snapshot!.messages[^1].text=="Synthetic message 79","Latest complete message preserved");
  string question="Please arrange the card as follows:\nDisplay name: L level\nHP: [bar]\n    current / maximum\n\nKeep this layout in both views.";
  string answer="Both card views use the same requested layout and keep the full name and current values visible.";
  var multiline=new Conversation("multiline-fixture",new[]{new ConversationMessage("user","",question,1),new ConversationMessage("assistant","commentary",answer,2)},false);
  string screen="› "+question.Replace("\n","\n  ")+"\n\n• "+answer+"\n\n› Ask Codex to do anything\n? for shortcuts";
  var multilineFrame=new ConsoleFrame("fixture",screen,100,20,2,screen.Split('\n').Length-2,Array.Empty<int>(),"fixture");
  var multilineCandidate=new LocalDaemonFeed.Candidate("fixture",multiline);
  Check(LocalDaemonFeed.Compatible(multilineFrame,multiline),"Paragraph breaks in a submitted question do not invalidate an existing binding");
  Check(LocalDaemonFeed.Match(multilineFrame,new[]{multilineCandidate})?.id==multiline.id,"New tab uniquely binds the entire multiline question");
  Check(LocalDaemonFeed.Match(multilineFrame,new[]{multilineCandidate,multilineCandidate with{conversation=multiline with{id="duplicate"}}})==null,"Multiline question never bypasses duplicate-record rejection");
  var conflict=multiline with{messages=new[]{multiline.messages[0] with{text=question.Replace("both views","another conversation")},multiline.messages[1]}};
  Check(!LocalDaemonFeed.Compatible(multilineFrame,conflict),"Conflicting text after a paragraph break is not silently ignored");
  var earlyOnly=multiline with{messages=new[]{multiline.messages[0] with{text=question.Split("\n\n")[0]},multiline.messages[1]}};
  Check(LocalDaemonFeed.Match(multilineFrame,new[]{multilineCandidate,new LocalDaemonFeed.Candidate("partial",earlyOnly)})==null,"Ambiguous complete-question forms cannot select an original record");
  var pinnedAnswer=multilineFrame with{text="› "+question.Split('\n')[0]+"\n\n  The middle of an older answer without a speaker marker.\n\n  Worked for 1m 14s\n\n› "+question+"\n• "+answer+"\n› Ask Codex to do anything\n? for shortcuts"};
  var pinnedHistory=multiline with{messages=new[]{multiline.messages[0] with{text=question.Split('\n')[0],sequence=0},multiline.messages[0],multiline.messages[1]}};
  Check(LocalDaemonFeed.Compatible(pinnedAnswer,pinnedHistory)&&LocalDaemonFeed.Match(pinnedAnswer,new[]{new LocalDaemonFeed.Candidate("pinned-history",pinnedHistory)})?.id==multiline.id,"Pinned question above a markerless older answer retains its complete original match");
  var wideAnswer=new Conversation("wide-fixture",new[]{new ConversationMessage("user","","Check this again",1),new ConversationMessage("assistant","commentary",answer,2)},false);
  var wideFrame=multilineFrame with{text="› Check this again\n    unrelated tool output\n    + 4 lines (ctrl+t to expand)\n• "+answer+"\n› Ask Codex to do anything\n? for shortcuts"};
  Check(LocalDaemonFeed.Match(wideFrame,new[]{new LocalDaemonFeed.Candidate("wide",wideAnswer)})?.id==wideAnswer.id,"A wide terminal keeps long exact answer evidence on one visual line");
  Check(LocalDaemonFeed.Match(wideFrame,new[]{new LocalDaemonFeed.Candidate("wide",wideAnswer with{messages=wideAnswer.messages.Take(1).ToArray()})})==null,"Pinned short question without original answer evidence remains rejected");
  var draft=multilineFrame with{text=screen.Replace("Ask Codex to do anything","Unsent different question")};
  Check(LocalDaemonFeed.Compatible(draft,multiline)&&LocalDaemonFeed.Match(draft,new[]{multilineCandidate})?.id==multiline.id,"Unsent composer does not replace multiline identity evidence");
  foreach(string status in new[]{"◦ Working (9s · esc to interrupt)","Worked for 9s"}){
   var busy=multilineFrame with{text=screen.Replace("• "+answer,status)};
   Check(LocalDaemonFeed.Compatible(busy,multiline)&&LocalDaemonFeed.Match(busy,new[]{multilineCandidate})?.id==multiline.id,"Progress or completion status is not appended to a submitted question");
  }
  string answerLines="The original answer contains a distinctive first sentence for identification.\nA second sentence confirms that the visible answer belongs to the same original question.";
  var pinned=new Conversation("pinned-fixture",new[]{new ConversationMessage("user","","Check",1),new ConversationMessage("assistant","final",answerLines,2)},false);
  var pinnedCandidate=new LocalDaemonFeed.Candidate("pinned",pinned);
  var pinnedFrame=multilineFrame with{text="› Check\n    function Example() { unrelated tool text }\n    + 70 lines (ctrl+t to expand)\n\n• "+answerLines+"\n\n› Ask Codex to do anything\n? for shortcuts",cursorY=7};
  Check(LocalDaemonFeed.Compatible(pinnedFrame,pinned)&&LocalDaemonFeed.Match(pinnedFrame,new[]{pinnedCandidate})?.id==pinned.id,"Pinned question above folded tool details still uses ordered answer evidence");
  Check(LocalDaemonFeed.Match(pinnedFrame,new[]{pinnedCandidate with{conversation=pinned with{messages=pinned.messages.Take(1).ToArray()}}})==null,"Short pinned question and tool details alone cannot bind");
  var clipped=multilineFrame with{text="› "+question[..45]+"…\n• "+answerLines+"\n\n› Ask Codex to do anything\n? for shortcuts"};
  var clippedCandidate=multilineCandidate with{conversation=multiline with{messages=new[]{multiline.messages[0],multiline.messages[1] with{text=answerLines,phase="final"}}}};
  Check(LocalDaemonFeed.Match(clipped,new[]{clippedCandidate})?.id==multiline.id,"Clipped distinctive prompt still matches with answer evidence");
  Check(LocalDaemonFeed.Match(clipped,new[]{multilineCandidate})==null,"Clipped prompt without matching answer remains rejected");
  var replyChat=new Conversation("reply-fixture",new[]{new ConversationMessage("user","",reply,1)},false);
  var replyFrame=multilineFrame with{text="› > Fixture question\n\n  "+plain+"\n• Received\n› Ask Codex to do anything\n? for shortcuts"};
  Check(LocalDaemonFeed.Compatible(replyFrame,replyChat),"Request question and answer across paragraphs remain one identity anchor");
  Check(!LocalDaemonFeed.Compatible(replyFrame with{text=replyFrame.text.Replace(plain,"Different answer")},replyChat),"Different request answer cannot match the same question");
  File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"public-checks.txt"),lines);
 }
}
#endif
