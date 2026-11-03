using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace TerminalLink;

internal static class DesktopPaths {
 internal static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLinkDesktopPreview");
 internal static readonly string Existing=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink");
 internal static string AttachmentDirectory=>Path.Combine(Root,"attachments");
 internal static void Configure(bool seed){
  Directory.CreateDirectory(Root);
  string local=Path.Combine(Root,"daemon-record-links-v1");
  LocalDaemonFeed.StorageOverride=local;
  RemoteCodexFeed.BindingCacheFile=Path.Combine(Root,"ssh-bindings.dpapi");
  if(!seed)return;
  // Only continuity metadata is seeded. Every entry is revalidated by the original reader.
  // No phone certificate, identity, settings or SSH registration is copied or written.
  void CopyIfMissing(string source,string dest){try{if(!File.Exists(dest)&&File.Exists(source))File.Copy(source,dest,false);}catch(IOException){}catch(UnauthorizedAccessException){}}
  Directory.CreateDirectory(local);
  var old=Path.Combine(Existing,"daemon-record-links-v1");
  if(Directory.Exists(old))foreach(string p in Directory.EnumerateFiles(old,"*.dpapi"))CopyIfMissing(p,Path.Combine(local,Path.GetFileName(p)));
  CopyIfMissing(Path.Combine(Existing,"ssh-bindings.dpapi"),RemoteCodexFeed.BindingCacheFile);
  NormalizeBindings(RemoteCodexFeed.BindingCacheFile);
 }
 internal static RemoteCodexFeed.Binding[] BindingRecords(JsonElement value){
  var result=new List<RemoteCodexFeed.Binding>();
  void Read(JsonElement node,int depth){
   if(depth>4)return;
   if(node.ValueKind==JsonValueKind.Array){foreach(var item in node.EnumerateArray())Read(item,depth+1);return;}
   if(node.ValueKind!=JsonValueKind.Object)return;
   // Legacy PowerShell JSON can wrap each record in {value: record, Count: 1}.
   if(node.TryGetProperty("value",out var wrapped)&&node.TryGetProperty("Count",out var count)&&count.TryGetInt32(out int n)&&n==1){Read(wrapped,depth+1);return;}
   try{var b=node.Deserialize<RemoteCodexFeed.Binding>();if(b?.endpoint!=null&&b.endpoint.pid>0&&b.endpoint.started>0&&b.endpoint.port is >0 and <=65535&&!string.IsNullOrEmpty(b.path)&&!string.IsNullOrEmpty(b.tui)&&!string.IsNullOrEmpty(b.stamp)&&!string.IsNullOrEmpty(b.thread))result.Add(b);}catch(JsonException){}
  }
  Read(value,0);return result.GroupBy(b=>b.endpoint.Key).Where(g=>g.Count()==1).Select(g=>g.Single()).ToArray();
 }
 static void NormalizeBindings(string path){
  try{if(!File.Exists(path)||new FileInfo(path).Length>1048576)return;byte[] entropy=Encoding.UTF8.GetBytes("TerminalLink.SshBinding.v1");byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),entropy,DataProtectionScope.CurrentUser);
   try{using var doc=JsonDocument.Parse(plain);byte[] clean=JsonSerializer.SerializeToUtf8Bytes(BindingRecords(doc.RootElement));try{if(!plain.AsSpan().SequenceEqual(clean)){string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(tmp,ProtectedData.Protect(clean,entropy,DataProtectionScope.CurrentUser));File.Move(tmp,path,true);}}finally{CryptographicOperations.ZeroMemory(clean);}}
   finally{CryptographicOperations.ZeroMemory(plain);}
  }catch{}
 }
}

internal static class DesktopProgram {
 [STAThread] static void Main(string[] args){
  if(AutomaticUpdates.Handle(args))return;
  try{
   if(args.Length==1&&args[0]=="--visibility-guard"){VisibilityGuard.Run();return;}
   if(args.Length==1&&args[0]=="--connector-guard"){ConnectorGuard.Run();return;}
   if(args.Length==1&&args[0]=="--connector-fixture"){ConnectorDockChecks.Fixture();return;}
   if(args.Length==1&&args[0]=="--connector-crash-fixture"){ConnectorDockChecks.CrashHost();return;}
   if(args.Length==1&&args[0]=="--self-check"){DesktopChecks.Run();return;}
   if(args.Length==1&&args[0]=="--background-reader"){DesktopBackgroundReader.Agent();return;}
   if(args.Length==1&&args[0]=="--background-check"){DesktopBackgroundReader.Check().GetAwaiter().GetResult();return;}
   bool demo=args.Contains("--demo")||args.Contains("--demo-render")||args.Contains("--ui-check")||args.Contains("--error-ui-check")||args.Contains("--attachment-ui-check")||args.Contains("--phone-ui-check")||args.Contains("--tray-ui-check")||args.Contains("--start-ui-check");
   if(!demo)DesktopPaths.Configure(true);
   if(args.Length==3&&args[0]=="--console-agent"){ConsoleNative.Agent(int.Parse(args[1]),long.Parse(args[2]));return;}
   if(args.Length==1&&args[0]=="--console-titles"){ConsoleNative.TitleAgent();return;}
   if(args.Length==5&&args[0]=="--new-tab"){NewTerminal.Agent(int.Parse(args[1]),long.Parse(args[2]),long.Parse(args[3]),int.Parse(args[4]));return;}
   if(args.Length==5&&args[0]=="--close-tab"){CloseTerminal.Agent(int.Parse(args[1]),long.Parse(args[2]),long.Parse(args[3]),int.Parse(args[4]));return;}
   if(args.Length==5&&args[0]=="--open-native"){
    using var output=new StreamWriter(Console.OpenStandardOutput(),new UTF8Encoding(false)){AutoFlush=true};
    try{
     var window=TerminalWindows.Read(true).Single(w=>w.Handle==long.Parse(args[3])&&w.Pid==int.Parse(args[4]));
     var target=new ConsoleTarget(window,int.Parse(args[1]),long.Parse(args[2]),"");
     using var locator=new TerminalTabLocator();var located=locator.Find(target,false);
     if(!located.tab.TryGetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern,out var selection))throw new IOException("기존 탭을 선택할 수 없습니다.");
     ((System.Windows.Automation.SelectionItemPattern)selection).Select();
     WindowVisibility.ShowForeground(located.window);output.WriteLine("ok");
    }catch(Exception e){output.WriteLine(e.Message);}return;
   }
   if(args.Length>0&&!demo&&!args.Contains("--live-check")&&!args.Contains("--phone-link"))throw new IOException("지원하지 않는 실행 옵션입니다.");
   using var mutex=new Mutex(true,"Local\\TerminalLinkDesktopPreview"+(demo?"Demo":""),out bool first);
   if(!first){MessageDialog.Notify(null,"TerminalLink","TerminalLink Desktop이 이미 실행 중입니다.");return;}
   if(!demo){VisibilityLease.RecoverAbandoned();ConnectorDockLease.RecoverAbandoned();}
   var app=new System.Windows.Application{ShutdownMode=System.Windows.ShutdownMode.OnMainWindowClose};
   app.DispatcherUnhandledException+=(_,e)=>{MessageDialog.Notify(app.MainWindow,"TerminalLink",e.Exception.Message);e.Handled=true;};
   var main=new DesktopWindow(demo,args.Contains("--demo-render"),args.Contains("--live-check"),args.Contains("--phone-link"));
   if(args.Contains("--ui-check")||args.Contains("--demo-render")||args.Contains("--error-ui-check")||args.Contains("--attachment-ui-check")||args.Contains("--phone-ui-check")||args.Contains("--tray-ui-check")||args.Contains("--start-ui-check")){main.ShowActivated=false;main.ShowInTaskbar=false;main.WindowStartupLocation=System.Windows.WindowStartupLocation.Manual;main.Left=-4000;main.Top=-4000;}
   void CheckOnce(Func<Task> check){bool checking=false;main.Loaded+=async(_,_)=>{if(checking)return;checking=true;await check();};}
   if(args.Contains("--ui-check"))CheckOnce(main.CheckSending);
   if(args.Contains("--start-ui-check"))CheckOnce(main.CheckStartUi);
   if(args.Contains("--error-ui-check"))CheckOnce(main.CheckErrorUi);
   if(args.Contains("--attachment-ui-check"))CheckOnce(main.CheckAttachmentUi);
   if(args.Contains("--phone-ui-check"))CheckOnce(main.CheckPhoneUi);
   if(args.Contains("--tray-ui-check"))CheckOnce(main.CheckTrayUi);
   app.Run(main);
  }catch(Exception e){
   if(args.Length>0){try{File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"desktop-error.txt"),e.ToString());}catch{}}
   else MessageDialog.Notify(null,"TerminalLink",e.Message);
   Environment.ExitCode=1;
  }
 }
 internal static ProcessStartInfo Worker(string mode,ConsoleTarget? t=null){
  var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8};
  start.ArgumentList.Add(mode);
  if(t!=null)foreach(var a in new[]{t.Pid.ToString(),t.Started.ToString(),t.Window.Handle.ToString(),t.Window.Pid.ToString()})start.ArgumentList.Add(a);
  return start;
 }
}
