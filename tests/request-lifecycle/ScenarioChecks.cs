using TerminalLink.Requests;

static class ScenarioChecks {
 internal static async Task Run(Action<bool,string> check) {
  for(int seed=0;seed<32;seed++) {
   var journal=new MemoryJournal();var manager=new RequestCoordinator(journal);
   RequestDefinition Make(string instance,string item)=>new(new(instance,"thread","turn",item),"userInput",[new("q","Identical question",[new("Yes","")],true)]);
   var first=Make("a","one");var second=Make("a","two");var other=Make("b","one");
   manager.BeginSource("a","epoch");manager.BeginSource("b","epoch");
   manager.Observe("a","epoch",0,true,[first,second]);manager.Observe("b","epoch",0,true,[other]);
   long sequence=0;var random=new Random(seed);
   for(int n=0;n<12;n++) {
    manager.MarkConnection("a",ConnectionState.Stale,"simulated-network-change");
    check(manager.Snapshot("a").Requests.Count(r=>r.Life==LifeState.Open)==2,"network-loss-keeps-both-questions");
    if(random.Next(2)==0)manager.Observe("a","epoch",sequence+2,false,[]);
    manager.Observe("a","epoch",++sequence,true,[first,second]);
    check(manager.Snapshot("b").Requests.Single().Connection==ConnectionState.Fresh,"another-tab-stays-fresh");
   }
   var view=manager.Snapshot("a").Requests.First();var command=new AnswerCommand(Guid.NewGuid().ToString("D"),view.Definition.Key,view.Revision,new(){["q"]=["Yes"]});
   var adapter=new Adapter();var task=manager.Submit(command,adapter,default);await adapter.Started.Task;
   manager.ConfirmAnswers(command.SubmissionId,command.Answers);
   adapter.Finish.SetResult(seed%2==0?DispatchOutcome.RejectedBeforeWrite:DispatchOutcome.SentUnconfirmed);await task;
   check(manager.Status(command.SubmissionId)!.Delivery==DeliveryState.Accepted,"provider-receipt-wins-late-transport-result");
   manager.Observe("a","epoch",++sequence,false,[first]);
   check(manager.Snapshot("a").Requests.Single(r=>r.Definition.Key==first.Key).Life==LifeState.Resolved,"replayed-open-cannot-reignite-answered-request");
   check(manager.Snapshot("a").Requests.Single(r=>r.Definition.Key==second.Key).Life==LifeState.Open&&manager.Snapshot("b").Requests.Single().Life==LifeState.Open,"answer-leaves-sibling-and-other-tab-open");
   var restored=new RequestCoordinator(journal);await restored.Submit(command,adapter,default);
   check(adapter.Writes==1,"restart-status-recovery-does-not-repeat-input");
  }
 }
}
