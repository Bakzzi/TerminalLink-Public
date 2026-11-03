using System.Text.Json;
namespace TerminalLink;
internal static class Program {
 static readonly List<string> records=new();
 static CodexFeed feed=new();
 static void Record(object record){string line=JsonSerializer.Serialize(record);records.Add(line);feed.AddRecord(System.Text.Encoding.UTF8.GetBytes(line));feed.Publish();}
 static void User(string text)=>Record(new{type="response_item",payload=new{type="message",role="user",content=new[]{new{type="input_text",text}}}});
 static void Question(string call)=>Record(new{type="event_msg",payload=new{type="item_completed",turn_id="turn",item=new{id=call,delivery="async",questions=new[]{new{title="Question "+call,options=new[]{"One","Two"}}}}}});
 static void Reset(){records.Clear();feed=new();Record(new{type="session_meta",payload=new{id="fixture"}});Record(new{type="event_msg",payload=new{type="task_started",turn_id="turn"}});}
 static void Check(bool yes,string name){if(!yes)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Count=>feed.Snapshot!.requests!.Length;
 static void Main(){
  Reset();Record(new{type="event_msg",timestamp="2026-10-28T08:51:40Z",payload=new{type="token_count",rate_limits=new{limit_id="codex",primary=new{used_percent=12.0,window_minutes=300,resets_at=1793774145L},secondary=new{used_percent=42.0,window_minutes=10080,resets_at=1793774145L}}}});
  Check(feed.Snapshot!.weeklyUsage?.usedPercent==42,"weekly window is selected by duration, not primary position");
  Record(new{type="event_msg",timestamp="2026-10-28T08:52:40Z",payload=new{type="token_count",rate_limits=new{limit_id="codex",primary=new{used_percent=999.0,window_minutes=10080,resets_at=1793774145L},secondary=(object?)null}}});
  Check(feed.Snapshot!.weeklyUsage?.usedPercent==42,"invalid usage cannot overwrite the last verified snapshot");
  Record(new{type="event_msg",timestamp="2026-10-28T08:53:40Z",payload=new{type="token_count",rate_limits=new{limit_id="other",primary=new{used_percent=15.0,window_minutes=10080,resets_at=1793774145L}}}});
  Check(feed.Snapshot!.weeklyUsage?.usedPercent==42,"other quota buckets are not displayed as Codex weekly usage");
  Reset();Question("one");Question("two");User("Continue with my new instruction instead");
  Check(Count==0,"ordinary accepted user input retires pending questions during the same active turn, before any final answer");
  Check(feed.Snapshot!.messages.Single().text=="Continue with my new instruction instead","retirement preserves the exact original user message");
  Question("three");Check(Count==1&&feed.Snapshot!.requests![0].id=="three:0","a new request after the user input remains pending");
  var replay=new CodexFeed();foreach(string line in records)replay.AddRecord(System.Text.Encoding.UTF8.GetBytes(line));replay.Publish();
  Check(replay.Snapshot!.requests!.Select(q=>q.id).SequenceEqual(new[]{"three:0"}),"reconnect/full replay cannot resurrect the superseded requests");
  Reset();Question("one");Question("two");
  string reply="<send_user_message_question_reply>"+JsonSerializer.Serialize(new[]{new{question="Question one",answer="One",questionItemId=JsonSerializer.Serialize(new object[]{"request_user_input_async","one",0})}})+"</send_user_message_question_reply>";
  User(reply);Check(Count==1&&feed.Snapshot!.requests![0].id=="two:0","a structured answer retires only its exact question and preserves the other pending question");
  User("<send_user_message_question_reply>broken</send_user_message_question_reply>");Check(Count==1,"malformed answer envelopes cannot retire another question");
  User("> Question two\n\nTwo");Check(Count==0,"legacy quoted question replies still retire their matching request");
  Reset();Record(new{type="event_msg",payload=new{type="item_completed",turn_id="turn",item=new{id="format",delivery="async",questions=new[]{new{title="Question with\n\ntwo paragraphs",options=new[]{"One"}}}}}});Question("other");
  string formatted="<send_user_message_question_reply>"+JsonSerializer.Serialize(new[]{new{question="Question with  two paragraphs",answer="One",questionItemId=JsonSerializer.Serialize(new object[]{"request_user_input_async","format",0})}})+"</send_user_message_question_reply>";
  User(formatted);Check(Count==1&&feed.Snapshot!.requests![0].id=="other:0","a reply with flattened paragraph whitespace retires only its exact question");
  var formattedReplay=new CodexFeed();foreach(string line in records)formattedReplay.AddRecord(System.Text.Encoding.UTF8.GetBytes(line));formattedReplay.Publish();
  Check(formattedReplay.Snapshot!.requests!.Select(q=>q.id).SequenceEqual(new[]{"other:0"}),"full replay removes the already answered formatted question");
  Reset();Question("one");User("<send_user_message_question_reply>"+JsonSerializer.Serialize(new[]{new{question="Different question",answer="One",questionItemId=JsonSerializer.Serialize(new object[]{"request_user_input_async","one",0})}})+"</send_user_message_question_reply>");
  Check(Count==1,"same ID with genuinely different question text cannot resolve a request");
  Reset();Question("one");
  User("# AGENTS.md instructions for fixture");Check(Count==1,"injected environment instructions do not cancel a request");
  feed.AddRecord(System.Text.Encoding.UTF8.GetBytes("{unfinished"));feed.Publish();Check(Count==1,"partial or unreadable records preserve the pending request");
  Record(new{type="response_item",payload=new{type="message",role="assistant",phase="commentary",content=new[]{new{type="output_text",text="I am still working"}}}});Check(Count==1,"assistant progress does not cancel the question");
  Record(new{type="response_item",payload=new{type="message",role="user",content=new[]{new{type="input_image",image_url="fixture"}}}});
  Check(Count==0,"accepted image-only user input also supersedes outstanding questions");
 }
}
internal static class JsonStrings {
 internal static string Str(this JsonElement value,string name)=>value.TryGetProperty(name,out var part)&&part.ValueKind==JsonValueKind.String?part.GetString()??"":"";
}
