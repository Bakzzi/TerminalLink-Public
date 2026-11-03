namespace TerminalLink;
// A separate, per-send native-TUI guard. Display readOnly never becomes generic write authority.
internal sealed class LocalImageTarget {
 internal const int PreparationMilliseconds=30000,MatchMilliseconds=7000,DeliverySeconds=60;
 readonly string identity,thread;
 LocalImageTarget(string identity,string thread){this.identity=identity;this.thread=thread;}
 internal static bool Accepts(Conversation? value,string thread)=>Guid.TryParse(thread,out _)&&value is {source:"record",readState:"ready"}&&value.id==thread;
 internal static LocalImageTarget Connect(Func<ConsoleFrame> read,string thread)=>Connect(read,thread,f=>LocalDaemonFeed.Identity(f),
  LocalDaemonFeed.ShellActive,LocalDaemonFeed.Read,LocalDaemonFeed.CorroboratesPhoto,()=>LocalDaemonFeed.CatalogReady,
  ()=>Environment.TickCount64,()=>Thread.Sleep(100));
 internal static LocalImageTarget Connect(Func<ConsoleFrame> read,string thread,Func<ConsoleFrame,string> identify,
  Func<ConsoleFrame,bool> shell,Func<ConsoleFrame,Conversation?> lookup,Func<ConsoleFrame,string,bool> corroborate,
  Func<bool> ready,Func<long> now,Action wait){
  if(!Guid.TryParse(thread,out _))throw new IOException("사진을 보낼 대화를 확인할 수 없습니다.");
  string identity=identify(read());
  if(identity.Length==0)throw new IOException("사진을 보낼 Codex 터미널을 확인할 수 없습니다.");
  long started=now(),readyAt=-1;
  do{
   var frame=read();
   if(identify(frame)!=identity||shell(frame))throw new IOException("Codex 터미널이 바뀌었습니다.");
   var value=lookup(frame);
   if(Accepts(value,thread)){
    // Starting a write requires current visible question/answer evidence too, not only a saved link.
    if(!corroborate(frame,thread))throw new IOException("현재 Codex 대화를 확인하지 못했습니다. 터미널의 최신 대화를 확인하세요.");
    return new(identity,thread);
   }
   // Catalog preparation can exceed the matching budget on the first send.
   // Keep the same submission waiting; do not make a second click warm it up.
   if(ready()){
    if(readyAt<0)readyAt=now();
    if(now()-readyAt>=MatchMilliseconds)throw new IOException("첨부를 보낼 대화가 바뀌었거나 아직 연결되지 않았습니다.");
   }
   wait();
  }while(now()-started<PreparationMilliseconds);
  throw new IOException("대화 준비가 지연되어 첨부 전송을 시작하지 못했습니다. 첨부와 작성한 내용은 유지됩니다.");
 }
 internal void Check(ConsoleFrame frame){
  if(LocalDaemonFeed.Identity(frame)!=identity||LocalDaemonFeed.ShellActive(frame)||!Accepts(LocalDaemonFeed.Read(frame),thread))
   throw new IOException("대화 또는 Codex 터미널이 바뀌어 사진 전송을 중단했습니다.");
 }
}
