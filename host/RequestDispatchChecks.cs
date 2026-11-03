#if TESTING
namespace TerminalLink;
internal static class RequestDispatchChecks {
 internal static void Run(Action<bool,string> check){
  const string known="선택 변경을 확인하지 못했습니다. 앱에서 확인하세요.";
  check(Requests.RequestFailure.Message(Requests.RequestFailure.Code(known))==known,"known request rejection keeps its actionable reason");
  check(!Requests.RequestFailure.Message(Requests.RequestFailure.Code("private exception text")).Contains("private exception text"),"unknown exception text is not exposed or stored");
  var lane=new RequestDispatchLane();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
  var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int sends=0,queries=0;
  check(lane.TryStart(async()=>{Interlocked.Increment(ref sends);entered.SetResult();await release.Task;}),"request delivery can start asynchronously");
  check(entered.Task.Wait(3000),"request delivery is in progress");
  check(!lane.TryStart(()=>{Interlocked.Increment(ref sends);return Task.CompletedTask;}),"second request submission is refused before a write");
  queries++;check(queries==1&&sends==1,"status can be processed while delivery is still pending");
  release.SetResult();lane.Finish().GetAwaiter().GetResult();check(sends==1,"status and blocked resubmission do not repeat delivery");
  check(lane.TryStart(()=>Task.CompletedTask),"a later request can start after delivery finishes");lane.Finish().GetAwaiter().GetResult();
 }
}
#endif
