using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
namespace TerminalLink;
internal static class NewTerminal {
 static readonly SemaphoreSlim gate=new(1,1);
 internal static async Task<ConsoleTarget> Open(ConsoleTarget source,Func<List<TerminalWindow>> allowed,CancellationToken cancel){
  await gate.WaitAsync(cancel);Process? helper=null;
  try{
   bool Authorized()=>source.Valid&&allowed().Any(w=>w.Pid==source.Window.Pid&&w.Handle==source.Window.Handle);
   if(!Authorized())throw new IOException("공유 중인 터미널을 먼저 선택하세요.");
   if(!source.Window.Process.Equals("WindowsTerminal",StringComparison.OrdinalIgnoreCase))throw new IOException("새 탭 추가는 Windows Terminal에서 사용할 수 있습니다.");
   var before=ConsoleNative.Targets(allowed()).Select(t=>t.Id).ToHashSet();
   var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
   foreach(string arg in new[]{"--new-tab",source.Pid.ToString(),source.Started.ToString(),source.Window.Handle.ToString(),source.Window.Pid.ToString()})start.ArgumentList.Add(arg);
   helper=Process.Start(start)??throw new IOException("탭 추가를 준비하지 못했습니다.");_=helper.StandardError.ReadToEndAsync();
   string? ready=await helper.StandardOutput.ReadLineAsync(cancel).AsTask().WaitAsync(TimeSpan.FromSeconds(10),cancel);
   if(ready!="located"||!Authorized())throw new IOException(ready??"탭 추가 대상을 확인하지 못했습니다.");
   await helper.StandardInput.WriteLineAsync("confirm");await helper.StandardInput.FlushAsync(cancel);
   string? result=await helper.StandardOutput.ReadLineAsync(cancel).AsTask().WaitAsync(TimeSpan.FromSeconds(5),cancel);
   if(result!="invoked")throw new IOException(result??"탭 추가 응답이 없습니다.");
   for(int i=0;i<80;i++){
    var added=ConsoleNative.Targets(allowed()).Where(t=>!before.Contains(t.Id)&&t.Window.Pid==source.Window.Pid).ToArray();
    if(added.Length==1)return added[0];
    if(added.Length>1)throw new IOException("여러 탭이 동시에 추가되었습니다. 자동 갱신된 목록에서 선택하세요.");
    await Task.Delay(100,cancel);
   }
   throw new IOException("추가한 탭을 아직 읽을 수 없습니다. 목록을 확인하세요.");
  }catch(TimeoutException){throw new IOException("새 탭 확인이 지연되었습니다. 목록을 확인하세요.");}
  finally{
   if(helper!=null){try{helper.StandardInput.Close();if(!helper.HasExited){await Task.WhenAny(helper.WaitForExitAsync(),Task.Delay(1500));if(!helper.HasExited)helper.Kill();}}catch{}helper.Dispose();}
   gate.Release();
  }
 }
 internal static void Agent(int pid,long started,long window,int owner){
  using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);
  using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
  try{
   var w=TerminalWindows.Read(true).FirstOrDefault(w=>w.Handle==window&&w.Pid==owner)??throw new IOException("창이 변경되었습니다.");
   var target=new ConsoleTarget(w,pid,started,"");
   using var locator=new TerminalTabLocator();var located=locator.Find(target,false);
   var root=AutomationElement.FromHandle(new IntPtr(located.window.Handle));
   var button=root.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"NewTabButton"),new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.SplitButton))));
   if(button==null||!button.Current.IsEnabled||!button.TryGetCurrentPattern(InvokePattern.Pattern,out var pattern))throw new IOException("이 창의 새 탭 버튼을 확인하지 못했습니다.");
   output.WriteLine("located");if(input.ReadLine()!="confirm"||!target.Valid||!Native.Valid(located.window))return;
   ((InvokePattern)pattern).Invoke();output.WriteLine("invoked");
  }catch(Exception e){try{output.WriteLine(e is IOException?e.Message:"탭 추가 실패: "+e.Message);}catch{}}
 }
}
