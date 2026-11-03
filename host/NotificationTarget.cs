using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Renci.SshNet;
namespace TerminalLink;
// Input always goes to the existing console. A record reader is never an input endpoint.
internal sealed class NotificationTarget : IDisposable {
 readonly Func<ConsoleFrame> read;readonly ConversationReader reader;readonly string thread,identity;
 readonly long deadline=Environment.TickCount64+45000;
 RemoteCodexFeed.Binding? binding;SshClient? client;CodexFeed remoteFeed=new();long offset;
 LocalImageTarget? local;
 internal bool Remote=>binding!=null;
 NotificationTarget(Func<ConsoleFrame> read,ConversationReader reader,string thread,string identity){this.read=read;this.reader=reader;this.thread=thread;this.identity=identity;}
 internal static NotificationTarget Connect(Func<ConsoleFrame> read,string thread){
  var reader=new ConversationReader();var frame=read();string session=frame.session;
  var target=new NotificationTarget(read,reader,thread,LocalDaemonFeed.Identity(frame));
  try{
   long until=Environment.TickCount64+12000;
   do{
    frame=read();if(frame.session!=session||LocalDaemonFeed.ShellActive(frame))throw new IOException("알림의 터미널이 종료되거나 바뀌었습니다.");
    var conversation=reader.Read(frame);
    if(ConversationReader.IsOriginal(conversation)){
     if(conversation!.id!=thread)throw new IOException("알림의 대화가 변경되었습니다.");
     if(conversation.source=="remote"){
      try{target.binding=RemoteCodexFeed.BoundTo(frame,thread);}
      catch(IOException)when(Environment.TickCount64<until){Thread.Sleep(100);continue;}
      target.client=RemoteCodexFeed.Connect(RemoteCodexFeed.RequireProfile(target.binding,frame));
     }else if(conversation.readOnly)target.local=LocalImageTarget.Connect(read,thread);
     if(!target.Remote&&target.identity.Length==0)throw new IOException("현재 Codex 터미널을 확인할 수 없습니다.");
     target.Current();return target;
    }
    Thread.Sleep(100);
   }while(Environment.TickCount64<until);
   throw new IOException("알림의 대화 기록을 확인하지 못했습니다. 앱에서 다시 연결하세요.");
  }catch{target.Dispose();throw;}
 }
 internal Conversation Current(){
  if(Environment.TickCount64>=deadline)throw new IOException("답장 연결 시간이 초과되었습니다. 앱에서 확인하세요.");
  var frame=read();if(LocalDaemonFeed.ShellActive(frame))throw new IOException("Codex가 종료되어 답장을 중단했습니다.");
  if(binding is RemoteCodexFeed.Binding b){
   var profile=RemoteCodexFeed.RequireProfile(b,frame);
   string request=JsonSerializer.Serialize(new{connection=b.endpoint.Connection,path=b.path,tui=b.tui,stamp=b.stamp,offset,anchors=Array.Empty<string>()});
   using var command=client!.CreateCommand("python3 -c "+RemoteCodexFeed.Quote(RemoteCodexFeed.Script)+" "+RemoteCodexFeed.Quote(request));command.CommandTimeout=TimeSpan.FromSeconds(5);
   using var doc=JsonDocument.Parse(command.Execute());var result=doc.RootElement;
   if(command.ExitStatus!=0||!result.Bool("available")||result.Str("stamp")!=b.stamp||result.Str("path")!=b.path||result.Str("tui")!=b.tui||(long)result.Num("start")!=offset)throw new IOException("원격 알림의 대화 또는 터미널이 바뀌었습니다.");
   foreach(var record in result.GetProperty("records").EnumerateArray())remoteFeed.AddRecord(Encoding.UTF8.GetBytes(record.GetRawText()));
   offset=(long)result.Num("offset");remoteFeed.Publish();
   string actual="remote:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.host+":"+profile.port+":"+profile.user+":"+remoteFeed.Snapshot!.id)));
   if(actual!=thread)throw new IOException("원격 알림의 원본이 바뀌었습니다.");
   RemoteCodexFeed.RequireProfile(b,read());
   return remoteFeed.Snapshot! with{id=thread,source="remote",readOnly=true};
  }
  if(LocalDaemonFeed.Identity(frame)!=identity)throw new IOException("Codex 터미널이 변경되었습니다.");
  local?.Check(frame);var conversation=reader.Read(frame);
  if(!ConversationReader.IsOriginal(conversation)||conversation!.id!=thread)throw new IOException("알림의 대화가 변경되었습니다.");
  return conversation;
 }
 public void Dispose(){client?.Dispose();}
}
