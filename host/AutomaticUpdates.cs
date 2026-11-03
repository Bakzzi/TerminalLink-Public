using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace TerminalLink;

// Local publication only. No network URL, shell command or destination is accepted
// from the manifest. The directory inherits the current user's LocalAppData ACL.
internal static class AutomaticUpdates {
#if AUTOUPDATE_TEST
 internal const string Product="fixture",Exe="Fixture.exe",DirectoryName="TerminalLinkUpdateFixture";
#elif DESKTOP
 internal const string Product="desktop",Exe="TerminalLink-Desktop.exe",DirectoryName="TerminalLinkDesktopPreview";
#else
 internal const string Product="connector",Exe="TerminalLink-PC.exe",DirectoryName="TerminalLink";
#endif
 internal sealed record Release(string product,string version,string sha256,long size,string commit);
 internal sealed record Plan(Release release,int pid,long started,string previousHash,string[] args,string ticket);
 sealed class NotNewException:IOException{}
#if AUTOUPDATE_TEST
 internal static string Install=>Environment.GetEnvironmentVariable("TERMINALLINK_UPDATE_FIXTURE")??throw new IOException("Missing isolated fixture directory");
#else
 internal static string Install=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs",DirectoryName);
#endif
 internal static string Root=>Path.Combine(Install,"automatic-updates");
 static string Catalog=>Path.Combine(Root,"release.json");
 static long checkedAt;static bool checking,launched;static string rejected="";
 internal static bool Installed=>string.Equals(Path.GetFullPath(Environment.ProcessPath!),Path.Combine(Install,Exe),StringComparison.OrdinalIgnoreCase);
 internal static string Hash(string path){using var input=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(input));}
 internal static void NoLinks(string path){for(var p=new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);p!=null;p=p.Parent)if(p.Exists&&(p.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked update directory rejected");if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Linked update file rejected");}
 internal static Release Read(string catalog,string product,Version installed){
  NoLinks(catalog);if(new FileInfo(catalog).Length>8192)throw new IOException("Update manifest too large");
  var r=JsonSerializer.Deserialize<Release>(File.ReadAllText(catalog))??throw new IOException("Missing update manifest");
  if(r.product!=product||!Version.TryParse(r.version,out var v)||!System.Text.RegularExpressions.Regex.IsMatch(r.sha256??"","^[A-F0-9]{64}$")||r.size<1||r.size>512L*1024*1024||!System.Text.RegularExpressions.Regex.IsMatch(r.commit??"","^[a-f0-9]{40}$"))throw new IOException("Invalid update manifest");if(v<=installed)throw new NotNewException();return r;
 }
 internal static void VerifyBundle(string path,Release r){NoLinks(path);if(new FileInfo(path).Length!=r.size||Hash(path)!=r.sha256)throw new IOException("Update bundle hash mismatch");}
 internal static void Extract(string bundle,string target,string exe){
  NoLinks(Path.Combine(target,"placeholder"));Directory.CreateDirectory(target);
  using var zip=ZipFile.OpenRead(bundle);long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var e in zip.Entries){
   string n=e.FullName.Replace('\\','/');if(n.EndsWith('/'))continue;
   bool allowed=n==exe||n=="LICENSE"||n=="THIRD_PARTY_NOTICES.md"||System.Text.RegularExpressions.Regex.IsMatch(n,"^licenses/[A-Za-z0-9_.-]+$");
   if(!allowed||!names.Add(n)||e.Length<0||(total+=e.Length)>700L*1024*1024)throw new IOException("Unexpected update entry");
   string dest=Path.Combine(target,n.Replace('/',Path.DirectorySeparatorChar));NoLinks(dest);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);e.ExtractToFile(dest,true);
  }
  if(!names.Contains(exe)||!names.Contains("LICENSE")||!names.Contains("THIRD_PARTY_NOTICES.md"))throw new IOException("Incomplete update bundle");
 }
 internal static async Task Check(Func<bool> idle,Func<string> save,Action close,Action<string> notice){
  if(!Installed||checking||launched||Environment.TickCount64-checkedAt<15000||!File.Exists(Catalog)||File.Exists(Path.Combine(Root,"disabled")))return;
  checkedAt=Environment.TickCount64;if(!idle())return;checking=true;
  try{
   string stamp=File.GetLastWriteTimeUtc(Catalog).Ticks.ToString();if(stamp==rejected)return;
   Release r;
   try{r=await Task.Run(()=>{var release=Read(Catalog,Product,Version.Parse(FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion!));VerifyBundle(Path.Combine(Root,release.sha256+".zip"),release);return release;});}catch{rejected=stamp;throw;}
   if(File.Exists(Path.Combine(Root,r.sha256+".attempted"))||!idle())return;
   string ticket=Guid.NewGuid().ToString("N"),worker=Path.Combine(Root,"worker.exe");NoLinks(worker);
   File.Copy(Environment.ProcessPath!,worker,true);
   using var me=Process.GetCurrentProcess();
   var plan=new Plan(r,me.Id,me.StartTime.ToUniversalTime().Ticks,Hash(Environment.ProcessPath!),Environment.GetCommandLineArgs().Skip(1).ToArray(),ticket);
   File.WriteAllText(Path.Combine(Root,"resume-pending.json"),save());
   File.WriteAllText(Path.Combine(Root,"plan.json"),JsonSerializer.Serialize(plan));
   var start=new ProcessStartInfo(worker){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};start.ArgumentList.Add("--apply-local-update");start.ArgumentList.Add(ticket);
   using var child=Process.Start(start)??throw new IOException("Could not start updater");
   try{close();launched=true;}catch{File.WriteAllText(Path.Combine(Root,ticket+".cancelled"),"cancelled");throw;}
  }catch(NotNewException){}catch(Exception e){notice("자동 업데이트 대기: "+e.Message);}finally{checking=false;}
 }
 internal static string? Resume(){if(!Installed)return null;string path=Path.Combine(Root,"resume.json");try{NoLinks(path);if(!File.Exists(path)||new FileInfo(path).Length>65536)return null;var s=File.ReadAllText(path);File.Delete(path);return s;}catch{return null;}}
 internal static void Healthy(){
  if(!Installed)return;try{string file=Path.Combine(Root,"health-request.json");if(!File.Exists(file))return;NoLinks(file);var p=JsonSerializer.Deserialize<Plan>(File.ReadAllText(file));if(p!=null&&Version.Parse(p.release.version)==Version.Parse(FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion!))File.WriteAllText(Path.Combine(Root,p.ticket+".healthy"),"ok");}catch{}
 }
 internal static bool Handle(string[] args){
  if(args.Length!=2||args[0]!="--apply-local-update")return false;
  try{Apply(args[1]);}catch(Exception e){try{File.WriteAllText(Path.Combine(Root,"last-result.json"),JsonSerializer.Serialize(new{ok=false,error=e.Message}));Recover(args[1]);}catch{}Environment.ExitCode=1;}return true;
 }
 static void Apply(string ticket){
  if(!System.Text.RegularExpressions.Regex.IsMatch(ticket,"^[a-f0-9]{32}$")||!string.Equals(Environment.ProcessPath,Path.Combine(Root,"worker.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Invalid updater invocation");
  NoLinks(Path.Combine(Root,"plan.json"));var p=JsonSerializer.Deserialize<Plan>(File.ReadAllText(Path.Combine(Root,"plan.json")))??throw new IOException("Missing plan");if(p.ticket!=ticket||p.release.product!=Product)throw new IOException("Invalid update plan");
  string target=Path.Combine(Install,Exe),bundle=Path.Combine(Root,p.release.sha256+".zip"),prepared=Path.Combine(Root,"prepared-"+ticket),backup=Path.Combine(Root,"previous.exe");
  NoLinks(target);NoLinks(backup);if(Hash(Environment.ProcessPath!)!=p.previousHash||Hash(target)!=p.previousHash)throw new IOException("Installed version changed");
  VerifyBundle(bundle,p.release);Extract(bundle,prepared,Exe);
  string incoming=Path.Combine(prepared,Exe);if(Version.Parse(FileVersionInfo.GetVersionInfo(incoming).FileVersion!)!=Version.Parse(p.release.version)||Version.Parse(p.release.version)<=Version.Parse(FileVersionInfo.GetVersionInfo(target).FileVersion!))throw new IOException("Executable version mismatch");
  try{using var parent=Process.GetProcessById(p.pid);if(parent.StartTime.ToUniversalTime().Ticks!=p.started||!string.Equals(parent.MainModule?.FileName,target,StringComparison.OrdinalIgnoreCase))throw new IOException("Parent identity changed");if(!parent.WaitForExit(60000))throw new IOException("Application is busy; update deferred");}catch(ArgumentException){}
  // Native console workers can finish shortly after their UI exits. Never kill them.
  var deadline=DateTime.UtcNow.AddSeconds(45);while(true)try{using var exclusive=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None);break;}catch(IOException)when(DateTime.UtcNow<deadline){Thread.Sleep(250);}
  if(File.Exists(Path.Combine(Root,ticket+".cancelled")))throw new IOException("Update cancelled while application was busy");
  if(Hash(target)!=p.previousHash)throw new IOException("Installed executable changed before replacement");
  Process? next=null;
  try{
  File.WriteAllText(Path.Combine(Root,p.release.sha256+".attempted"),ticket);
  File.Copy(target,backup,true);string staged=Path.Combine(Install,Exe+".next");NoLinks(staged);File.Copy(incoming,staged,true);File.Move(staged,target,true);
  foreach(var f in Directory.EnumerateFiles(prepared,"*",SearchOption.AllDirectories)){string relative=Path.GetRelativePath(prepared,f);if(relative==Exe)continue;string dest=Path.Combine(Install,relative);NoLinks(dest);Directory.CreateDirectory(Path.GetDirectoryName(dest)!);File.Copy(f,dest,true);}
  File.Move(Path.Combine(Root,"resume-pending.json"),Path.Combine(Root,"resume.json"),true);
  File.WriteAllText(Path.Combine(Root,"health-request.json"),JsonSerializer.Serialize(p));
   next=Start(target,p.args);bool healthy=false;var until=DateTime.UtcNow.AddSeconds(60);
   while(DateTime.UtcNow<until){if(next.HasExited)break;if(File.Exists(Path.Combine(Root,ticket+".healthy"))){Thread.Sleep(1000);healthy=!next.HasExited;break;}Thread.Sleep(250);}
   if(!healthy){if(!next.HasExited)throw new IOException("New process is running but health check is pending; previous version retained");throw new IOException("New version exited before health confirmation");}
   File.WriteAllText(Path.Combine(Root,"last-result.json"),JsonSerializer.Serialize(new{ok=true,version=p.release.version,sha256=p.release.sha256,commit=p.release.commit}));
  }catch{
   if(next==null||next.HasExited){if(File.Exists(backup)&&Hash(backup)==p.previousHash)File.Copy(backup,target,true);if(Hash(target)==p.previousHash){using var restored=Start(target,p.args);}}
   throw;
  }finally{next?.Dispose();}
 }
 static void Recover(string ticket){
  if(!string.Equals(Environment.ProcessPath,Path.Combine(Root,"worker.exe"),StringComparison.OrdinalIgnoreCase))return;
  var p=JsonSerializer.Deserialize<Plan>(File.ReadAllText(Path.Combine(Root,"plan.json")));if(p==null||p.ticket!=ticket||p.release.product!=Product)return;
  if(File.Exists(Path.Combine(Root,ticket+".cancelled")))return;
  try{using var parent=Process.GetProcessById(p.pid);if(!parent.HasExited&&parent.StartTime.ToUniversalTime().Ticks==p.started&&!parent.WaitForExit(60000))return;}catch(ArgumentException){}
  string target=Path.Combine(Install,Exe);NoLinks(target);if(Hash(target)!=p.previousHash)return;
  foreach(var candidate in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Exe)))using(candidate){try{if(!candidate.HasExited&&string.Equals(candidate.MainModule?.FileName,target,StringComparison.OrdinalIgnoreCase))return;}catch{}}
  if(File.Exists(Path.Combine(Root,"resume-pending.json")))File.Move(Path.Combine(Root,"resume-pending.json"),Path.Combine(Root,"resume.json"),true);
  using var restored=Start(target,p.args);
 }
 static Process Start(string target,string[] args){var start=new ProcessStartInfo(target){UseShellExecute=false,WorkingDirectory=Install,WindowStyle=ProcessWindowStyle.Hidden};foreach(var arg in args)start.ArgumentList.Add(arg);return Process.Start(start)??throw new IOException("Could not restart application");}
}
