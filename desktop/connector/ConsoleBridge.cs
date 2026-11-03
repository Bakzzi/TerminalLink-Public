using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
namespace TerminalLink;

internal record ConsoleTarget(TerminalWindow OriginalWindow,int Pid,long Started,string Name) {
 internal TerminalWindow Window=>TerminalWindows.Resolve(OriginalWindow);
 static readonly System.Collections.Concurrent.ConcurrentDictionary<(int,long),long> anchors=new();
 internal string Id=>$"{anchors.GetOrAdd((Pid,Started),OriginalWindow.Handle)}:{Pid}:{Started}";
 internal static void PruneAnchors(HashSet<int> live){if(anchors.Count>256)foreach(var key in anchors.Keys)if(!live.Contains(key.Item1))anchors.TryRemove(key,out _);}
 internal bool Valid=>Native.Valid(Window)&&Alive();
 bool Alive(){try{using var p=Process.GetProcessById(Pid);return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==Started;}catch{return false;}}
}
internal sealed record ConsoleFrame(string title,string text,int columns,int rows,int cursorX,int cursorY,int[] peers,string session="");
internal static class ConsoleNative {
 static string sessionIdentity="";
 [StructLayout(LayoutKind.Sequential)] internal struct Coord {internal short X,Y;internal Coord(short x,short y){X=x;Y=y;}}
 [StructLayout(LayoutKind.Sequential)] internal struct Rect {internal short L,T,R,B;}
 [StructLayout(LayoutKind.Sequential)] internal struct Info {internal Coord Size,Cursor;internal ushort Attributes;internal Rect Window;internal Coord Max;}
 [StructLayout(LayoutKind.Explicit,CharSet=CharSet.Unicode,Size=4)] internal struct Cell {[FieldOffset(0)]internal char Unicode;[FieldOffset(2)]internal ushort Attributes;}
 [StructLayout(LayoutKind.Explicit,CharSet=CharSet.Unicode,Size=20)] internal struct Input {
  [FieldOffset(0)]internal ushort Type;[FieldOffset(4)]internal int Down;[FieldOffset(8)]internal ushort Repeat;
  [FieldOffset(10)]internal ushort Key;[FieldOffset(12)]internal ushort Scan;[FieldOffset(14)]internal char Unicode;[FieldOffset(16)]internal uint Flags;
 }
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct ProcessEntry {
  internal uint Size,Usage,Pid;internal UIntPtr Heap;internal uint Module,Threads,Parent;internal int Priority;internal uint Flags;
  [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]internal string Name;
 }
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool AttachConsole(uint pid);
 [DllImport("kernel32.dll")]internal static extern bool FreeConsole();
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr CreateFile(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
 [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetConsoleScreenBufferInfo(IntPtr handle,out Info info);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool ReadConsoleOutputW(IntPtr h,[Out] Cell[] cells,Coord size,Coord origin,ref Rect region);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool WriteConsoleInputW(IntPtr h,Input[] inputs,uint count,out uint written);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNumberOfConsoleInputEvents(IntPtr handle,out uint count);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern uint GetConsoleTitle(StringBuilder text,uint size);
 [DllImport("kernel32.dll")]internal static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")]static extern uint GetConsoleProcessList([Out] uint[] pids,uint count);
 [DllImport("kernel32.dll")]static extern bool SetConsoleCtrlHandler(IntPtr handler,bool add);
 [DllImport("kernel32.dll")]static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint pid);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32FirstW(IntPtr snapshot,ref ProcessEntry entry);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool Process32NextW(IntPtr snapshot,ref ProcessEntry entry);
 [DllImport("user32.dll")]static extern uint MapVirtualKeyW(uint code,uint type);
 internal static List<ConsoleTarget> Targets(List<TerminalWindow> allowed) {
  var processes=new List<ProcessEntry>();var snapshot=CreateToolhelp32Snapshot(2,0);
  if(snapshot==new IntPtr(-1))return new();
  try{var entry=new ProcessEntry{Size=(uint)Marshal.SizeOf<ProcessEntry>(),Name=""};if(Process32FirstW(snapshot,ref entry))do{processes.Add(entry);}while(Process32NextW(snapshot,ref entry));}finally{CloseHandle(snapshot);}
  ConsoleTarget.PruneAnchors(processes.Select(p=>(int)p.Pid).ToHashSet());
  var result=new List<ConsoleTarget>();
  foreach(var window in TerminalWindows.Groups(allowed.Where(Native.Valid))){
   var candidates=window.Process.Equals("WindowsTerminal",StringComparison.OrdinalIgnoreCase)
    ? processes.Where(p=>p.Parent==window.Pid&&!p.Name.Equals("OpenConsole.exe",StringComparison.OrdinalIgnoreCase)&&!p.Name.Equals("conhost.exe",StringComparison.OrdinalIgnoreCase)&&!p.Name.Equals(Path.GetFileName(Environment.ProcessPath),StringComparison.OrdinalIgnoreCase))
    : processes.Where(p=>p.Pid==window.Pid);
   foreach(var entry in candidates)try{using var p=Process.GetProcessById((int)entry.Pid);if(!TerminalWindows.SameUser(p))continue;result.Add(new(window,p.Id,p.StartTime.ToUniversalTime().Ticks,$"{p.ProcessName} · {p.Id}"));}catch{}
  }
  return result;
 }
 internal static void TitleAgent(){
  // Keep IPC handles independent of the console being inspected.
  using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);
  using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
  string? line=input.ReadLine();if(line==null||line.Length>262144)return;
  using var request=JsonDocument.Parse(line);
  if(request.RootElement.ValueKind!=JsonValueKind.Array||request.RootElement.GetArrayLength()>256)return;
  var titles=new Dictionary<string,string>();
  foreach(var item in request.RootElement.EnumerateArray()){
   bool attached=false;
   try{
    string id=item.Str("id");if(id.Length>128)continue;
    using var process=Process.GetProcessById(item.GetProperty("pid").GetInt32());
    if(process.HasExited||process.StartTime.ToUniversalTime().Ticks!=item.GetProperty("started").GetInt64())continue;
    if(!AttachConsole((uint)process.Id))continue;attached=true;SetConsoleCtrlHandler(IntPtr.Zero,true);
    var title=new StringBuilder(1024);GetConsoleTitle(title,1024);
    string value=title.ToString().Trim();if(value.Length>0)titles[id]=value;
   }catch{}
   finally{if(attached)FreeConsole();}
  }
  output.WriteLine(JsonSerializer.Serialize(titles));
 }
 internal static string Decode(Cell[] cells,int width,int rows) {
  var text=new StringBuilder();
  for(int y=0;y<rows;y++){
   var line=new StringBuilder();
   for(int x=0;x<width;x++){var c=cells[y*width+x];if((c.Attributes&0x0200)!=0)continue;char ch=c.Unicode;if(ch=='\0'||ch=='\uffff')ch=' ';line.Append(ch);}
   text.Append(line.ToString().TrimEnd(' '));if(y+1<rows)text.Append('\n');
  }
  return text.ToString().TrimEnd('\n');
 }
 internal static ConsoleFrame Read(IntPtr handle) {
  if(!GetConsoleScreenBufferInfo(handle,out var info))throw new IOException("터미널 내용을 읽지 못했습니다.");
  int width=info.Size.X;int end=Math.Min(info.Size.Y,Math.Max(info.Cursor.Y+1,info.Window.B+1));int start=0;int height=end;
  if(width<1||width>4096||height<1)throw new IOException("터미널 크기가 유효하지 않습니다.");
  // Read every available buffer row in bounded Win32 calls. A batch is not a history limit.
  var cells=new Cell[checked(width*height)];int batchRows=Math.Max(1,16384/width);
  for(int y=0;y<height;y+=batchRows){
   int rowsInBatch=Math.Min(batchRows,height-y);var part=new Cell[width*rowsInBatch];var region=new Rect{L=0,T=(short)y,R=(short)(width-1),B=(short)(y+rowsInBatch-1)};
   if(!ReadConsoleOutputW(handle,part,new Coord((short)width,(short)rowsInBatch),new Coord(0,0),ref region)||region.T!=y||region.B!=y+rowsInBatch-1||region.L!=0||region.R!=width-1)throw new IOException("터미널 크기가 읽는 중 바뀌었습니다. 다시 읽습니다.");
   Array.Copy(part,0,cells,y*width,part.Length);
  }
  var title=new StringBuilder(1024);GetConsoleTitle(title,1024);var peers=new uint[256];uint count=GetConsoleProcessList(peers,256);
  return new(title.ToString(),Decode(cells,width,height),width,height,info.Cursor.X,info.Cursor.Y-start,peers.Take((int)Math.Min(256,count)).Where(p=>p!=Environment.ProcessId).Select(p=>(int)p).ToArray(),sessionIdentity);
 }
 internal static void SettleInput(IntPtr handle) {
  long deadline=Environment.TickCount64+3000;
  while(GetNumberOfConsoleInputEvents(handle,out uint count)&&count>0){
   if(Environment.TickCount64>=deadline)throw new IOException("입력 처리가 지연되고 있습니다. 앱에서 확인하세요.");
   Thread.Sleep(15);
  }
  Thread.Sleep(400);
 }
 internal static Input[] RemotePasteEvents(string text) {
  text=RemoteImages.Caption(text);
  return ("\x1b[200~"+text+"\x1b[201~").Select(c=>new Input{Type=1,Down=1,Repeat=1,Unicode=c}).ToArray();
 }
 internal static void WriteRemotePaste(IntPtr handle,string text) {
  var events=RemotePasteEvents(text);
  if(!WriteConsoleInputW(handle,events,(uint)events.Length,out var written)||written!=events.Length)throw new IOException("원격 붙여넣기를 전달하지 못했습니다.");
 }
 internal static void Write(IntPtr handle,JsonElement message) {
  var events=new List<Input>();
  void Key(ushort key,char character,uint flags){var down=new Input{Type=1,Down=1,Repeat=1,Key=key,Scan=(ushort)MapVirtualKeyW(key,0),Unicode=character,Flags=flags};events.Add(down);down.Down=0;events.Add(down);}
  if(message.Str("op")=="text"){
   if(message.Bool("bracketedPaste")){WriteRemotePaste(handle,message.Str("text"));return;}
   string value=message.Str("text");if(value.Length>4096)throw new IOException("한 번에 4096자까지 입력할 수 있습니다.");
   foreach(char c in value){if(c=='\r')continue;if(c=='\n')Key(13,'\r',message.Bool("literalNewlines")?16u:0u);else if(message.Bool("literalNewlines"))events.Add(new Input{Type=1,Down=1,Repeat=1,Unicode=c});else Key(0,c,0);}
  }else if(message.Str("op")=="key"){
   string key=message.Str("key");bool ctrl=message.Bool("ctrl"),alt=message.Bool("alt"),shift=message.Bool("shift");
   ushort vk=key switch {"ENTER"=>13,"TAB"=>9,"ESC"=>27,"BACKSPACE"=>8,"DELETE"=>46,"UP"=>38,"DOWN"=>40,"LEFT"=>37,"RIGHT"=>39,"HOME"=>36,"END"=>35,"PAGEUP"=>33,"PAGEDOWN"=>34,"SPACE"=>32,"INSERT"=>45,_ when key.StartsWith("F")&&int.TryParse(key[1..],out var f)&&f>=1&&f<=12=>(ushort)(111+f),_ when key.Length==1&&char.IsAsciiLetterOrDigit(key[0])=>(ushort)char.ToUpperInvariant(key[0]),_=>0};
   if(vk==0)throw new IOException("지원하지 않는 키입니다.");
   char ch=vk switch {13=>'\r',9=>'\t',27=>'\x1b',8=>'\b',32=>' ',_ when vk>=65&&vk<=90=>(char)(ctrl?vk-64:shift?vk:vk+32),_ when vk>=48&&vk<=57=>(char)vk,_=>'\0'};
   Key(vk,ch,(ctrl?8u:0u)|(alt?2u:0u)|(shift?16u:0u)|(vk>=33&&vk<=46?256u:0u));
  }else throw new IOException("지원하지 않는 입력입니다.");
  if(events.Count>0&&(!WriteConsoleInputW(handle,events.ToArray(),(uint)events.Count,out var written)||written!=events.Count))throw new IOException("터미널 입력을 전달하지 못했습니다.");
 }
 // Transport mode only; missing process metadata never vetoes ordinary input.
 // Query this attached console, not a stale tab title or conversation binding.
 internal static bool HasSshInput() {
  var peers=new uint[256];uint count=GetConsoleProcessList(peers,(uint)peers.Length);
  foreach(uint pid in peers.Take((int)Math.Min(count,(uint)peers.Length)))try{
   using var process=Process.GetProcessById((int)pid);
   if(process.ProcessName.Equals("ssh",StringComparison.OrdinalIgnoreCase))return true;
  }catch(ArgumentException){}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}
  return false;
 }
 internal static void Agent(int pid,long started) {
  sessionIdentity=pid+":"+started;
  // Capture only the inherited IPC pipes BEFORE attaching. Never write to target stdout.
  using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);
  using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};var gate=new object();
  void Emit(object o){lock(gate)output.WriteLine(JsonSerializer.Serialize(o));}
  IntPtr read=new(-1),write=new(-1);bool attached=false;
  try{
   using var target=Process.GetProcessById(pid);if(target.StartTime.ToUniversalTime().Ticks!=started)throw new IOException("터미널 세션이 변경되었습니다.");
   if(!AttachConsole((uint)pid))throw new IOException("이 프로세스의 콘솔에 연결할 수 없습니다.");attached=true;SetConsoleCtrlHandler(IntPtr.Zero,true);
   read=CreateFile("CONOUT$",0x80000000,3,IntPtr.Zero,3,0,IntPtr.Zero);write=CreateFile("CONIN$",0xc0000000,3,IntPtr.Zero,3,0,IntPtr.Zero);
   if(read==new IntPtr(-1)||write==new IntPtr(-1))throw new IOException("콘솔에 접근할 수 없습니다. 권한을 확인하세요.");
   using var life=new CancellationTokenSource();
   var reader=Task.Run(()=>{
    var ordered=new OrderedNativeInput(m=>Write(write,m),()=>GetNumberOfConsoleInputEvents(write,out uint count)&&count>0,
     ()=>{if(target.HasExited||target.StartTime.ToUniversalTime().Ticks!=started)throw new IOException("터미널 세션이 종료되거나 변경되었습니다.");});
    try{string? line;while((line=input.ReadLine())!=null){
     if(line.Length>32768)break;
     try{
      using var json=JsonDocument.Parse(line);var message=json.RootElement;
      if(message.Str("op")=="close")break;
      if(message.Str("op")=="remoteImageInput"){Emit(RemoteImages.Input(read,write,message));continue;}
      if(message.Str("op")=="imageInput"){Emit(ImageInput.Execute(read,write,message));continue;}
      if(message.Str("op")=="notificationInput"){
       if(NativeMessageInput.Handles(message.Str("kind"))){
        try{Emit(NativeMessageInput.Execute(ordered.Send,message,HasSshInput()));}
        catch(Exception e){Emit(new{notificationResult=message.Str("request"),ok=false,message=e.Message});}
       }else Emit(NotificationInput.Execute(read,write,message));
       continue;
      }
      ordered.Send(message);
     }catch(Exception e){Emit(new{error=e.Message});}
    }}finally{life.Cancel();}
   });
   string previous="";
   while(!life.IsCancellationRequested&&!target.HasExited){var frame=Read(read);string json=JsonSerializer.Serialize(frame);if(json!=previous){Emit(new{frame});previous=json;}Thread.Sleep(frame.rows>500?1000:250);}
  }catch(Exception e){try{Emit(new{error=e.Message,closed=true});}catch{}}
  finally{if(read!=new IntPtr(-1))CloseHandle(read);if(write!=new IntPtr(-1))CloseHandle(write);if(attached)FreeConsole();}
 }
}
internal sealed class ConsoleBridge : IDisposable {
 readonly Process process;readonly object inputGate=new();
 readonly System.Collections.Concurrent.ConcurrentDictionary<string,TaskCompletionSource<JsonElement>> pending=new();
 internal ConsoleTarget Target{get;} internal volatile ConsoleFrame? Latest;internal volatile string? Error;internal volatile bool Closed;
 internal ConsoleBridge(ConsoleTarget target){
  Target=target;var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
  start.ArgumentList.Add("--console-agent");start.ArgumentList.Add(target.Pid.ToString());start.ArgumentList.Add(target.Started.ToString());
  process=Process.Start(start)??throw new IOException("콘솔 연결을 시작하지 못했습니다.");
  _=process.StandardError.ReadToEndAsync();_=Task.Run(async()=>{try{string? line;while((line=await process.StandardOutput.ReadLineAsync())!=null){using var json=JsonDocument.Parse(line);if(json.RootElement.TryGetProperty("notificationResult",out var result)&&pending.TryRemove(result.GetString()??"",out var completion))completion.TrySetResult(json.RootElement.Clone());if(json.RootElement.TryGetProperty("frame",out var f))Latest=f.Deserialize<ConsoleFrame>();if(json.RootElement.TryGetProperty("error",out var error))Error=error.GetString();}}catch(Exception e){Error=e.Message;}finally{Closed=true;}});
 }
 internal async Task<JsonElement> Notification(JsonElement message,CancellationToken ct,TimeSpan? timeout=null){
  string id=message.Str("request");var completion=new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
  if(!pending.TryAdd(id,completion))throw new IOException("Duplicate notification input");
  try{Send(message);return await completion.Task.WaitAsync(timeout??TimeSpan.FromSeconds(12),ct);}finally{pending.TryRemove(id,out _);}
 }
 internal void Send(JsonElement message){if(Closed||!Target.Valid)throw new IOException("터미널 연결이 종료되었습니다.");lock(inputGate){process.StandardInput.WriteLine(message.GetRawText());process.StandardInput.Flush();}}
 public void Dispose(){try{lock(inputGate){process.StandardInput.WriteLine("{\"op\":\"close\"}");process.StandardInput.Flush();process.StandardInput.Close();}if(!process.WaitForExit(1000))process.Kill();}catch{}process.Dispose();}
}
