namespace TerminalLink;
// A separate, per-send native-TUI guard. Display readOnly never becomes generic write authority.
internal sealed class LocalImageTarget {
 readonly string identity,thread;
 LocalImageTarget(string identity,string thread){this.identity=identity;this.thread=thread;}
 internal static bool Accepts(Conversation? value,string thread)=>Guid.TryParse(thread,out _)&&value is {source:"record",readState:"ready"}&&value.id==thread;
 internal static LocalImageTarget Connect(Func<ConsoleFrame> read,string thread){
  if(!Guid.TryParse(thread,out _))throw new IOException("사진을 보낼 대화를 확인할 수 없습니다.");
  string identity=LocalDaemonFeed.Identity(read());
  if(identity.Length==0)throw new IOException("사진을 보낼 Codex 터미널을 확인할 수 없습니다.");
  long deadline=Environment.TickCount64+7000;
  do{
   var frame=read();
   if(LocalDaemonFeed.Identity(frame)!=identity||LocalDaemonFeed.ShellActive(frame))throw new IOException("Codex 터미널이 바뀌었습니다.");
   var value=LocalDaemonFeed.Read(frame);
   if(Accepts(value,thread)){
    // Starting a write requires current visible question/answer evidence too, not only a saved link.
    if(!LocalDaemonFeed.CorroboratesPhoto(frame,thread))throw new IOException("현재 Codex 대화를 확인하지 못했습니다. 터미널의 최신 대화를 확인하세요.");
    return new(identity,thread);
   }
   Thread.Sleep(100);
  }while(Environment.TickCount64<deadline);
  throw new IOException("사진을 보낼 대화가 바뀌었거나 아직 연결되지 않았습니다.");
 }
 internal void Check(ConsoleFrame frame){
  if(LocalDaemonFeed.Identity(frame)!=identity||LocalDaemonFeed.ShellActive(frame)||!Accepts(LocalDaemonFeed.Read(frame),thread))
   throw new IOException("대화 또는 Codex 터미널이 바뀌어 사진 전송을 중단했습니다.");
 }
}
