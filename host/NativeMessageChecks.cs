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
  check(result.Bool("ok")&&events.Count==2&&events[0].Str("text")=="한글 message\nSecond line 👋"&&events[0].Bool("literalNewlines")&&events[1].Str("key")=="ENTER","conversation dispatch writes exact Unicode/multiline text then one Enter");
  check(now==350&&checks==3,"acknowledged send uses terminal queue settlement and target lifetime checks");
  var expected=events.Select(e=>e.GetRawText()).ToArray();events.Clear();now=0;
  writer=new(m=>events.Add(m.Clone()),()=>false,Current,()=>now,ms=>now+=ms);
  writer.Send(J(new{op="text",text="한글 message\nSecond line 👋",literalNewlines=true}));writer.Send(J(new{op="key",key="ENTER"}));
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
 }
}
#endif
