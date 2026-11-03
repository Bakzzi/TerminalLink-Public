using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
namespace TerminalLink;
internal static class CloseTerminal {
 internal static async Task Close(ConsoleTarget target,Func<bool> authorized,CancellationToken cancel){
  if(!authorized()||!target.Valid)throw new IOException("닫을 터미널이 변경되었거나 공유가 해제되었습니다.");
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
  foreach(string arg in new[]{"--close-tab",target.Pid.ToString(),target.Started.ToString(),target.Window.Handle.ToString(),target.Window.Pid.ToString()})start.ArgumentList.Add(arg);
  using var helper=Process.Start(start)??throw new IOException("탭 닫기를 준비하지 못했습니다.");
  _=helper.StandardError.ReadToEndAsync();
  try{
   string? ready=await helper.StandardOutput.ReadLineAsync(cancel).AsTask().WaitAsync(TimeSpan.FromSeconds(5),cancel);
   if(ready!="ready")throw new IOException("탭 닫기 준비 실패: "+ready);
   if(!authorized()||!target.Valid)throw new IOException("닫을 터미널이 변경되었거나 공유가 해제되었습니다.");
   await helper.StandardInput.WriteLineAsync("prepare");await helper.StandardInput.FlushAsync(cancel);
   string? located=await helper.StandardOutput.ReadLineAsync(cancel).AsTask().WaitAsync(TimeSpan.FromSeconds(10),cancel);
   if(located!="located")throw new IOException(located??"닫을 탭을 확인하지 못했습니다.");
   if(!authorized()||!target.Valid)throw new IOException("탭 공유 또는 연결 권한이 변경되었습니다.");
   await helper.StandardInput.WriteLineAsync("confirm");await helper.StandardInput.FlushAsync(cancel);
   string? result=await helper.StandardOutput.ReadLineAsync(cancel).AsTask().WaitAsync(TimeSpan.FromSeconds(10),cancel);
   if(result!="invoked")throw new IOException(result??"탭 닫기 응답이 없습니다.");
   for(int i=0;i<80;i++){cancel.ThrowIfCancellationRequested();if(!target.Valid)return;await Task.Delay(100,cancel);}
   throw new IOException("닫기 요청을 보냈지만 탭이 아직 실행 중입니다. PC의 확인창을 확인하세요.");
  }catch(TimeoutException){throw new IOException("탭 닫기 응답이 지연되었습니다. 목록을 확인하세요.");}
  finally{try{helper.StandardInput.Close();if(!helper.HasExited){await Task.WhenAny(helper.WaitForExitAsync(),Task.Delay(1500));if(!helper.HasExited)helper.Kill();}}catch{}}
 }
 internal static void Agent(int pid,long started,long window,int owner){
  using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);
  using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
  try{
   var w=TerminalWindows.Read(true).FirstOrDefault(w=>w.Handle==window&&w.Pid==owner)??throw new IOException("창이 변경되었습니다.");
   var target=new ConsoleTarget(w,pid,started,"");
   bool valid()=>target.Valid&&ConsoleNative.Targets(TerminalWindows.Read().Where(x=>x.Pid==owner).ToList()).Any(t=>t.Id==target.Id);
   if(!valid())throw new IOException("대상 탭이 변경되었습니다.");
   output.WriteLine("ready");if(input.ReadLine()!="prepare"||!valid())return;
   using var locator=new TerminalTabLocator();var located=locator.Find(target,true);
   var close=located.tab.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"CloseButton"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)));
   if(close==null||!close.Current.IsEnabled||!close.TryGetCurrentPattern(InvokePattern.Pattern,out var pattern)||!valid())throw new IOException("탭 닫기 버튼을 확인하지 못했습니다.");
   output.WriteLine("located");if(input.ReadLine()!="confirm"||!valid())return;
   ((InvokePattern)pattern).Invoke();output.WriteLine("invoked");
  }catch(Exception e){try{output.WriteLine(e is IOException?e.Message:"탭 닫기 실패: "+e.Message);}catch{}}
 }
}
