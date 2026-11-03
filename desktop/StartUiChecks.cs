using System.Windows;
using System.Windows.Threading;

namespace TerminalLink;
public partial class DesktopWindow {
 internal async Task CheckStartUi(){
  var passed=new List<string>();void Check(bool ok,string name){if(!ok)throw new Exception(name);passed.Add("PASS "+name);}
  try{
   if(!demo)throw new InvalidOperationException("Synthetic UI only");
   var tab=new DesktopTab(new(new(0,0,"startup","WindowsTerminal"),42,42,"New workspace")){Frame=StartInputChecks.Welcome(),NewIdentity="native-fixture"};
   tabs.Add(tab);Tabs.SelectedItem=tab;await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);RenderState();UpdateLayout();
   Check(!tab.Ready&&tab.Original==null&&SendButton.IsEnabled&&Composer.IsEnabled,"a new TUI can send its first message without a fabricated conversation record");
   Check(EmptyText.Visibility==Visibility.Visible&&EmptyText.Text.Contains("첫 메시지")&&NoticeBar.Visibility!=Visibility.Visible,"the new-conversation state replaces the failed-history error");
   Check(!AttachButton.IsEnabled&&RequestButton.Visibility!=Visibility.Visible,"attachments and requests require the real conversation to exist");
   tab.Sending=busy=true;RenderState();Check(!Composer.IsEnabled&&!SendButton.IsEnabled,"first-message sending locks its editor and prevents duplicate submits");
   tab.Sending=busy=false;tab.SendError="Synthetic failure";Composer.Text="Preserved first draft";RenderState();
   Check(Composer.IsEnabled&&SendButton.IsEnabled&&Composer.Text=="Preserved first draft","failed first-message sending restores the editable draft");
   tab.SendError="";tab.ConfirmStart("Hi","new-thread");RenderState();UpdateLayout();
   Check(Messages.Items.Count==1&&((ChatRow)Messages.Items[0]).Text=="Hi"&&!SendButton.IsEnabled,"the completed first send is shown once while the original is connecting");
   tab.Accept(tab.Frame!,new("new-thread",new[]{new ConversationMessage("user","","Hi",1)},false));RenderState();
   Check(Messages.Items.Count==1&&SendButton.IsEnabled&&AttachButton.IsEnabled,"original arrival replaces the sent receipt and enables ordinary chat actions");
   tab.Accept(new("fixture","› Ask Codex to do anything\n? for shortcuts",80,2,2,0,Array.Empty<int>()),new("unavailable",Array.Empty<ConversationMessage>(),false,source:"unavailable",readState:"failed"));RenderState();
   Check(!SendButton.IsEnabled&&tab.NewIdentity.Length==0&&Messages.Items.Count==1,"a failed existing conversation cannot fall through to new-session sending");
  }catch(Exception e){passed.Add("FAIL "+e);Environment.ExitCode=1;}
  finally{File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"desktop-start-ui-checks.txt"),passed);Close();}
 }
}
