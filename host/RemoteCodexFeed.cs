using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Renci.SshNet;
namespace TerminalLink;

// A separate, read-only SSH connection. It never sends input to the user's terminal.
// Identity needs the exact local SSH socket, remote TUI identity, a CLI-owned record,
// matching working directory, and an unambiguous user-message anchor (or explicit UUID).
internal static class RemoteCodexFeed {
 internal sealed record Profile(string host,int port,string user,string password,string hostKey,string hostKeyType);
 internal sealed record Endpoint(int pid,long started,string local,int localPort,string host,int port){internal string Key=>$"{pid}:{started}:{local}:{localPort}:{host}:{port}";internal string Connection=>$"{local} {localPort} {host} {port}";}
 static readonly string profilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","ssh-read.dpapi");
 static readonly byte[] entropy=Encoding.UTF8.GetBytes("TerminalLink.SshRead.v1");
 internal static string BindingCacheFile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TerminalLink","ssh-bindings.dpapi");
 static readonly object bindingLock=new();static readonly byte[] bindingEntropy=Encoding.UTF8.GetBytes("TerminalLink.SshBinding.v1");
 internal static Binding[] SavedBindings(){lock(bindingLock)try{var file=new FileInfo(BindingCacheFile);if(!file.Exists||file.Length>1048576)return Array.Empty<Binding>();byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(BindingCacheFile),bindingEntropy,DataProtectionScope.CurrentUser);try{return JsonSerializer.Deserialize<Binding[]>(plain)??Array.Empty<Binding>();}finally{CryptographicOperations.ZeroMemory(plain);}}catch{return Array.Empty<Binding>();}}
 static void SaveBinding(Binding binding){lock(bindingLock)try{var records=SavedBindings().Where(b=>b.endpoint.Key!=binding.endpoint.Key&&Endpoints(new[]{b.endpoint.pid}).Any(e=>e==b.endpoint)).Append(binding).ToArray();byte[] plain=JsonSerializer.SerializeToUtf8Bytes(records);try{Directory.CreateDirectory(Path.GetDirectoryName(BindingCacheFile)!);File.WriteAllBytes(BindingCacheFile+".tmp",ProtectedData.Protect(plain,bindingEntropy,DataProtectionScope.CurrentUser));File.Move(BindingCacheFile+".tmp",BindingCacheFile,true);}finally{CryptographicOperations.ZeroMemory(plain);}}catch{}}
 static readonly ConcurrentDictionary<string,Entry> entries=new();
 static readonly object profileLock=new();static Profile[] profiles=Array.Empty<Profile>();static long nextProfiles;
 internal static Profile[] Profiles(){lock(profileLock){if(Environment.TickCount64<nextProfiles)return profiles;nextProfiles=Environment.TickCount64+5000;try{byte[] data=ProtectedData.Unprotect(File.ReadAllBytes(profilePath),entropy,DataProtectionScope.CurrentUser);try{profiles=JsonSerializer.Deserialize<Profile[]>(data)??Array.Empty<Profile>();}finally{CryptographicOperations.ZeroMemory(data);}}catch{profiles=Array.Empty<Profile>();}return profiles;}}
 internal static void Register(){using var input=new StreamReader(Console.OpenStandardInput(),Encoding.UTF8);var profile=JsonSerializer.Deserialize<Profile>(input.ReadToEnd())??throw new IOException("Missing SSH read profile");if(!IPAddress.TryParse(profile.host,out _)||profile.port is <1 or >65535||profile.user.Length==0||profile.hostKey.Length==0||profile.hostKeyType.Length==0)throw new IOException("Invalid SSH read profile");using(var test=Connect(profile)){}var list=Profiles().Where(p=>p.host!=profile.host||p.port!=profile.port||p.user!=profile.user).Append(profile).ToArray();byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(list);try{Directory.CreateDirectory(Path.GetDirectoryName(profilePath)!);File.WriteAllBytes(profilePath,ProtectedData.Protect(bytes,entropy,DataProtectionScope.CurrentUser));}finally{CryptographicOperations.ZeroMemory(bytes);}}
 internal static SshClient Connect(Profile profile){var info=new PasswordConnectionInfo(profile.host,profile.port,profile.user,profile.password){Timeout=TimeSpan.FromSeconds(6)};foreach(string key in info.HostKeyAlgorithms.Keys.ToArray())if(key!=profile.hostKeyType)info.HostKeyAlgorithms.Remove(key);var client=new SshClient(info);client.HostKeyReceived+=(_,e)=>e.CanTrust=Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=')==profile.hostKey;try{client.Connect();return client;}catch{client.Dispose();throw;}}
 internal static Conversation? Read(ConsoleFrame frame){
  var registered=Profiles();if(registered.Length==0)return null;
  var endpoints=Endpoints(frame.peers).ToArray();if(endpoints.Length!=1)return null;
  var endpoint=endpoints[0];var matches=registered.Where(p=>p.host==endpoint.host&&p.port==endpoint.port).ToArray();if(matches.Length!=1)return null;
  Entry entry;lock(entries){if(!entries.TryGetValue(endpoint.Key,out entry!)){var saved=SavedBindings().SingleOrDefault(b=>b.endpoint==endpoint);if(!ScreenConversation.Recognizes(frame)&&saved==null)return null;entry=new(endpoint,matches[0],saved);entries[endpoint.Key]=entry;entry.Start();}}
  entry.Touch(frame);return entry.Latest;
 }
 // A clipped tool result can follow a complete one-line user prompt without a speaker marker.
 // The remote matcher still requires equality with a complete stored user message, never a prefix.
 internal sealed record Binding(Endpoint endpoint,string path,string tui,string stamp,string thread);
 internal static Binding BoundTo(ConsoleFrame frame,string thread){
  var endpoint=Endpoints(frame.peers).SingleOrDefault()??throw new IOException("SSH 연결을 확인할 수 없습니다.");
  if(!entries.TryGetValue(endpoint.Key,out var entry)||entry.Bound is not Binding bound||bound.thread!=thread||entry.Latest?.id!=thread)throw new IOException("현재 원격 대화 기록을 확인한 뒤 다시 첨부하세요.");
  return bound;
 }
 internal static Profile RequireProfile(Binding bound,ConsoleFrame frame){
  if(!Endpoints(frame.peers).Any(e=>e==bound.endpoint))throw new IOException("SSH 연결이 바뀌어 사진 전송을 중단했습니다.");
  var profiles=Profiles().Where(p=>p.host==bound.endpoint.host&&p.port==bound.endpoint.port).ToArray();
  if(profiles.Length!=1)throw new IOException("등록된 SSH 연결을 확인할 수 없습니다.");return profiles[0];
 }
 internal static void Verify(SshClient client,Binding bound){
  string request=JsonSerializer.Serialize(new{connection=bound.endpoint.Connection,path=bound.path,tui=bound.tui,stamp=bound.stamp,validateOnly=true,anchors=Array.Empty<string>()});
  using var command=client.CreateCommand("python3 -c "+Quote(Script)+" "+Quote(request));command.CommandTimeout=TimeSpan.FromSeconds(5);
  using var doc=JsonDocument.Parse(command.Execute());var result=doc.RootElement;
  if(command.ExitStatus!=0||!result.Bool("available")||result.Str("stamp")!=bound.stamp)throw new IOException("원격 Codex 대화가 바뀌어 사진 전송을 중단했습니다.");
  var profile=Profiles().Single(p=>p.host==bound.endpoint.host&&p.port==bound.endpoint.port);
  string thread="remote:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.host+":"+profile.port+":"+profile.user+":"+result.Str("id"))));
  if(thread!=bound.thread)throw new IOException("사진의 대상 대화가 바뀌었습니다.");
 }
 internal static string[] Anchors(ConsoleFrame frame)=>ScreenConversation.Extract(frame).Where(m=>m.role=="user")
  // A folded question is incomplete; its expansion hint is never user text.
  .Where(m=>!Regex.IsMatch(m.text,@"(?m)^\s*\+ (?:Show details|\d+ lines? \((?:ctrl\+t|\^t) to expand\))\s*$"))
  .TakeLast(3).SelectMany(m=>new[]{m.text,m.text.Split('\n')[0]}).Distinct().ToArray();
 // Only explicit assistant prose supplies recovery evidence when the submitted
 // question has scrolled out of view. Tool output and the unsent composer do not.
 internal static string[][] AnswerAnchors(ConsoleFrame frame)=>ScreenConversation.Extract(frame)
  .Where(m=>m.role=="assistant").TakeLast(2)
  .Select(m=>m.text.Split('\n').Select(line=>Regex.Replace(line.Trim(),@"^[•*-]\s+","")).Where(line=>!Regex.IsMatch(line,@"^[<{\[|`~]")).Select(line=>Regex.Replace(line,@"[\s*`_]","")).Where(line=>line.Length>=24).Distinct().ToArray())
  .Where(lines=>lines.Length>0).ToArray();
 internal static string Diagnostic="not-started";
 internal static void Stop(){foreach(var entry in entries.Values)entry.Dispose();entries.Clear();}
 sealed class Entry : IDisposable {
  readonly Endpoint endpoint;readonly Profile profile;readonly CancellationTokenSource life=new();
  internal volatile Conversation? Latest;internal volatile Binding? Bound;volatile ConsoleFrame? frame;long touched=Environment.TickCount64;SshClient? client;CodexFeed feed=new();
  string path="",tui="",stamp="";long offset;
  Binding? seed;internal Entry(Endpoint endpoint,Profile profile,Binding? seed){this.endpoint=endpoint;this.profile=profile;this.seed=seed;}
  internal void Start()=>_=Task.Run(Loop);
  internal void Touch(ConsoleFrame value){frame=value;Interlocked.Exchange(ref touched,Environment.TickCount64);}
  async Task Loop(){try{while(!life.IsCancellationRequested&&Environment.TickCount64-Interlocked.Read(ref touched)<30000){
   if(frame is ConsoleFrame current)try{
    if(!Endpoints(current.peers).Any(e=>e==endpoint)){Bound=null;Latest=null;break;}
    if(client?.IsConnected!=true){client?.Dispose();client=Connect(profile);}
    if(seed is Binding saved){try{Verify(client,saved);seed=null;path=saved.path;tui=saved.tui;stamp=saved.stamp;offset=0;}catch{path=tui=stamp="";offset=0;}}
    var anchors=Anchors(current);
    string request=JsonSerializer.Serialize(new{connection=endpoint.Connection,anchors,answers=AnswerAnchors(current),path,tui,stamp,offset});
    using var command=client.CreateCommand("python3 -c "+Quote(Script)+" "+Quote(request));command.CommandTimeout=TimeSpan.FromSeconds(20);
    string output=command.Execute();if(command.ExitStatus!=0)throw new IOException("Remote history read failed");
    using var doc=JsonDocument.Parse(output);var result=doc.RootElement;
    if(!result.Bool("available")){Bound=null;Diagnostic=result.Str("reason");Latest=null;path=tui=stamp="";offset=0;feed=new();}
    else{
     string next=result.Str("stamp");long start=(long)result.Num("start");if(next!=stamp||start<offset){feed=new();Latest=null;}
     bool changed=Latest==null;
     foreach(var record in result.GetProperty("records").EnumerateArray())changed|=feed.AddRecord(Encoding.UTF8.GetBytes(record.GetRawText()));
     seed=null;Diagnostic="record-confirmed";path=result.Str("path");tui=result.Str("tui");stamp=next;offset=(long)result.Num("offset");
     if(changed){feed.Publish();var chat=feed.Snapshot!;string identity=profile.host+":"+profile.port+":"+profile.user+":"+chat.id;Latest=chat with{id="remote:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))),source="remote",readOnly=true};}
     var confirmed=new Binding(endpoint,path,tui,stamp,Latest!.id);if(Bound!=confirmed)SaveBinding(confirmed);Bound=confirmed;
    }
   }catch(Exception e)when(!life.IsCancellationRequested){Diagnostic=e.GetType().Name;client?.Dispose();client=null;}
   await Task.Delay(2000,life.Token);
  }}catch(OperationCanceledException){}finally{client?.Dispose();lock(entries)entries.TryRemove(endpoint.Key,out _);}}
  public void Dispose(){life.Cancel();client?.Dispose();}
 }
#if TESTING
 internal static void VerifyReadBoundary(Endpoint endpoint,string[] anchors){
  var profile=Profiles().Single(p=>p.host==endpoint.host&&p.port==endpoint.port);bool denied=false;try{using var rejected=Connect(profile with{hostKey="wrong-key"});}catch{denied=true;}if(!denied)throw new Exception("Changed SSH host key accepted");
  using var client=Connect(profile);
  foreach(var request in new[]{new{connection="192.0.2.1 1 192.0.2.2 22",anchors=anchors},new{connection=endpoint.Connection,anchors=new[]{"unmatched isolated history identity "+Guid.NewGuid()}}}){
   using var command=client.CreateCommand("python3 -c "+Quote(Script)+" "+Quote(JsonSerializer.Serialize(request)));command.CommandTimeout=TimeSpan.FromSeconds(20);using var doc=JsonDocument.Parse(command.Execute());if(command.ExitStatus!=0||doc.RootElement.Bool("available")||doc.RootElement.TryGetProperty("records",out _))throw new Exception("Unverified remote history disclosed");
  }
 }
#endif
 internal static string Quote(string value)=>"'"+value.Replace("'","'\"'\"'")+"'";
 [StructLayout(LayoutKind.Sequential)]struct TcpRow{internal uint state,local,localPort,remote,remotePort,pid;}
 [DllImport("iphlpapi.dll",SetLastError=true)]static extern uint GetExtendedTcpTable(IntPtr table,ref int length,bool sorted,int family,int tableClass,uint reserved);
 internal static IEnumerable<Endpoint> Endpoints(int[] peers){
  var ssh=new Dictionary<int,long>();foreach(int id in peers)try{using var p=Process.GetProcessById(id);if(p.ProcessName.Equals("ssh",StringComparison.OrdinalIgnoreCase))ssh[id]=p.StartTime.ToUniversalTime().Ticks;}catch{}
  if(ssh.Count==0)yield break;int length=0;GetExtendedTcpTable(IntPtr.Zero,ref length,false,2,5,0);if(length<=0)yield break;IntPtr table=Marshal.AllocHGlobal(length);
  try{if(GetExtendedTcpTable(table,ref length,false,2,5,0)!=0)yield break;int count=Marshal.ReadInt32(table),size=Marshal.SizeOf<TcpRow>();if(count<0||count>(length-4)/size)yield break;
   for(int i=0;i<count;i++){var row=Marshal.PtrToStructure<TcpRow>(table+4+i*size);if(row.state!=5||!ssh.TryGetValue((int)row.pid,out long started))continue;int Port(uint value){var b=BitConverter.GetBytes(value);return b[0]*256+b[1];}yield return new((int)row.pid,started,new IPAddress(row.local).ToString(),Port(row.localPort),new IPAddress(row.remote).ToString(),Port(row.remotePort));}
  }finally{Marshal.FreeHGlobal(table);}
 }
 internal const string Script="""
import os,sys,json,pathlib,re
request=json.loads(sys.argv[1]); connection=request['connection']; active=[]; writers=set(); answer_evidence=(0,0)
def identity(p):
    return p.name+':'+(p/'stat').read_text().rsplit(')',1)[1].split()[19]
def normal(t):
    return ''.join(t.split())
def answer_match(text,groups):
    global answer_evidence
    original=normal(re.sub(r'[*`_]', '', text))
    for parts in groups:
        at=hits=total=0
        for part in dict.fromkeys(parts):
            if len(part)<24:continue
            found=original.find(part,at)
            if found<0:continue
            at=found+len(part);hits+=1;total+=len(part)
        if total>answer_evidence[1]:answer_evidence=(hits,total)
        # Same substantive-prose threshold used by the local original-record matcher.
        # Every fragment must occur in order inside ONE original assistant message.
        if hits>=2 and total>=64 or hits==1 and total>=80:return True
    return False
for p in pathlib.Path('/proc').iterdir():
    if not p.name.isdigit(): continue
    try:
        if p.stat().st_uid!=os.getuid() or (p/'comm').read_text().strip()!='codex': continue
        cmd=(p/'cmdline').read_bytes().decode().split('\0'); env=(p/'environ').read_bytes().split(b'\0')
        if 'app-server' not in cmd and b'SSH_CONNECTION='+connection.encode() in env:
            active.append((identity(p),os.readlink(p/'cwd'),cmd))
        for fd in (p/'fd').iterdir():
            try:
                path=os.readlink(fd)
                # Only actual open rollout descriptors from this user's Codex processes.
                if '/.codex/sessions/' in path and pathlib.Path(path).name.startswith('rollout-') and path.endswith('.jsonl'):
                    writers.add(path)
            except OSError: pass
    except (OSError,ValueError,UnicodeError): pass
if len(active)!=1:
    print(json.dumps({'available':False,'reason':'remote terminal identity is ambiguous or closed'}));sys.exit()
tui,cwd,cmd=active[0]; candidates=[]; original_count=0
for path in sorted(writers):
    try:
        with open(path,'rb') as f: meta=json.loads(f.readline()).get('payload',{})
        if meta.get('source')!='cli' or meta.get('cwd')!=cwd:continue
        st=os.stat(path); candidates.append((path,meta,str(st.st_dev)+':'+str(st.st_ino)))
    except (OSError,ValueError):pass
explicit=[x for x in cmd if re.fullmatch(r'[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}',x)]
if len(explicit)==1:candidates=[x for x in candidates if x[1].get('id')==explicit[0]]
original_count=len(candidates)
anchors=[normal(x) for x in request.get('anchors',[]) if len(normal(x))>=8]
answer_groups=request.get('answers',[]) if not anchors else []
known=request.get('tui')==tui and request.get('path')
# Retain a verified binding only while the same TUI has the same sole open user record.
if not (len(candidates)==1 and known==candidates[0][0]) and len(explicit)!=1:
    matched=[]
    for candidate in candidates:
        hit=False
        if anchors or answer_groups:
            with open(candidate[0],'rb') as f:
                for line in f:
                    try:
                        item=json.loads(line); p=item.get('payload',{})
                        if item.get('type')=='response_item' and p.get('type')=='message':
                            if p.get('role')=='user':
                                text='\n'.join(x.get('text','') for x in p.get('content',[]) if x.get('type')=='input_text')
                                if normal(text) in anchors:hit=True;break
                            elif p.get('role')=='assistant' and p.get('phase') in ('final','final_answer','commentary'):
                                text='\n'.join(x.get('text','') for x in p.get('content',[]) if x.get('type') in ('output_text','text'))
                                if answer_match(text,answer_groups):hit=True;break
                    except (ValueError,TypeError):pass
        if hit:matched.append(candidate)
    candidates=matched
if len(candidates)!=1:
    print(json.dumps({'available':False,'reason':'conversation identity is unconfirmed; writers='+str(len(writers))+' candidates='+str(original_count)+' matched='+str(len(candidates))+' anchors='+str([len(x) for x in anchors])+' answerEvidence='+str(answer_evidence)}));sys.exit()
path,meta,inode=candidates[0]; stamp=tui+':'+inode+':'+meta['id']; start=request.get('offset',0) if request.get('stamp')==stamp else 0
if request.get('validateOnly'):
    ok=request.get('stamp')==stamp and request.get('path')==path and request.get('tui')==tui
    print(json.dumps({'available':ok,'stamp':stamp if ok else '', 'id':meta['id'] if ok else ''}));sys.exit()
if start<0 or os.stat(path).st_size<start:start=0
records=[]
with open(path,'rb') as f:
    f.seek(start)
    while True:
        begin=f.tell(); line=f.readline()
        if not line:break
        if not line.endswith(b'\n'):f.seek(begin);break
        try:item=json.loads(line)
        except ValueError:continue
        p=item.get('payload',{}); kind=item.get('type')
        if kind=='session_meta':
            records.append({'type':kind,'payload':{'id':p.get('id','')}})
        elif kind=='event_msg' and p.get('type') in ('task_started','task_complete'):
            records.append({'type':kind,'payload':{'type':p['type'],'turn_id':p.get('turn_id','')}})
        elif kind=='event_msg' and p.get('type')=='item_completed' and p.get('item',{}).get('delivery')=='async':
            q=p['item']
            records.append({'type':kind,'payload':{'type':'item_completed','turn_id':p.get('turn_id',''),'item':{'id':q.get('id',''),'delivery':'async','questions':[{'title':x.get('title',''),'options':x.get('options',[])} for x in q.get('questions',[])]}}})
        elif kind=='response_item' and p.get('type')=='message' and (p.get('role')=='user' or p.get('role')=='assistant' and p.get('phase') in ('commentary','final_answer','final')):
            # No system instructions, tools, images or credentials from unrelated sources.
            records.append({'type':kind,'payload':{'type':'message','role':p['role'],'phase':p.get('phase',''),'content':[x for x in p.get('content',[]) if x.get('type') in ('input_text','output_text','text')]}})
    end=f.tell()
print(json.dumps({'available':True,'path':path,'tui':tui,'stamp':stamp,'start':start,'offset':end,'records':records},ensure_ascii=False))
""";
}
