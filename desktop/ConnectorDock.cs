using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TerminalLink;

// Host the already running connector. No server, Identity, device keys or phone socket
// are created here. The independent guard restores this same HWND on pipe closure.
internal sealed record ConnectorWindow(long Handle,int Pid,long Started,string Path,bool Fixture=false);
internal sealed record ConnectorDockState(ConnectorWindow Target,int Owner,long OwnerStarted,long Style,long ExStyle,ConnectorNative.Placement Placement);

internal static class ConnectorNative {
 internal const string Marker="TerminalLinkDesktop.ConnectorDock.v1";
 const long AppWindow=0x40000,ToolWindow=0x80;
 internal static string Installed=>System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","TerminalLink","TerminalLink-PC.exe");
 [StructLayout(LayoutKind.Sequential)] internal struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)] internal struct Rectangle {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)] internal struct Placement {public int Length,Flags,Show;public Point Min,Max;public Rectangle Normal;}
 [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetStyle(IntPtr h,int index);
 [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)] static extern IntPtr SetStyle(IntPtr h,int index,IntPtr value);
 [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr h);
 [DllImport("user32.dll")] static extern bool GetWindowPlacement(IntPtr h,ref Placement placement);
 [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h,in Placement placement);
 [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int width,int height,uint flags);
 [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr h,int command);
 [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr h);
 [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr h,uint command);
 [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr h);
 [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
 [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
 [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
 [DllImport("user32.dll")] static extern int GetWindowRgn(IntPtr h,IntPtr region);
 [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr h,IntPtr region,bool redraw);
 internal delegate void WinEvent(IntPtr hook,uint kind,IntPtr h,int obj,int child,uint thread,uint time);
 [DllImport("user32.dll")] internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
 [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(IntPtr hook);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetProp(IntPtr h,string name,IntPtr value);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetProp(IntPtr h,string name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr h,string name);
 delegate bool EnumWindowProc(IntPtr window,IntPtr param);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr param);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h,StringBuilder text,int max);
 internal static readonly JsonSerializerOptions Json=new(){IncludeFields=true};

 internal static bool Same(ConnectorWindow target){
  try{Native.GetWindowThreadProcessId(new(target.Handle),out var pid);using var p=Process.GetProcessById(target.Pid);
   return Native.IsWindow(new(target.Handle))&&pid==target.Pid&&p.StartTime.ToUniversalTime().Ticks==target.Started&&TerminalWindows.SameUser(p)&&p.SessionId==Process.GetCurrentProcess().SessionId&&string.Equals(p.MainModule?.FileName,target.Path,StringComparison.OrdinalIgnoreCase);
  }catch{return false;}
 }
 internal static void Validate(ConnectorWindow target){
  if(!Same(target))throw new IOException("연결 프로그램이 종료되거나 바뀌었습니다. 다시 확인해 주세요.");
  var name=new StringBuilder(512);GetWindowText(new(target.Handle),name,name.Capacity);
  bool allowed=target.Fixture?string.Equals(target.Path,Environment.ProcessPath,StringComparison.OrdinalIgnoreCase)&&name.ToString()=="TerminalLink isolated connector fixture":string.Equals(target.Path,Installed,StringComparison.OrdinalIgnoreCase)&&name.ToString()=="TerminalLink · 연결 프로그램";
  if(!allowed)throw new IOException("설치된 TerminalLink 연결 프로그램 창만 사용할 수 있습니다.");
 }
 internal static ConnectorWindow? Find()=>Find(false);
 internal static ConnectorWindow? FindFixture()=>Find(true);
 static ConnectorWindow? Find(bool fixture){
  var found=new List<ConnectorWindow>();
  EnumWindows((h,_)=>{
   var title=new StringBuilder(128);GetWindowText(h,title,title.Capacity);if(title.ToString()!=(fixture?"TerminalLink isolated connector fixture":"TerminalLink · 연결 프로그램"))return true;
   try{Native.GetWindowThreadProcessId(h,out var pid);using var p=Process.GetProcessById((int)pid);var w=new ConnectorWindow(h.ToInt64(),p.Id,p.StartTime.ToUniversalTime().Ticks,p.MainModule?.FileName??"",fixture);Validate(w);found.Add(w);}catch{}return true;
  },IntPtr.Zero);
  if(found.Count>1)throw new IOException("연결 프로그램 창이 여러 개 열려 있습니다. 사용할 창 하나만 남긴 뒤 다시 확인해 주세요.");
  return found.SingleOrDefault();
 }
 internal static bool ProcessRunning()=>Process.GetProcessesByName("TerminalLink-PC").Any(p=>{using(p){try{return TerminalWindows.SameUser(p)&&string.Equals(p.MainModule?.FileName,Installed,StringComparison.OrdinalIgnoreCase)&&!p.HasExited;}catch{return false;}}});
 internal static ConnectorDockState Capture(ConnectorWindow target){
  Validate(target);var h=new IntPtr(target.Handle);
  if(GetParent(h)!=IntPtr.Zero||GetProp(h,Marker)!=IntPtr.Zero||!IsWindowEnabled(h))throw new IOException("연결 프로그램의 열린 팝업을 닫은 뒤 다시 확인해 주세요.");
  var region=CreateRectRgn(0,0,0,0);try{if(GetWindowRgn(h,region)!=0)throw new IOException("연결 창의 사용자 지정 모양을 확인할 수 없어 그대로 두었습니다.");}finally{DeleteObject(region);}
  var placement=new Placement{Length=Marshal.SizeOf<Placement>()};if(!GetWindowPlacement(h,ref placement))throw new IOException("연결 창의 원래 위치를 확인하지 못했습니다.");
  using var self=Process.GetCurrentProcess();return new(target,self.Id,self.StartTime.ToUniversalTime().Ticks,GetStyle(h,-16).ToInt64(),GetStyle(h,-20).ToInt64(),placement);
 }
 static void Style(IntPtr h,int index,long style){Marshal.SetLastPInvokeError(0);if(SetStyle(h,index,new(style))==IntPtr.Zero&&Marshal.GetLastPInvokeError()!=0)throw new Win32Exception(Marshal.GetLastPInvokeError());}
 internal static void Attach(ConnectorDockState state,IntPtr parent){
  Validate(state.Target);var h=new IntPtr(state.Target.Handle);Native.GetWindowThreadProcessId(parent,out var owner);
  using var p=Process.GetProcessById(state.Owner);
  if(owner!=state.Owner||p.StartTime.ToUniversalTime().Ticks!=state.OwnerStarted||GetParent(h)!=IntPtr.Zero||GetProp(h,Marker)!=IntPtr.Zero)throw new IOException("화면 대상이 변경되어 연결 화면을 그대로 두었습니다.");
  if(!SetProp(h,Marker,new(state.Owner)))throw new Win32Exception(Marshal.GetLastPInvokeError());
  try{
   ShowWindowAsync(h,0);
   // Keep the original top-level HWND independent. SetParent to another process
   // destroys the connector HWND if that process crashes (covered by our fixture).
   // Clip/position its client UI over the viewport instead, with no native owner.
   Style(h,-16,state.Style&~(0x20000000L|0x1000000L));
   Style(h,-20,(state.ExStyle|ToolWindow)&~AppWindow);
   SetWindowPos(h,IntPtr.Zero,0,0,0,0,0x4037); // async, no activate, frame changed
  }catch{Restore(state);throw;}
 }
 internal static void Restore(ConnectorDockState state,bool background=false){
  if(!Same(state.Target))return;var h=new IntPtr(state.Target.Handle);if(GetProp(h,Marker)!=new IntPtr(state.Owner))return;
  // No WM_CLOSE, process termination, or changes to the connector's server state.
  ShowWindowAsync(h,0);SetWindowRgn(h,IntPtr.Zero,true);Style(h,-16,state.Style);Style(h,-20,state.ExStyle);
  var placement=state.Placement;placement.Flags|=4;if(background)placement.Show=0; // keep a normal shutdown in the background
  if(!SetWindowPlacement(h,in placement))throw new IOException("연결 창의 위치를 복구하지 못했습니다.");
  SetWindowPos(h,IntPtr.Zero,0,0,0,0,0x4037);RemoveProp(h,Marker);
 }
 internal static void Clip(IntPtr h,int left,int top,int width,int height){
  var region=CreateRectRgn(left,top,left+Math.Max(1,width),top+Math.Max(1,height));
  if(SetWindowRgn(h,region,true)==0)DeleteObject(region); // Windows owns a successful region.
 }
}

internal sealed class ConnectorDockLease {
 readonly Process guard;readonly ConnectorDockState state;bool detached;Task? detaching;
 internal ConnectorWindow Target=>state.Target;
 internal bool Healthy=>!detached&&!guard.HasExited&&ConnectorNative.Same(Target);
 static string Journal=>Path.Combine(DesktopPaths.Root,"connector-window.json");
 ConnectorDockLease(Process guard,ConnectorDockState state){this.guard=guard;this.state=state;}
 internal static void RecoverAbandoned(){
  try{if(!File.Exists(Journal)||new FileInfo(Journal).Length>16384)return;var state=JsonSerializer.Deserialize<ConnectorDockState>(File.ReadAllText(Journal),ConnectorNative.Json);if(state==null)return;
   try{using var p=Process.GetProcessById(state.Owner);if(p.StartTime.ToUniversalTime().Ticks==state.OwnerStarted&&!p.HasExited)return;}catch{}
   ConnectorNative.Validate(state.Target);ConnectorNative.Restore(state);
  }catch{}
 }
 internal static async Task<ConnectorDockLease> Attach(ConnectorWindow target,IntPtr parent){
  var state=await Task.Run(()=>ConnectorNative.Capture(target));
  if(!target.Fixture){Directory.CreateDirectory(DesktopPaths.Root);File.WriteAllText(Journal,JsonSerializer.Serialize(state,ConnectorNative.Json));}
  var guard=Process.Start(DesktopProgram.Worker("--connector-guard"))??throw new IOException("연결 창 복구 장치를 시작하지 못했습니다.");_=guard.StandardError.ReadToEndAsync();var lease=new ConnectorDockLease(guard,state);
  try{
   await guard.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {state,parent=parent.ToInt64()},ConnectorNative.Json));await guard.StandardInput.FlushAsync();
   var result=await guard.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));if(result!="ok")throw new IOException(result??"연결 화면을 열지 못했습니다.");return lease;
  }catch{await lease.Detach();throw;}
 }
 internal Task Detach(bool background=false){if(detaching?.IsFaulted==true)detaching=null;return detaching??=DetachCore(background);}
 async Task DetachCore(bool background){
  if(detached)return;
  try{if(background){await guard.StandardInput.WriteLineAsync("background");await guard.StandardInput.FlushAsync();}guard.StandardInput.Close();await guard.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(4));}catch(TimeoutException){}catch(InvalidOperationException){}
  await Task.Run(()=>ConnectorNative.Restore(state,background));
  if(ConnectorNative.Same(Target)&&ConnectorNative.GetParent(new(Target.Handle))!=IntPtr.Zero)throw new IOException("연결 창 복구를 기다리고 있습니다. 잠시 뒤 앱을 닫아 주세요.");
  detached=true;guard.Dispose();
  if(!Target.Fixture)try{File.Delete(Journal);}catch(IOException){}
 }
}

internal static class ConnectorGuard {
 internal static void Run(){
  ConnectorDockState? state=null;bool background=false;
  using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
  try{
   var line=input.ReadLine();if(line==null||line.Length>16384)return;
   using var doc=JsonDocument.Parse(line);state=doc.RootElement.GetProperty("state").Deserialize<ConnectorDockState>(ConnectorNative.Json)??throw new IOException("연결 창 정보가 없습니다.");
   ConnectorNative.Attach(state,new(doc.RootElement.GetProperty("parent").GetInt64()));output.WriteLine("ok");
   string? command;while((command=input.ReadLine())!=null){if(command=="background")background=true;} // inherited private pipe
  }catch(Exception e){output.WriteLine(e.Message.Replace('\n',' '));}
  finally{if(state!=null)try{ConnectorNative.Restore(state,background);}catch{}}
 }
}

internal sealed class ConnectorHost : System.Windows.Controls.Border,IDisposable {
 internal IntPtr Container=>owner==null?IntPtr.Zero:new WindowInteropHelper(owner).Handle;
 internal ConnectorWindow? Target;
 internal double Horizontal,Vertical;
 internal event Action<double,double,double,double>? ExtentChanged;
 Window? owner;IntPtr hook;readonly ConnectorNative.WinEvent foreground;bool queued,disposed;string last="";
 internal ConnectorHost(){
  foreground=(_,_,_,_,_,_,_)=>QueueFit();
  Loaded+=(_,_)=>{
   if(owner!=null)return;owner=Window.GetWindow(this);if(owner==null)return;
   owner.LocationChanged+=Changed;owner.SizeChanged+=Sized;owner.StateChanged+=Changed;owner.IsVisibleChanged+=VisibilityChanged;
   hook=ConnectorNative.SetWinEventHook(3,3,IntPtr.Zero,foreground,0,0,0);Fit();
  };
  SizeChanged+=Sized;IsVisibleChanged+=VisibilityChanged;
 }
 void Changed(object? sender,EventArgs e)=>Fit();
 void Sized(object sender,SizeChangedEventArgs e)=>Fit();
 void VisibilityChanged(object sender,DependencyPropertyChangedEventArgs e)=>Fit();
 void QueueFit(){if(queued||disposed)return;queued=true;Dispatcher.BeginInvoke(()=>{queued=false;Fit(true);},DispatcherPriority.Input);}
 internal void Fit(){
  Fit(false);
 }
 void Fit(bool reorder){
  if(disposed||Target==null||owner==null||Container==IntPtr.Zero||!ConnectorNative.Same(Target))return;
  var h=new IntPtr(Target.Handle);
  if(ConnectorNative.GetProp(h,ConnectorNative.Marker)!=new IntPtr(Environment.ProcessId))return;
  if(!IsVisible||!owner.IsVisible||owner.WindowState==WindowState.Minimized){ConnectorNative.ShowWindowAsync(h,0);last="";return;}
  double scale=System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
  // Crop only the connector's own caption and resize edges. Its real Body and all
  // controls/dialog handlers remain in the original connector process.
  const int edge=7,caption=38;
  double width=Math.Max(760,ActualWidth+edge*2),height=Math.Max(740,ActualHeight+caption+edge);
  double maxX=Math.Max(0,width-edge*2-ActualWidth),maxY=Math.Max(0,height-caption-edge-ActualHeight);
  Horizontal=Math.Clamp(Horizontal,0,maxX);Vertical=Math.Clamp(Vertical,0,maxY);
  var origin=PointToScreen(new(0,0));int left=(int)Math.Round(edge*scale),top=(int)Math.Round(caption*scale);
  int x=(int)Math.Round(origin.X)-left,y=(int)Math.Round(origin.Y)-top,w=(int)Math.Ceiling(width*scale),t=(int)Math.Ceiling(height*scale);
  left+=(int)Math.Round(Horizontal*scale);top+=(int)Math.Round(Vertical*scale);x-=(int)Math.Round(Horizontal*scale);y-=(int)Math.Round(Vertical*scale);
  string geometry=$"{x},{y},{w},{t},{ActualWidth},{ActualHeight},{left},{top}";
  if(geometry!=last||reorder){
   var foreground=ConnectorNative.GetForegroundWindow();bool ownDialog=ConnectorNative.GetAncestor(foreground,3)==h;
   var above=ConnectorNative.GetWindow(Container,3);bool keepOrder=above==h||ownDialog;
   ConnectorNative.Clip(h,left,top,(int)Math.Round(ActualWidth*scale),(int)Math.Round(ActualHeight*scale));
   ConnectorNative.SetWindowPos(h,keepOrder?IntPtr.Zero:above,x,y,w,t,(uint)(0x4050|(keepOrder?4:0)));last=geometry;
  }
  ExtentChanged?.Invoke(maxX,maxY,ActualWidth,ActualHeight);
 }
 public void Dispose(){
  if(disposed)return;disposed=true;if(hook!=IntPtr.Zero)ConnectorNative.UnhookWinEvent(hook);
  if(owner!=null){owner.LocationChanged-=Changed;owner.SizeChanged-=Sized;owner.StateChanged-=Changed;owner.IsVisibleChanged-=VisibilityChanged;}
 }
}
