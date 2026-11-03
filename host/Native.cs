using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace TerminalLink;

internal record TerminalWindow(long Handle, int Pid, string Title, string Process,long Started=0) {
 public override string ToString() => $"{Title}  ·  {Process} ({Pid})";
}
internal static class Native {
 [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; public int Width=>Right-Left; public int Height=>Bottom-Top; }
 [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; }
 [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint Type; public InputUnion Data; }
 [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT Key; [FieldOffset(0)] public MOUSEINPUT Mouse; }
 [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort Vk,Scan; public uint Flags,Time; public UIntPtr Extra; }
 [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int Dx,Dy; public uint MouseData,Flags,Time; public UIntPtr Extra; }
 delegate bool EnumProc(IntPtr h,IntPtr p);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb,IntPtr p);
 [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr h);
 [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h,StringBuilder b,int n);
 [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
 [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr h,out RECT r);
 [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h,int attr,out RECT r,int size);
 [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,IntPtr pid);
 [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a,uint b,bool attach);
 [DllImport("user32.dll")] static extern uint SendInput(uint count,INPUT[] inputs,int size);
 [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h,int command);
internal static void Restore(TerminalWindow w){if(!Valid(w)||!DesktopAvailable())throw new IOException("창이 닫혔거나 PC가 잠겨 있습니다.");ShowWindowAsync(new(w.Handle),9);}
[DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
 [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h,uint flag);
 [DllImport("user32.dll")] static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
 [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
 internal static bool DesktopAvailable() {
  var d=OpenInputDesktop(0,false,0x0100); if(d==IntPtr.Zero)return false; CloseDesktop(d);return true;
 }
 internal static List<TerminalWindow> Windows() {
  var list=new List<TerminalWindow>();
  EnumWindows((h,_)=>{
   if(!IsWindowVisible(h)) return true;
   var cls=new StringBuilder(256);GetClassName(h,cls,256);
   if(cls.ToString()!="ConsoleWindowClass" && !cls.ToString().StartsWith("CASCADIA",StringComparison.OrdinalIgnoreCase)) return true;
   GetWindowThreadProcessId(h,out var pid);
   try {
    using var proc=System.Diagnostics.Process.GetProcessById((int)pid);
    if(!TerminalWindows.SameUser(proc))return true;
    var title=new StringBuilder(1024);GetWindowText(h,title,1024);
    list.Add(new(h.ToInt64(),(int)pid,title.ToString(),proc.ProcessName,proc.StartTime.ToUniversalTime().Ticks));
   } catch { }
   return true;
  },IntPtr.Zero);
  return list;
 }
 internal static bool Valid(TerminalWindow w) {
  var h=new IntPtr(w.Handle);GetWindowThreadProcessId(h,out var pid);
  if(!IsWindow(h)||!IsWindowVisible(h)||pid!=w.Pid)return false;
  try{using var p=Process.GetProcessById(w.Pid);return (w.Started==0||p.StartTime.ToUniversalTime().Ticks==w.Started)&&TerminalWindows.SameUser(p);}catch{return false;}
 }
 internal static RECT Bounds(IntPtr h) {
  if(DwmGetWindowAttribute(h,9,out var r,Marshal.SizeOf<RECT>())!=0) GetWindowRect(h,out r);
  return r;
 }
 internal static void Focus(TerminalWindow w) {
  if(!Valid(w)||IsIconic(new(w.Handle))||!DesktopAvailable())throw new IOException("창을 닫았거나 최소화/잠금 상태입니다.");
  var h=new IntPtr(w.Handle);
  if(GetForegroundWindow()==h) return;
  uint current=GetCurrentThreadId(),front=GetWindowThreadProcessId(GetForegroundWindow(),IntPtr.Zero);
  bool attached=front!=0&&current!=front&&AttachThreadInput(current,front,true);
  try { SetForegroundWindow(h); } finally {if(attached)AttachThreadInput(current,front,false);}
  if(GetForegroundWindow()!=h)throw new IOException("PC에서 공유할 터미널을 한 번 선택해 주세요.");
 }
 static INPUT Key(ushort vk,bool up=false,ushort scan=0,bool unicode=false)=>new(){Type=1,Data=new(){Key=new(){Vk=vk,Scan=scan,Flags=(up?2u:0u)|(unicode?4u:0u)|(!unicode&&vk>=33&&vk<=46?1u:0u)}}};
 static void Send(List<INPUT> inputs) {
  if(SendInput((uint)inputs.Count,inputs.ToArray(),Marshal.SizeOf<INPUT>())!=inputs.Count)throw new IOException("입력을 전달하지 못했습니다. PC와 터미널의 권한을 확인하세요.");
 }
 internal static void Text(TerminalWindow w,string text) {
  if(text.Length>4096)throw new IOException("한 번에 4096자까지 입력할 수 있습니다.");
  Focus(w);var inputs=new List<INPUT>();
  foreach(char c in text) {if(c=='\r')continue; if(c=='\n'){inputs.Add(Key(13));inputs.Add(Key(13,true));}else{inputs.Add(Key(0,false,c,true));inputs.Add(Key(0,true,c,true));}}
  Send(inputs);
 }
 internal static void Press(TerminalWindow w,string key,bool ctrl,bool alt,bool shift) {
  ushort vk=key switch { "ENTER"=>13,"TAB"=>9,"ESC"=>27,"BACKSPACE"=>8,"DELETE"=>46,"UP"=>38,"DOWN"=>40,"LEFT"=>37,"RIGHT"=>39,"HOME"=>36,"END"=>35,"PAGEUP"=>33,"PAGEDOWN"=>34,"SPACE"=>32,"INSERT"=>45,_ when key.StartsWith("F")&&int.TryParse(key[1..],out var f)&&f>=1&&f<=12=>(ushort)(111+f),_ when key.Length==1&&(char.IsAsciiLetterOrDigit(key[0]))=>(ushort)char.ToUpperInvariant(key[0]),_=>0};
  if(vk==0)return; if((alt&&(key=="TAB"||key=="ESC"))||(ctrl&&shift&&key=="ESC"))throw new IOException("시스템 전환 단축키는 지원하지 않습니다."); Focus(w);var a=new List<INPUT>();
  if(ctrl)a.Add(Key(17));if(alt)a.Add(Key(18));if(shift)a.Add(Key(16));
  a.Add(Key(vk));a.Add(Key(vk,true));
  if(shift)a.Add(Key(16,true));if(alt)a.Add(Key(18,true));if(ctrl)a.Add(Key(17,true));
  Send(a);
 }
 internal static void Pointer(TerminalWindow w,double x,double y,string action,int delta) {
  if(!double.IsFinite(x)||!double.IsFinite(y)||x<0||x>1||y<0||y>1)throw new IOException("잘못된 좌표");
  Focus(w);var h=new IntPtr(w.Handle);var r=Bounds(h);
  var p=new POINT{X=r.Left+(int)(x*(r.Width-1)),Y=r.Top+(int)(y*(r.Height-1))};
  if(GetAncestor(WindowFromPoint(p),2)!=h)throw new IOException("해당 위치가 다른 창에 가려져 있습니다.");
  SetCursorPos(p.X,p.Y);
  var a=new List<INPUT>();
  if(action=="wheel")a.Add(new(){Type=0,Data=new(){Mouse=new(){Flags=0x0800,MouseData=unchecked((uint)Math.Clamp(delta,-1200,1200))}}});
  else {uint down=action=="right"?8u:2u,up=action=="right"?16u:4u; a.Add(new(){Data=new(){Mouse=new(){Flags=down}}});a.Add(new(){Data=new(){Mouse=new(){Flags=up}}});}
  Send(a);
 }

 internal static void Drag(TerminalWindow w,double sx,double sy,double x,double y){
  var values=new[]{sx,sy,x,y};
  if(values.Any(v=>!double.IsFinite(v)||v<0||v>1))throw new IOException("잘못된 좌표");
  Focus(w);var h=new IntPtr(w.Handle);var r=Bounds(h);
  POINT At(double nx,double ny)=>new(){X=r.Left+(int)(nx*(r.Width-1)),Y=r.Top+(int)(ny*(r.Height-1))};
  var start=At(sx,sy);if(GetAncestor(WindowFromPoint(start),2)!=h)throw new IOException("다른 창에 가려진 위치입니다.");
  SetCursorPos(start.X,start.Y);Send(new(){new(){Data=new(){Mouse=new(){Flags=2}}}});
  try{
   for(int i=1;i<=12;i++){if(GetForegroundWindow()!=h)break;var p=At(sx+(x-sx)*i/12,sy+(y-sy)*i/12);SetCursorPos(p.X,p.Y);Thread.Sleep(5);}
  }finally{Send(new(){new(){Data=new(){Mouse=new(){Flags=4}}}});}
 }
}