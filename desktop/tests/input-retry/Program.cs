using System.Diagnostics;
namespace TerminalLink;

// Compile the production guard itself. Only its record/console dependencies are
// synthetic; this executable cannot attach to a console or send native input.
internal record ConsoleFrame(string identity,bool shell=false,string thread="00000000-0000-4000-8000-000000000123");
internal record Conversation(string id,string source="record",string readState="ready");
internal static class LocalDaemonFeed {
 internal static int Calls,Failures;
 internal static bool Permanent;
 internal static string Identity(ConsoleFrame f)=>f.identity;
 internal static bool ShellActive(ConsoleFrame f)=>f.shell;
 internal static Conversation Read(ConsoleFrame f)=>new(f.thread);
 internal static bool CorroboratesPhoto(ConsoleFrame f,string thread){Calls++;return !Permanent&&Calls>Failures&&f.thread==thread;}
 internal static void Reset(int failures=0,bool permanent=false){Calls=0;Failures=failures;Permanent=permanent;}
}
internal static class Program {
 const string Thread="00000000-0000-4000-8000-000000000123";
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static void Reject(Func<ConsoleFrame> read){try{LocalImageTarget.Connect(read,Thread);throw new Exception("Unverified input target accepted");}catch(IOException){}}
 static void Main(){
  var frame=new ConsoleFrame("same-native-process-and-session");
  LocalDaemonFeed.Reset();var immediate=LocalImageTarget.Connect(()=>frame,Thread);
  Check(LocalDaemonFeed.Calls==1,"already verified input proceeds without a retry delay");
  LocalDaemonFeed.Reset(2);int reads=0,submits=0;var watch=Stopwatch.StartNew();
  var recovered=LocalImageTarget.Connect(()=>{reads++;return frame;},Thread);submits++;
  Check(LocalDaemonFeed.Calls==3&&reads==4&&submits==1&&watch.ElapsedMilliseconds>=180&&watch.ElapsedMilliseconds<2500,"two transient evidence failures recover on the same submit before any input");
  recovered.Check(frame);
  LocalDaemonFeed.Reset(1);reads=0;
  Reject(()=>++reads<=2?frame:frame with{identity="different-native-session"});
  Check(LocalDaemonFeed.Calls==1,"native session change during retry is rejected before a second evidence check");
  LocalDaemonFeed.Reset(1);reads=0;
  Reject(()=>++reads<=2?frame:frame with{shell=true});
  Check(LocalDaemonFeed.Calls==1,"return to a shell during retry cannot receive the pending chat input");
  LocalDaemonFeed.Reset(permanent:true);watch.Restart();Reject(()=>frame);
  Check(LocalDaemonFeed.Calls>1&&watch.ElapsedMilliseconds>=6900&&watch.ElapsedMilliseconds<8500,"permanent missing or ambiguous evidence fails within the existing seven second deadline");
  LocalDaemonFeed.Reset();watch.Restart();Reject(()=>frame with{thread="00000000-0000-4000-8000-000000000456"});
  Check(LocalDaemonFeed.Calls==0&&watch.ElapsedMilliseconds<8500,"another original conversation is never accepted or corroborated");
  LocalDaemonFeed.Reset();try{recovered.Check(frame with{thread="00000000-0000-4000-8000-000000000456"});throw new Exception("Changed target accepted after connect");}catch(IOException){}
  Check(LocalDaemonFeed.Calls==0,"existing post-connect original identity protection is preserved");
 }
}
