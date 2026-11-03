using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace TerminalLink;
internal sealed record BackgroundFrame(int Pid,long Started,ConsoleFrame? Frame);
internal sealed record BackgroundObservation(DesktopTab Tab,Conversation? Conversation);

// A single private read-only worker samples inactive consoles. It neither selects
// a native tab nor opens CONIN$. No per-tab console processes are retained here.
internal sealed class DesktopBackgroundReader : IDisposable {
 Process? worker;
 internal async Task<BackgroundFrame[]> Read(ConsoleTarget[] targets,CancellationToken cancellation){
  var frames=new List<BackgroundFrame>();await foreach(var frame in Stream(targets,cancellation))frames.Add(frame);return frames.ToArray();
 }
 internal async IAsyncEnumerable<BackgroundFrame> Stream(ConsoleTarget[] targets,[EnumeratorCancellation]CancellationToken cancellation){
  if(targets.Length==0)yield break;
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(TimeSpan.FromSeconds(10));
  bool complete=false;
  try{
   if(worker==null||worker.HasExited){Dispose();worker=Process.Start(DesktopProgram.Worker("--background-reader"))??throw new IOException("백그라운드 확인을 시작하지 못했습니다.");_=worker.StandardError.ReadToEndAsync();}
   await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(targets.Select(t=>new Request(t.Pid,t.Started))).AsMemory(),timeout.Token).ConfigureAwait(false);await worker.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
   for(int i=0;i<targets.Length;i++){
    string? line=await worker.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);if(line==null)throw new IOException("백그라운드 확인 연결이 끊겼습니다.");
    var item=JsonSerializer.Deserialize<BackgroundFrame>(line)??throw new IOException("확인 응답이 없습니다.");
    if(item.Pid!=targets[i].Pid||item.Started!=targets[i].Started)throw new IOException("확인 대상이 바뀌었습니다.");yield return item;
   }
   complete=true;
  }finally{if(!complete)Dispose();}
 }
 public void Dispose(){var p=worker;worker=null;if(p==null)return;try{p.StandardInput.Close();if(!p.WaitForExit(500))p.Kill();}catch{}p.Dispose();}
 internal sealed record Request(int Pid,long Started);
 [DllImport("kernel32.dll")]static extern bool SetConsoleCtrlHandler(IntPtr handler,bool add);
 internal static void Agent(){
  using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
  string? line;while((line=input.ReadLine())!=null){
   if(line.Length>1048576)return;var requests=JsonSerializer.Deserialize<Request[]>(line);if(requests==null)return;
   var allowed=ConsoleNative.Targets(DesktopSession.Windows());
   foreach(var request in requests){
    ConsoleFrame? frame=null;bool attached=false;var handle=new IntPtr(-1);
    try{
     var target=allowed.SingleOrDefault(t=>t.Pid==request.Pid&&t.Started==request.Started);
     if(target!=null&&target.Valid&&ConsoleNative.AttachConsole((uint)target.Pid)){
      attached=true;SetConsoleCtrlHandler(IntPtr.Zero,true);
      handle=ConsoleNative.CreateFile("CONOUT$",0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
      if(handle!=new IntPtr(-1)&&target.Valid)frame=ConsoleNative.Read(handle) with{session=$"{target.Pid}:{target.Started}"};
     }
    }catch{}finally{if(handle!=new IntPtr(-1))ConsoleNative.CloseHandle(handle);if(attached)ConsoleNative.FreeConsole();}
    output.WriteLine(JsonSerializer.Serialize(new BackgroundFrame(request.Pid,request.Started,frame)));
   }
  }
 }
 internal static async Task Check(){
  using var reader=new DesktopBackgroundReader();var targets=ConsoleNative.Targets(DesktopSession.Windows()).ToArray();
  var samples=new List<long>();bool success=targets.Length>0;int read=0;
  for(int i=0;i<4;i++){var watch=Stopwatch.StartNew();var frames=await reader.Read(targets,CancellationToken.None);samples.Add(watch.ElapsedMilliseconds);read=frames.Count(f=>f.Frame!=null);success&=frames.All(f=>f.Frame!=null&&f.Frame.session==$"{f.Pid}:{f.Started}");if(i<3)await Task.Delay(1000);}
  long workerMemory=0;if(reader.worker!=null){reader.worker.Refresh();workerMemory=reader.worker.WorkingSet64;}
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"desktop-background-check.json"),JsonSerializer.Serialize(new{success,targets=targets.Length,read,scanMilliseconds=samples,workerMemoryMB=workerMemory/1024/1024,workerProcesses=1,inputsSent=0}));if(!success)Environment.ExitCode=1;
 }
}
