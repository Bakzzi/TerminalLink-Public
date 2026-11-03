using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Renci.SshNet;
using Renci.SshNet.Sftp;

namespace TerminalLink;

internal sealed record SshProfile(string host,int port,string user,string password,string hostKey,string hostKeyType) {
 internal string Key=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{host}:{port}:{user}:{hostKeyType}:{hostKey}")));
}
internal sealed record FileSpec(int index,string name,long size,string sha256);
internal sealed record UploadRequest(int version,string id,long created,FileSpec[] files);
internal sealed record Receipt(bool ok,string request,SharedFile[] files);
internal sealed record Journal(string manifest,long started,SharedFile?[] files);
internal sealed record Settings(string[] profiles);

internal static class Bridge {
 internal static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLinkDesktopPreview","filebox-bridge");
 static readonly string SettingsFile=Path.Combine(Root,"settings.json");
 internal static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=false};
 internal static string Hash(byte[] value)=>Convert.ToHexString(SHA256.HashData(value));
 internal static bool Id(string value)=>Regex.IsMatch(value??"",@"\A[a-f0-9]{32}\z");
 internal static string Resource(string name){var a=typeof(Bridge).Assembly;using var stream=a.GetManifestResourceStream(a.GetManifestResourceNames().Single(n=>n.EndsWith("."+name,StringComparison.Ordinal)))!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
 internal static void Atomic<T>(string path,T value){
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){JsonSerializer.Serialize(stream,value);stream.Flush(true);}File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);}
 }
 internal static SshProfile[] Profiles(){
  string path=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","ssh-read.dpapi");
  byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(path),Encoding.UTF8.GetBytes("TerminalLink.SshRead.v1"),DataProtectionScope.CurrentUser);
  try{return JsonSerializer.Deserialize<SshProfile[]>(plain,Json)??[];}finally{CryptographicOperations.ZeroMemory(plain);}
 }
 internal static ConnectionInfo Connection(SshProfile p){
  if(p.port is <1 or >65535||string.IsNullOrWhiteSpace(p.hostKey)||string.IsNullOrWhiteSpace(p.hostKeyType))throw new IOException("Invalid registered SSH profile");
  var info=new PasswordConnectionInfo(p.host,p.port,p.user,p.password){Timeout=TimeSpan.FromSeconds(6)};
  foreach(string k in info.HostKeyAlgorithms.Keys.ToArray())if(k!=p.hostKeyType)info.HostKeyAlgorithms.Remove(k);
  return info;
 }
 internal static bool Pinned(SshProfile p,byte[] key)=>Convert.ToBase64String(SHA256.HashData(key)).TrimEnd('=')==p.hostKey;
 internal static SftpClient Connect(SshProfile p){var c=new SftpClient(Connection(p)){OperationTimeout=TimeSpan.FromSeconds(15),BufferSize=32768,KeepAliveInterval=TimeSpan.FromSeconds(30)};c.HostKeyReceived+=(_,e)=>e.CanTrust=Pinned(p,e.HostKey);try{c.Connect();return c;}catch{c.Dispose();throw;}}
 internal static SshClient ConnectShell(SshProfile p){var c=new SshClient(Connection(p));c.HostKeyReceived+=(_,e)=>e.CanTrust=Pinned(p,e.HostKey);try{c.Connect();return c;}catch{c.Dispose();throw;}}
 internal static string Quote(string s)=>"'"+s.Replace("'","'\"'\"'")+"'";
 internal static void Setup(){
  var profiles=Profiles();if(profiles.Length==0)throw new IOException("No registered SSH profile");
  var enabled=File.Exists(SettingsFile)?JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile),Json)?.profiles??[]:Array.Empty<string>();
  var complete=enabled.ToHashSet(StringComparer.Ordinal);
  foreach(var p in profiles){
   using var ssh=ConnectShell(p);string helper=Resource("publish.py"),skill=Resource("SKILL.md");
   string payload=Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new{helper,skill}));
   using var command=ssh.CreateCommand("python3 -c "+Quote(Resource("setup.py"))+" "+Quote(payload));command.CommandTimeout=TimeSpan.FromSeconds(20);
   string answer=command.Execute();if(command.ExitStatus!=0)throw new IOException("Remote helper installation failed: "+command.Error.Split('\n',StringSplitOptions.RemoveEmptyEntries).LastOrDefault());
   using var doc=JsonDocument.Parse(answer);var hashes=doc.RootElement.GetProperty("hashes");
   if(!doc.RootElement.GetProperty("ok").GetBoolean()||hashes.GetProperty("helper").GetString()!=Hash(Encoding.UTF8.GetBytes(helper))||hashes.GetProperty("skill").GetString()!=Hash(Encoding.UTF8.GetBytes(skill)))throw new IOException("Remote install hash mismatch");
   complete.Add(p.Key);Atomic(SettingsFile,new Settings(complete.ToArray()));
  }
  Atomic(Path.Combine(Root,"setup-result.json"),new{ok=true,count=profiles.Length,helperSha256=Hash(Encoding.UTF8.GetBytes(Resource("publish.py"))),skillSha256=Hash(Encoding.UTF8.GetBytes(Resource("SKILL.md")))});
 }
 internal static void Validate(UploadRequest r,long now){
  if(r.version!=1||!Id(r.id)||r.created<now-86400000||r.created>now+300000||r.files==null||r.files.Length is <1 or >32)throw new IOException("Invalid or expired upload request");
  long total=0;
  for(int i=0;i<r.files.Length;i++){
   var f=r.files[i];if(f==null||f.index!=i||string.IsNullOrWhiteSpace(f.name)||f.name.Length>200||f.name is "." or ".."||f.name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||f.name.EndsWith('.')||f.name.EndsWith(' ')||Regex.IsMatch(f.name,@"\A(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\..*)?\z",RegexOptions.IgnoreCase)||f.size is <0 or >SharedFiles.MaxFile||!Regex.IsMatch(f.sha256??"",@"\A[0-9A-F]{64}\z"))throw new IOException("Invalid file description");
   total+=f.size;
  }
  if(total>SharedFiles.MaxTotal)throw new IOException("Upload exceeds file box quota");
 }
 internal static void CopyVerified(Stream source,Stream output,FileSpec f){
  using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] buffer=new byte[192*1024];long size=0;int n;
  while((n=source.Read(buffer))>0){size+=n;if(size>f.size)throw new IOException("Remote file size changed");output.Write(buffer,0,n);hash.AppendData(buffer,0,n);}
  if(size!=f.size||Convert.ToHexString(hash.GetHashAndReset())!=f.sha256)throw new IOException("Remote file verification failed");
 }
 internal static void VerifyCopy(SharedFiles store,SharedFile actual,FileSpec expected){
  var found=store.Find(actual.id)??throw new IOException("Published file is unavailable or expired");
  if(found!=actual||actual.name!=expected.name||actual.size!=expected.size||actual.sha256!=expected.sha256||actual.expires!=actual.created+86400000)throw new IOException("Published metadata mismatch");
  using var input=File.OpenRead(store.Data(actual.id));if(Convert.ToHexString(SHA256.HashData(input))!=expected.sha256)throw new IOException("Published content mismatch");
 }
 // Reconcile the narrow publish-before-journal crash window from durable PC metadata.
 internal static SharedFile? Recover(SharedFiles store,FileSpec f,long since){
  var matches=store.List().Where(x=>x.created>=since&&x.name==f.name&&x.size==f.size&&x.sha256==f.sha256).ToArray();
  if(matches.Length>1)throw new IOException("Ambiguous publication recovery; not republished");
  if(matches.Length==0)return null;VerifyCopy(store,matches[0],f);return matches[0];
 }
 internal static Receipt Import(UploadRequest r,string manifestHash,string journalPath,SharedFiles store,string staging,Action<FileSpec,string> download,Func<bool> cancelled){
  Validate(r,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
  Journal j;
  if(File.Exists(journalPath)){
   j=JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath),Json)??throw new IOException("Invalid publication journal");
   if(j.manifest!=manifestHash||j.files.Length!=r.files.Length)throw new IOException("Request changed after publication began");
  }else{j=new(manifestHash,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),new SharedFile?[r.files.Length]);Atomic(journalPath,j);}
  for(int i=0;i<r.files.Length;i++){
   if(cancelled())throw new IOException("Upload was cancelled");
   var f=r.files[i];var previous=j.files[i];
   if(previous!=null){VerifyCopy(store,previous,f);continue;}
   var recovered=Recover(store,f,j.started);
   if(recovered==null){
    string folder=Path.Combine(staging,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string file=Path.Combine(folder,f.name);
    try{download(f,file);using(var input=File.OpenRead(file)){if(input.Length!=f.size||Convert.ToHexString(SHA256.HashData(input))!=f.sha256)throw new IOException("Staged copy verification failed");}if(cancelled())throw new IOException("Upload was cancelled");recovered=store.Publish(file);VerifyCopy(store,recovered,f);}
    finally{if(File.Exists(file))File.Delete(file);Directory.Delete(folder,false);}
   }
   j.files[i]=recovered;Atomic(journalPath,j);
  }
  return new(true,r.id,j.files.Select(x=>x!).ToArray());
 }
 static ISftpFile Child(SftpClient c,string parent,string name,bool directory,int uid){
  var f=c.ListDirectory(parent).Single(x=>x.Name==name);
  var a=f.Attributes;
  if(f.IsSymbolicLink||f.UserId!=uid||(directory?!f.IsDirectory:!f.IsRegularFile)||!a.OwnerCanRead||!a.OwnerCanWrite||a.OwnerCanExecute!=directory||a.GroupCanRead||a.GroupCanWrite||a.GroupCanExecute||a.OthersCanRead||a.OthersCanWrite||a.OthersCanExecute)throw new IOException("Unsafe remote queue entry");return f;
 }
 static byte[] Small(SftpClient c,string path){using var stream=c.OpenRead(path);using var output=new MemoryStream();byte[] buffer=new byte[4096];int n;while((n=stream.Read(buffer))>0){if(output.Length+n>65536)throw new IOException("Oversized remote metadata");output.Write(buffer,0,n);}return output.ToArray();}
 static void RemoteJson(SftpClient c,string dir,string name,object value){
  string temp=dir+"/.pc-"+Guid.NewGuid().ToString("N");
  try{using(var stream=c.Open(temp,FileMode.CreateNew,FileAccess.Write)){c.ChangePermissions(temp,600);JsonSerializer.Serialize(stream,value);}c.RenameFile(temp,dir+"/"+name,true);}finally{if(c.Exists(temp))c.DeleteFile(temp);}
 }
 internal static int Pass(SftpClient c,SshProfile profile){
  string home=c.WorkingDirectory.TrimEnd('/'),baseDir=home+"/.local/share/terminallink-filebox-v1",queue=baseDir+"/outbox";
  int uid=c.GetAttributes(home).UserId;Child(c,home+"/.local/share","terminallink-filebox-v1",true,uid);Child(c,baseDir,"outbox",true,uid);
  int completed=0;var store=new SharedFiles();
  foreach(var entry in c.ListDirectory(queue).Where(x=>Id(x.Name)).Take(32)){
   string dir=queue+"/"+entry.Name;
   try{
    Child(c,queue,entry.Name,true,uid);
    if(c.Exists(dir+"/receipt.json")||c.Exists(dir+"/cancel.json"))continue;
    Child(c,dir,"manifest.json",false,uid);byte[] bytes=Small(c,dir+"/manifest.json");
    var r=JsonSerializer.Deserialize<UploadRequest>(bytes,Json)??throw new IOException("Invalid manifest");
    if(r.id!=entry.Name)throw new IOException("Request identity mismatch");Validate(r,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    string journal=Path.Combine(Root,"receipts",profile.Key,r.id+".json");
    Receipt receipt=Import(r,Hash(bytes),journal,store,Path.Combine(Root,"staging"),(f,path)=>{
     string remote=f.index+".bin";var attrs=Child(c,dir,remote,false,uid);if(attrs.Length!=f.size)throw new IOException("Snapshot size mismatch");
     using var source=c.OpenRead(dir+"/"+remote);using var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);CopyVerified(source,target,f);target.Flush(true);
    },()=>c.Exists(dir+"/cancel.json"));
    RemoteJson(c,dir,"receipt.json",receipt);completed++;
   }catch(Exception e)when(e is IOException or JsonException or InvalidOperationException){
    // Retryable failures never produce a success receipt; keep other queued requests working.
    Atomic(Path.Combine(Root,"last-error.json"),new{time=DateTimeOffset.UtcNow,request=entry.Name,error=e.GetType().Name,message=e is IOException?e.Message:"Invalid upload metadata"});
   }
  }
  return completed;
 }
 internal static void Run(bool once){
  using var mutex=new Mutex(true,"Local\\TerminalLinkFileBoxBridge-v1",out bool first);if(!first)throw new IOException("File box bridge is already running");
  using var stop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\TerminalLinkFileBoxBridgeStop-v1");stop.Reset();
  var settings=JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile),Json)??throw new IOException("Run remote setup first");
  var profiles=Profiles().Where(p=>settings.profiles.Contains(p.Key,StringComparer.Ordinal)).ToArray();if(profiles.Length==0)throw new IOException("Registered SSH profile changed; setup is required");
  var clients=new Dictionary<string,SftpClient>();
  try{do{
   foreach(var p in profiles)try{
    if(!clients.TryGetValue(p.Key,out var c)||!c.IsConnected){c?.Dispose();clients[p.Key]=c=Connect(p);}
    int n=Pass(c,p);Atomic(Path.Combine(Root,"status.json"),new{connected=true,checkedAt=DateTimeOffset.UtcNow,completed=n,version="0.1.0"});
   }catch(Exception){if(clients.Remove(p.Key,out var c))c.Dispose();Atomic(Path.Combine(Root,"status.json"),new{connected=false,checkedAt=DateTimeOffset.UtcNow,message="SSH connection unavailable; queued uploads remain pending"});if(once)throw;}
   if(!once&&stop.WaitOne(5000))break;
  }while(!once);}finally{foreach(var c in clients.Values)c.Dispose();}
 }
}

internal static class Program {
 [STAThread] static int Main(string[] args){
  try{
   if(args.Length==1&&args[0]=="--check"){Checks.Run();return 0;}
   if(args.Length==1&&args[0]=="--remote-check"){RemoteCheck.Run();return 0;}
   if(args.Length==1&&args[0]=="--setup-remote"){Bridge.Setup();return 0;}
   if(args.Length==1&&args[0] is "--run" or "--once"){Bridge.Run(args[0]=="--once");return 0;}
   if(args.Length==1&&args[0]=="--stop"){if(EventWaitHandle.TryOpenExisting("Local\\TerminalLinkFileBoxBridgeStop-v1",out var stop)){using(stop)stop.Set();}return 0;}
   throw new IOException("Use --setup-remote, --run, --once or --check");
  }catch(Exception e){Bridge.Atomic(Path.Combine(Bridge.Root,"command-error.json"),new{error=e.GetType().Name,message=e.Message});return 1;}
 }
}
