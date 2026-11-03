using System.Text.Json;
namespace TerminalLink;

// One ordered writer for terminal keys and acknowledged conversation messages.
// It never reads screen text, resolves history, clears a draft or retries input.
internal sealed class OrderedNativeInput(Action<JsonElement> write,Func<bool> pending,Action current,Func<long>? clock=null,Action<int>? delay=null) {
 readonly Func<long> now=clock??(()=>Environment.TickCount64);
 readonly Action<int> wait=delay??Thread.Sleep;
 long lastText=-1;
 internal void Send(JsonElement message){
  current();
  if(message.Str("op")=="key"&&message.Str("key")=="ENTER"&&!message.Bool("ctrl")&&!message.Bool("alt")&&!message.Bool("shift")&&lastText>=0){
   long deadline=now()+2000;bool drained=false;
   while(pending()&&now()<deadline){drained=true;wait(10);}
   long remaining=drained?350:350-(now()-lastText);
   if(remaining>0)wait((int)remaining);
   current();lastText=-1;
  }
  write(message);
  if(message.Str("op")=="text"&&message.Str("text").Length>0)lastText=now();
  else if(message.Str("op")=="key"&&message.Str("key")!="ENTER")lastText=-1;
 }
}
