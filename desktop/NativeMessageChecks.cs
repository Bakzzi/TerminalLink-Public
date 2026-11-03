#if TESTING || DESKTOP
using System.Text.Json;
namespace TerminalLink;
internal static class NativeMessageChecks {
 internal static void Run(Action<bool,string> check){
  static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value);
  var command=J(new{kind="message",request="fixture",text="한글 message\r\nSecond line 👋"});
  var events=new List<JsonElement>();long now=0;bool live=true;int checks=0;
  void Current(){checks++;if(!live)throw new IOException("selected console closed");}
  var writer=new OrderedNativeInput(m=>events.Add(m.Clone()),()=>false,Current,()=>now,ms=>now+=ms);
  var result=J(NativeMessageInput.Execute(writer.Send,command));
  check(result.Bool("ok")&&result.Str("delivery")=="InputWritten"&&events.Count==2&&events[0].Str("text")=="한글 message\nSecond line 👋"&&events[0].Bool("literalNewlines")&&!events[0].Bool("bracketedPaste")&&events[1].Str("key")=="ENTER","local dispatch writes exact Unicode/multiline text then one Enter; ack means input written only");
  check(now==350&&checks==3,"acknowledged send uses terminal queue settlement and target lifetime checks");
  var expected=events.Select(e=>e.GetRawText()).ToArray();events.Clear();now=0;
  writer=new(m=>events.Add(m.Clone()),()=>false,Current,()=>now,ms=>now+=ms);
  writer.Send(J(new{op="text",text="한글 message\nSecond line 👋",literalNewlines=true,bracketedPaste=false}));writer.Send(J(new{op="key",key="ENTER"}));
  check(events.Select(e=>e.GetRawText()).SequenceEqual(expected),"terminal and conversation share the identical ordered native command sequence");
  // The API intentionally has no frame/history input: U+00BB, old U+203A,
  // missing cursor, repaint, clipped history and absent records cannot veto it.
  events.Clear();now=0;live=false;bool failed=false;
  try{NativeMessageInput.Execute(writer.Send,command);}catch(IOException){failed=true;}
  check(failed&&events.Count==0,"closed selected native target receives no input");
  live=true;events.Clear();writer=new(m=>{events.Add(m);live=false;},()=>false,Current,()=>now,ms=>now+=ms);failed=false;
  try{NativeMessageInput.Execute(writer.Send,command);}catch(IOException){failed=true;}
  check(failed&&events.Count==1&&events[0].Str("op")=="text","target closure after text never receives Enter or an automatic second send");
  live=true;int attempts=0;writer=new(_=>{attempts++;throw new IOException("OS write failed");},()=>false,Current,()=>now,ms=>now+=ms);failed=false;
  try{NativeMessageInput.Execute(writer.Send,command);}catch(IOException){failed=true;}
  check(failed&&attempts==1,"actual native write failure propagates without acknowledgement or repeat");
  string draft="existing native draft: ";int enters=0;
  writer=new(m=>{if(m.Str("op")=="text")draft+=m.Str("text");else enters++;},()=>false,Current,()=>now,ms=>now+=ms);
  NativeMessageInput.Execute(writer.Send,J(new{request="fixture",text="appended"}));
  check(draft=="existing native draft: appended"&&enters==1,"ordinary input appends like terminal view and never clears the user's draft");
  now=0;events.Clear();int polls=0;
  writer=new(m=>events.Add(m),()=>++polls<4,Current,()=>now,ms=>now+=ms);
  NativeMessageInput.Execute(writer.Send,command);
  check(events.Count==2&&polls==4&&now==380,"busy native queue drains before a single Enter without retrying text");
  check(!NativeMessageInput.Handles("answer")&&!NativeMessageInput.Handles("request"),"notification replies and request decisions retain their separate original-target route");
  // The remote reader can process text + Enter together despite the local gap.
  // Exercise actual native paste records and the receiver's burst/paste boundary.
  foreach(string text in new[]{"한글 message", "  first\nsecond 🐱\n", new string('a',4096)}){
   events.Clear();now=0;writer=new(m=>events.Add(m),()=>false,Current,()=>now,ms=>now+=ms);
   NativeMessageInput.Execute(writer.Send,J(new{request="remote-fixture",text}),remote:true);
   check(events.Count==2&&events[0].Bool("bracketedPaste")&&events[1].Str("key")=="ENTER","SSH uses explicit paste and exactly one submit for "+text.Length+" characters");
   var records=ConsoleNative.RemotePasteEvents(events[0].Str("text"));
   string wire=new(records.Select(r=>r.Unicode).ToArray());
   check(records.All(r=>r.Type==1&&r.Down==1&&r.Repeat==1)&&wire=="\x1b[200~"+text+"\x1b[201~","native SSH records preserve Unicode/whitespace/newlines inside paste boundaries");
   var delayed=new DelayedPasteReceiver();delayed.Receive(text+"\r");
   check(delayed.Submitted==null&&delayed.Draft==text+"\n","coalesced unframed input reproduces text stranded in the remote draft");
   delayed=new();delayed.Receive(wire+"\r");
   check(delayed.Submitted==text&&delayed.Draft=="","coalesced framed input submits once instead of absorbing Enter into the draft");
  }
  bool rejected=false;try{ConsoleNative.RemotePasteEvents("escape\x1b[201~");}catch(IOException){rejected=true;}
  check(rejected,"user text cannot inject a paste terminator or another control sequence");
 }
 // Minimal model of the paste-burst boundary, not a full Codex/SSH integration
 // test. Codex: codex-rs/tui/src/bottom_pane/paste_burst.rs (120 ms window,
 // clear_after_explicit_paste). Deliver the delayed packet in one read.
 sealed class DelayedPasteReceiver {
  internal string Draft="";internal string? Submitted;
  internal void Receive(string wire){
   if(wire.StartsWith("\x1b[200~",StringComparison.Ordinal)){
    int end=wire.IndexOf("\x1b[201~",StringComparison.Ordinal);Draft+=wire[6..end];
    if(wire[(end+6)..]=="\r"){Submitted=Draft;Draft="";}
   }else Draft+=wire.Replace('\r','\n');
  }
 }
}
#endif
