using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace TerminalLink;

internal sealed record WindowDisplay(TerminalWindow Window,int Owner,long Style,uint Color,byte Alpha,uint Flags,bool Taskbar,bool Synthetic=false);

// Keep WS_VISIBLE for the mobile connector; change opacity and shell presentation only.
internal static class WindowVisibility {
 const long Layered=0x80000,Transparent=0x20,ToolWindow=0x80,AppWindow=0x40000,NoActivate=0x8000000;
 const long Mask=Layered|Transparent|ToolWindow|AppWindow|NoActivate;
 const string Property="TerminalLinkDesktop.Hidden.v1";
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)] static extern IntPtr GetStyle(IntPtr hwnd,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetStyle(IntPtr hwnd,int index,IntPtr value);
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetLayeredWindowAttributes(IntPtr hwnd,uint color,byte alpha,uint flags);
 [DllImport("user32.dll",SetLastError=true)] static extern bool GetLayeredWindowAttributes(IntPtr hwnd,out uint color,out byte alpha,out uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetProp(IntPtr hwnd,string name,IntPtr value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hwnd,string name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr hwnd,string name);
 [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
 [DllImport("user32.dll",SetLastError=true)] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
 [ComImport,Guid("56FDF344-FD6D-11D0-958A-006097C9A090")] class TaskbarObject{}
 [ComImport,Guid("56FDF342-FD6D-11D0-958A-006097C9A090"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface ITaskbar {
  void HrInit();void AddTab(IntPtr hwnd);void DeleteTab(IntPtr hwnd);void ActivateTab(IntPtr hwnd);void SetActiveAlt(IntPtr hwnd);
 }
 static void Taskbar(IntPtr hwnd,bool show){var obj=(ITaskbar)new TaskbarObject();try{obj.HrInit();if(show)obj.AddTab(hwnd);else obj.DeleteTab(hwnd);}finally{Marshal.ReleaseComObject(obj);}}
 internal static bool Same(TerminalWindow w){try{Native.GetWindowThreadProcessId(new(w.Handle),out var pid);using var p=Process.GetProcessById(w.Pid);return Native.IsWindow(new(w.Handle))&&pid==w.Pid&&p.StartTime.ToUniversalTime().Ticks==w.Started&&TerminalWindows.SameUser(p);}catch{return false;}}
 internal static int Hidden(TerminalWindow w)=>GetProp(new(w.Handle),Property)!=IntPtr.Zero&&GetLayeredWindowAttributes(new(w.Handle),out _,out byte alpha,out _)&&alpha==0?1:0;
 static void Validate(TerminalWindow w,bool synthetic){
  if(!Same(w))throw new IOException("대상 창이 종료되거나 바뀌었습니다.");using var p=Process.GetProcessById(w.Pid);
  bool test=synthetic&&p.MainModule?.FileName==Environment.ProcessPath&&w.Title=="TerminalLink isolated visibility fixture";
  if(!test&&!p.ProcessName.Equals("WindowsTerminal",StringComparison.OrdinalIgnoreCase))throw new IOException("기존 Windows Terminal 창만 숨길 수 있습니다.");
 }
 internal static WindowDisplay Capture(TerminalWindow w,int owner,bool synthetic=false){
  Validate(w,synthetic);var h=new IntPtr(w.Handle);if(GetProp(h,Property)!=IntPtr.Zero)throw new IOException("이 창은 이미 다른 PC 앱이 숨기고 있습니다.");
  long style=GetStyle(h,-20).ToInt64();uint color=0,flags=2;byte alpha=255;
  if((style&Layered)!=0&&!GetLayeredWindowAttributes(h,out color,out alpha,out flags))throw new IOException("기존 투명도 설정을 확인할 수 없어 창을 그대로 두었습니다.");
  if(alpha==0)throw new IOException("이미 보이지 않는 창의 표시 상태는 변경하지 않습니다.");
  bool taskbar=(style&ToolWindow)==0&&((style&AppWindow)!=0||GetWindow(h,4)==IntPtr.Zero);
  return new(w,owner,style,color,alpha,flags,taskbar,synthetic);
 }
 static void Style(IntPtr h,long style){Marshal.SetLastPInvokeError(0);if(SetStyle(h,-20,new(style))==IntPtr.Zero&&Marshal.GetLastPInvokeError()!=0)throw new Win32Exception(Marshal.GetLastPInvokeError());}
 internal static void Hide(WindowDisplay state){
  Validate(state.Window,state.Synthetic);var h=new IntPtr(state.Window.Handle);
  if(GetProp(h,Property)!=IntPtr.Zero||GetStyle(h,-20).ToInt64()!=state.Style)throw new IOException("창의 표시 상태가 바뀌었습니다.");
  if(!SetProp(h,Property,new(state.Owner)))throw new Win32Exception(Marshal.GetLastPInvokeError());
  try{
   Style(h,(state.Style|Layered|Transparent|ToolWindow|NoActivate)&~AppWindow);
   if(!SetLayeredWindowAttributes(h,0,0,2))throw new Win32Exception(Marshal.GetLastPInvokeError());
   SetWindowPos(h,IntPtr.Zero,0,0,0,0,0x37);if(state.Taskbar)Taskbar(h,false);
   if(Hidden(state.Window)!=1||!Native.IsWindowVisible(h))throw new IOException("창 숨김을 확인하지 못했습니다.");
  }catch{Restore(state);throw;}
 }
 internal static void Restore(WindowDisplay state){
  if(!Same(state.Window))return;var h=new IntPtr(state.Window.Handle);if(GetProp(h,Property)!=new IntPtr(state.Owner))return;
  if(!SetLayeredWindowAttributes(h,state.Color,state.Alpha,state.Flags))throw new Win32Exception(Marshal.GetLastPInvokeError());
  Style(h,(GetStyle(h,-20).ToInt64()&~Mask)|(state.Style&Mask));SetWindowPos(h,IntPtr.Zero,0,0,0,0,0x37);
  if(state.Taskbar)Taskbar(h,true);RemoveProp(h,Property);
 }
 internal static void ShowForeground(TerminalWindow w){if(Same(w)){if(Native.IsIconic(new(w.Handle)))Native.Restore(w);SetForegroundWindow(new(w.Handle));}}
}

internal sealed class VisibilityLease : IDisposable {
 readonly Process guard;readonly SemaphoreSlim gate=new(1,1);readonly Dictionary<long,WindowDisplay> owned=new();
 readonly bool synthetic;bool disposed;
 static string Journal=>Path.Combine(DesktopPaths.Root,"hidden-windows.json");
 sealed record Recovery(int owner,long started,WindowDisplay[] windows);
 internal static void RecoverAbandoned(){
  try{if(!File.Exists(Journal)||new FileInfo(Journal).Length>65536)return;var r=JsonSerializer.Deserialize<Recovery>(File.ReadAllText(Journal));if(r==null)return;try{using var p=Process.GetProcessById(r.owner);if(p.StartTime.ToUniversalTime().Ticks==r.started&&!p.HasExited)return;}catch{}
   foreach(var state in r.windows)WindowVisibility.Restore(state);
  }catch{}
 }
 internal VisibilityLease(bool synthetic=false){this.synthetic=synthetic;guard=Process.Start(DesktopProgram.Worker("--visibility-guard"))??throw new IOException("창 복구 장치를 시작하지 못했습니다.");_=guard.StandardError.ReadToEndAsync();}
 void Save(){if(synthetic)return;Directory.CreateDirectory(DesktopPaths.Root);using var self=Process.GetCurrentProcess();File.WriteAllText(Journal,JsonSerializer.Serialize(new Recovery(self.Id,self.StartTime.ToUniversalTime().Ticks,owned.Values.ToArray())));}
 internal async Task Set(TerminalWindow window,bool hide){
  await gate.WaitAsync();try{
   if(disposed)throw new ObjectDisposedException(nameof(VisibilityLease));if(hide&&owned.ContainsKey(window.Handle)||!hide&&!owned.ContainsKey(window.Handle))return;
   WindowDisplay state;if(hide){state=WindowVisibility.Capture(window,Environment.ProcessId,synthetic);owned[window.Handle]=state;Save();}else state=owned[window.Handle];
   await guard.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{state,hide}));await guard.StandardInput.FlushAsync();
   var line=await guard.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(4));if(line!="ok")throw new IOException(line??"창 복구 장치가 종료되었습니다.");
   if(!hide){owned.Remove(window.Handle);Save();}
  }catch{Restore();throw;}finally{gate.Release();}
 }
 void Restore(){foreach(var pair in owned.ToArray())try{WindowVisibility.Restore(pair.Value);owned.Remove(pair.Key);}catch{}try{Save();}catch{}}
 internal void CheckHealth(){if(!disposed&&guard.HasExited){Restore();throw new IOException("창 복구 장치가 종료되어 터미널을 다시 표시했습니다. 앱을 다시 열어 주세요.");}}
 internal void DisconnectForCheck(){if(!synthetic)throw new InvalidOperationException();guard.StandardInput.Close();long until=Environment.TickCount64+5000;while(!guard.HasExited&&Environment.TickCount64<until){System.Windows.Forms.Application.DoEvents();Thread.Sleep(10);}if(!guard.HasExited)throw new IOException("Guardian did not exit");}
 public void Dispose(){if(disposed)return;disposed=true;Restore();try{guard.StandardInput.Close();}catch{}guard.Dispose();}
}

internal static class VisibilityGuard {
 internal static void Run(){
  var owned=new Dictionary<long,WindowDisplay>();using var input=new StreamReader(Console.OpenStandardInput(),System.Text.Encoding.UTF8);using var output=new StreamWriter(Console.OpenStandardOutput(),new System.Text.UTF8Encoding(false)){AutoFlush=true};
  try{string? line;while((line=input.ReadLine())!=null){try{
   using var doc=JsonDocument.Parse(line);var state=doc.RootElement.GetProperty("state").Deserialize<WindowDisplay>()??throw new IOException("대상이 없습니다.");bool hide=doc.RootElement.GetProperty("hide").GetBoolean();
   if(hide){owned[state.Window.Handle]=state;WindowVisibility.Hide(state);}
   else if(owned.TryGetValue(state.Window.Handle,out var old)){WindowVisibility.Restore(old);owned.Remove(state.Window.Handle);}
   output.WriteLine("ok");
  }catch(Exception e){output.WriteLine(e.Message.Replace('\n',' '));}}}
  finally{foreach(var state in owned.Values)try{WindowVisibility.Restore(state);}catch{}}
 }
}
