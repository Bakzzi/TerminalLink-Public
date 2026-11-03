namespace TerminalLink;

// A phone may query status while one original request is being delivered.
// A second submission is refused before writing; delivery is never repeated here.
internal sealed class RequestDispatchLane {
 Task? active;
 internal bool TryStart(Func<Task> operation){
  if(active is { IsCompleted:false })return false;
  active=Task.Run(operation);return true;
 }
 internal async Task Finish(){if(active!=null)try{await active.ConfigureAwait(false);}catch(OperationCanceledException){}}
}
