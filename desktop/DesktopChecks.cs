using System.Diagnostics;
using System.Text.Json;
namespace TerminalLink;
internal static class DesktopChecks {
 sealed class FixtureWindow : System.Windows.Forms.Form {protected override bool ShowWithoutActivation=>true;}
 sealed class FakeConnection : IDisposable {internal int Disposals;public void Dispose()=>Disposals++;}
 internal static void Run(){
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"desktop-checks.txt"),"RUNNING");
  var passed=new List<string>();
  void Pump(Task task){long until=Environment.TickCount64+10000;while(!task.IsCompleted&&Environment.TickCount64<until){System.Windows.Forms.Application.DoEvents();Thread.Sleep(10);}task.WaitAsync(TimeSpan.FromSeconds(1)).GetAwaiter().GetResult();}
  void Check(bool ok,string name){if(!ok)throw new Exception(name);passed.Add(name);}
  try{
   string fixtureRoot=Path.Combine(Path.GetTempPath(),"TerminalLink-clear-fixture-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixtureRoot);
   try{
    string originalFile=Path.Combine(fixtureRoot,"original.txt");File.WriteAllText(originalFile,"fixture");var box=new SharedFiles(Path.Combine(fixtureRoot,"box"));
    var firstFile=box.Publish(originalFile);var laterFile=box.Publish(originalFile);var clearResult=FileBoxClear.Remove(box,new[]{firstFile});
    Check(clearResult.removed==1&&clearResult.failed==0&&box.Find(firstFile.id)==null&&box.Find(laterFile.id)!=null&&File.ReadAllText(originalFile)=="fixture","file box clear removes the selected copy and preserves later uploads and originals");
    var missing=FileBoxClear.Remove(box,new[]{firstFile});Check(missing.failed==1&&box.Find(laterFile.id)!=null,"file box clear reports changed entries without deleting others");
   }finally{Directory.Delete(fixtureRoot,true);}
   SentEchoChecks.Run(Check);
   StateScenarioChecks.Run(Check);
   AttachmentChecks.Run(Check);
   AttachmentTransferChecks.Run(Check);
   ReplyLatencyChecks.Run(Check);
   RequestChecks.Run(Check);
   InputEvidenceChecks.Run(Check);
   NativeMessageChecks.Run(Check);
   NativeComposerChecks.Run(Check);
   StartInputChecks.Run(Check);
   var target=new ConsoleTarget(new(0,0,"fixture","WindowsTerminal"),1,1,"fixture");var tab=new DesktopTab(target);
   var frame=new ConsoleFrame("fixture","",80,25,0,0,Array.Empty<int>());
   var messages=Enumerable.Range(1,2100).Select(i=>new ConversationMessage(i%2==1?"user":"assistant",i%2==1?"":"final","Message "+i,i)).ToArray();
   var first=new Conversation("fixture",messages,false,new[]{new QueuedQuestion("q1","Question",new[]{"One","Two"})});tab.Accept(frame,first);
   Check(tab.Messages.Count==2100,"all original messages retained without a history cap");var original=tab.Messages[0];var tail=tab.Messages[^1];
   tab.Accept(frame,first with{messages=messages.Append(new("assistant","commentary","progress",2101)).ToArray()});
   Check(tab.Messages.Count==2101&&tab.Messages[^1].Speaker=="Codex · 진행중"&&ReferenceEquals(original,tab.Messages[0])&&ReferenceEquals(tail,tab.Messages[2099]),"progress is appended immediately without rebuilding completed rows");
   tab.Accept(frame,new("unavailable",Array.Empty<ConversationMessage>(),false,source:"unavailable",readState:"failed"));
   Check(!tab.Ready&&tab.Messages.Count==2101&&tab.Original?.requests?.Length==1,"temporary read failure keeps content and request but blocks sending");
   tab.Accept(frame,new("different",new[]{new ConversationMessage("user","","New thread",1)},false));
   Check(tab.Messages.Count==1&&tab.Messages[0].Text=="New thread","confirmed thread switch separates history");
   var cleaned=new ChatRow(new("assistant","final","Done<oai-mem-citation><citation_entries>fixture</citation_entries></oai-mem-citation>",1));
   Check(cleaned.Text=="Done","display strips internal citation metadata");
   // Local records and SSH records share this parser. Publish each arrival before
   // any final reply exists, rather than feeding a pre-completed conversation.
   foreach(string source in new[]{"record","remote"}){
    var feed=new CodexFeed();var streamed=new DesktopTab(target);
    void Arrive(string role,string phase,string text){
     feed.AddRecord(JsonSerializer.SerializeToUtf8Bytes(new{type="response_item",payload=new{type="message",role,phase,content=new[]{new{type="output_text",text}}}}));feed.Publish();
     streamed.Accept(frame,feed.Snapshot! with{id="stream-fixture",source=source});
    }
    Arrive("user","","Question");Arrive("assistant","commentary","First progress");var progress=streamed.Messages.LastOrDefault();
    Check(streamed.Messages.Count==2&&progress?.Text=="First progress"&&progress.Speaker=="Codex · 진행중",source+" progress visible before a final reply exists");
    Arrive("assistant","analysis","Private reasoning");Arrive("tool","","Tool output");Arrive("assistant","commentary","Second progress");
    Check(streamed.Messages.Count==3&&streamed.Messages[^1].Text=="Second progress"&&ReferenceEquals(progress,streamed.Messages[1]),source+" later progress appends in order without exposing analysis or tools");
    Arrive("assistant","final_answer","Finished");
    Check(streamed.Messages.Count==4&&streamed.Messages[^1].Speaker=="Codex"&&ReferenceEquals(progress,streamed.Messages[1]),source+" final reply appends separately without relabeling progress");
   }
   string draft="",sent="";int enter=0;
   var snapshot=new Conversation("input-fixture",new[]{new ConversationMessage("user","","Working question",1)},false);
   ConsoleFrame Read()=>new("fixture","› "+(draft.Length>0?draft:"Ask Codex to do anything")+"\n? for shortcuts",80,25,draft.Length+2,0,Array.Empty<int>());
   var cmd=JsonSerializer.SerializeToElement(new{request="fixture",kind="message",thread=snapshot.id,text="다음 질문"});
   var result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,()=>snapshot,s=>{draft=s;sent=s;},(key,alt)=>{if(key=="ENTER")enter++;},cmd));
   Check(result.Bool("ok")&&sent=="다음 질문"&&enter==1,"normal message sends while prior turn is working, with one Enter");
   draft="existing draft";enter=0;sent="";
   result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,()=>snapshot,s=>sent=s,(key,alt)=>enter++,cmd));
   Check(!result.Bool("ok")&&sent==""&&enter==0,"existing terminal draft is never overwritten");
   draft="";result=JsonSerializer.SerializeToElement(NotificationInput.ExecuteCore(Read,()=>snapshot with{id="wrong"},s=>sent=s,(key,alt)=>enter++,cmd));
   Check(!result.Bool("ok")&&sent==""&&enter==0,"wrong conversation rejects input");
   using var form=new FixtureWindow{Text="TerminalLink isolated visibility fixture",ShowInTaskbar=false,StartPosition=System.Windows.Forms.FormStartPosition.Manual,Location=new System.Drawing.Point(-2500,-2500),Size=new System.Drawing.Size(200,100)};
   form.Show();using var self=Process.GetCurrentProcess();var window=new TerminalWindow(form.Handle.ToInt64(),self.Id,form.Text,self.ProcessName,self.StartTime.ToUniversalTime().Ticks);
   var bounds=form.Bounds;
   using(var lease=new VisibilityLease(true)){
    Pump(lease.Set(window,true));Check(WindowVisibility.Hidden(window)==1&&Native.IsWindowVisible(form.Handle)&&!Native.IsIconic(form.Handle),"separate guardian hides pixels without hiding or minimizing native window");
    Pump(lease.Set(window,false));Check(WindowVisibility.Hidden(window)==0&&form.Bounds==bounds&&Native.IsWindow(form.Handle),"restoring preserves exact handle and bounds");
    Pump(lease.Set(window,true));lease.DisconnectForCheck();Check(WindowVisibility.Hidden(window)==0&&form.Bounds==bounds,"guardian restores on lost parent pipe, including parent crash");
   }
   Check(!WindowVisibility.Same(window with{Started=window.Started+1}),"reused process identity is rejected");
   var binding=new RemoteCodexFeed.Binding(new(123,456,"192.0.2.1",20000,"192.0.2.2",22),"/tmp/fixture.jsonl","123:456","inode-fixture","remote:fixture");
   var wrapped=JsonSerializer.SerializeToElement(new[]{new{value=binding,Count=1}});
   Check(DesktopPaths.BindingRecords(wrapped).Single()==binding,"legacy cache wrappers preserve identity metadata only");
   Check(DesktopPaths.BindingRecords(JsonSerializer.SerializeToElement(new[]{binding,binding})).Length==0,"ambiguous cached endpoints are rejected");
   var a=new FakeConnection();var b=new FakeConnection();
   using(var connections=new RetainedConnections<FakeConnection>()){
    connections.Select("a",1,()=>a);using(var pending=connections.Acquire("a",()=>throw new Exception("duplicate connection"))){
     connections.Select("b",2,()=>b);using var read=connections.ReadSelected("b");
     Check(a.Disposals==0&&read?.Value==b&&pending.Value==a,"switching tabs retains the exact pending send connection and reads the new tab");
     connections.Select("a",1,()=>throw new Exception("stale selection"));using var stillB=connections.ReadSelected("b");
     Check(stillB?.Value==b,"late selection cannot replace a newer selected tab");
    }
    Check(a.Disposals==1&&b.Disposals==0,"old connection is released once only after send completion");
   }
   Check(b.Disposals==1,"selected connection is released when desktop session ends");
   string orderFile=Path.Combine(AppContext.BaseDirectory,"tab-order-check-"+Guid.NewGuid().ToString("N")+".json");
   try{
    var orderA=new DesktopTab(target);var orderB=new DesktopTab(target with{Pid=2,Started=2});var newcomer=new DesktopTab(target with{Pid=3,Started=3});
    new TabOrderStore(orderFile).Save(new[]{orderB,orderA});
    var reopened=new TabOrderStore(orderFile);orderB.Rename(orderB.Target with{Name="Renamed tab"});
    Check(reopened.Arrange(new[]{orderA,orderB,newcomer}).SequenceEqual(new[]{orderB,orderA,newcomer}),"saved order survives a new reader and title changes; new tabs append");
    var reusedPid=new DesktopTab(orderB.Target with{Started=999});
    Check(reopened.Arrange(new[]{reusedPid,orderA}).SequenceEqual(new[]{orderA,reusedPid}),"closed tab identities cannot apply their saved position to a reused PID");
    File.WriteAllText(orderFile,"invalid interrupted file");
    Check(new TabOrderStore(orderFile).Arrange(new[]{orderA,newcomer}).SequenceEqual(new[]{orderA,newcomer}),"unreadable order settings keep all live tabs in discovery order");
   }finally{if(File.Exists(orderFile))File.Delete(orderFile);}
   form.Close();
   UnreadChecks.Run(Check);
   File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-checks.txt"),passed.Select(p=>"PASS "+p));
  }catch(Exception e){File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"desktop-checks.txt"),string.Join("\n",passed)+"\nFAIL "+e);Environment.ExitCode=1;}
 }
}
