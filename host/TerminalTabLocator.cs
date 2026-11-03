using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
namespace TerminalLink;

// Used only for an explicitly requested tab action; discovery never changes titles or focus.
internal sealed class TerminalTabLocator : IDisposable {
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern uint GetConsoleTitle(StringBuilder text,uint count);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool SetConsoleTitle(string title);
 readonly string marker="TerminalLink-action-"+Guid.NewGuid().ToString("N");
 string original="";bool attached;
 string Title(){var b=new StringBuilder(2048);GetConsoleTitle(b,2048);return b.ToString();}
 internal (TerminalWindow window,AutomationElement tab) Find(ConsoleTarget target,bool closing){
  if(!target.Valid)throw new IOException("터미널이 변경되었습니다.");
  var all=TerminalWindows.Read(true).Where(w=>w.Pid==target.Window.Pid&&w.Process.Equals("WindowsTerminal",StringComparison.OrdinalIgnoreCase)).ToList();
  if(all.Count==0)throw new IOException("대상 터미널 창을 확인하지 못했습니다.");
  var condition=new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.TabItem);
  int tabs=all.Sum(w=>AutomationElement.FromHandle(new IntPtr(w.Handle)).FindAll(TreeScope.Descendants,condition).Count);
  if(closing&&tabs!=ConsoleNative.Targets(all).Count)throw new IOException("분할 창 구성을 확인할 수 없습니다. PC에서 닫아주세요.");
  if(!ConsoleNative.AttachConsole((uint)target.Pid))throw new IOException("콘솔에 접근하지 못했습니다.");
  attached=true;original=Title();
  if(!SetConsoleTitle(marker))throw new IOException("대상 탭을 확인하지 못했습니다.");
  for(int i=0;i<40;i++){
   if(!target.Valid)throw new IOException("터미널이 변경되었습니다.");
   if(Title()!=marker){original=Title();if(!SetConsoleTitle(marker))throw new IOException("대상 탭을 확인하지 못했습니다.");}
   var matches=new List<(TerminalWindow,AutomationElement)>();
   foreach(var window in all.Where(Native.Valid)){
    var root=AutomationElement.FromHandle(new IntPtr(window.Handle));
    foreach(AutomationElement tab in root.FindAll(TreeScope.Descendants,condition))if(tab.Current.Name==marker)matches.Add((window,tab));
   }
   if(matches.Count==1){Restore();return matches[0];}
   if(matches.Count>1)throw new IOException("대상 탭이 중복되었습니다.");
   Thread.Sleep(50);
  }
  throw new IOException("탭 제목이 고정되어 대상을 확인하지 못했습니다. PC에서 확인하세요.");
 }
 void Restore(){if(attached){try{if(Title()==marker)SetConsoleTitle(original);}finally{ConsoleNative.FreeConsole();attached=false;}}}
 public void Dispose(){Restore();}
}