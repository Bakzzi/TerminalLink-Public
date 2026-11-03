namespace TerminalLink;

internal static class StateScenarioChecks {
 internal static void Run(Action<bool,string> check) {
  for(int seed=0;seed<32;seed++) {
   var random=new Random(seed);var frame=new ConsoleFrame("synthetic","",80,25,0,0,Array.Empty<int>());
   var tabs=Enumerable.Range(0,8).Select(i=>new DesktopTab(new(new(0,0,"synthetic","WindowsTerminal"),i+1,i+1,"Tab "+i))).ToArray();
   for(int i=0;i<tabs.Length;i++)tabs[i].Accept(frame,new("thread-"+i,[new("assistant","final","Completed answer "+i,1)],false));
   var rows=tabs.Select(t=>t.Messages[0]).ToArray();
   for(int n=0;n<24;n++) {
    int index=random.Next(tabs.Length);var tab=tabs[index];string id=tab.Original!.id;
    tab.Draft="draft "+index;tab.Accept(frame,null);
    check(!tab.Ready&&ReferenceEquals(tab.Messages[0],rows[index])&&tab.Draft=="draft "+index,"transient-read-keeps-completed-answer-and-draft");
    tab.ConfirmSent(id,1,"same words",[]);
    var next=tab.Original with{messages=tab.Original.messages.Append(new("user","","same words",tab.Original.messages.Length+1)).ToArray()};
    tab.Accept(frame,next);tab.Accept(frame,next with{});
    check(tab.Messages.Count==next.messages.Length&&tab.Messages.All(m=>m.Sequence>0),"replayed-records-consume-one-confirmed-send-only");
    check(tabs.Select((t,i)=>ReferenceEquals(t.Messages[0],rows[i])).All(v=>v),"tab-switch-order-preserves-all-completed-row-instances");
   }
  }
 }
}
