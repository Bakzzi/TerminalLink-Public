#if TESTING
using System.Text.Json;
using TerminalLink.Requests;
namespace TerminalLink;
internal static class RequestShutdownChecks {
 sealed class Journal : IRequestJournal {
  public RequestDocument? Load()=>null;
  public void Save(RequestDocument document){}
 }
 sealed class NonPumpingContext : SynchronizationContext {
  public override void Post(SendOrPostCallback callback,object? state){}
 }
 internal static void Run(Action<bool,string> check){
  string pipe="TerminalLink.Requests.ShutdownFixture."+Guid.NewGuid().ToString("N");
  var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var server=new RequestIpcServer(new RequestCoordinator(new Journal()),_=>null,pipe,native:async (_,_)=>{
   entered.TrySetResult(); await release.Task; return JsonSerializer.SerializeToElement(new { ok=true });
  });
  var client=RequestIpcClient.Send(new RequestCommand(1,"native",Native:JsonSerializer.SerializeToElement(new {})),CancellationToken.None,pipe);
  check(entered.Task.Wait(5000),"isolated request reached IPC handler");
  Exception? error=null; using var started=new ManualResetEventSlim();
  var thread=new Thread(()=>{try{SynchronizationContext.SetSynchronizationContext(new NonPumpingContext());started.Set();server.DisposeAsync().AsTask().GetAwaiter().GetResult();}catch(Exception e){error=e;}}){IsBackground=true};
  thread.Start(); started.Wait(); Thread.Sleep(150); release.SetResult();
  bool exited=thread.Join(5000);
  check(exited&&error==null,"IPC shutdown finishes on a non-pumping UI synchronization context");
  _=client.ContinueWith(t=>{_ = t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
 }
}
#endif
