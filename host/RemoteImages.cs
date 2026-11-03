using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Renci.SshNet;
namespace TerminalLink;

// Transfer is separate; input stays with the original Windows Terminal SSH TUI.
internal static class RemoteImages {
 internal sealed record Photo(string name,long size,string sha256);
 internal sealed record Upload(RemoteCodexFeed.Binding binding,string directory,Photo[] photos){internal string[] Paths=>photos.Select(p=>directory+"/"+p.name).ToArray();}
 internal static string Caption(string text){text=text.Replace("\r\n","\n").Replace("\r","\n");if(text.Length>4096||text.Any(c=>char.IsControl(c)&&c!='\n'))throw new IOException("설명은 제어 문자 없이 4096자 이내로 입력하세요.");return text;}
 internal static void Validate(Upload upload){
  if(!Regex.IsMatch(upload.directory,@"\A/tmp/terminallink-attachments-[0-9]+/[a-f0-9]{32}\z")||upload.photos.Length is <1 or >4)throw new IOException("원격 사진 경로가 유효하지 않습니다.");
  for(int i=0;i<upload.photos.Length;i++){var p=upload.photos[i];if(p.name!=i+".png"||p.size is <1 or >8388608||!Regex.IsMatch(p.sha256,@"\A[0-9A-F]{64}\z"))throw new IOException("원격 사진 정보가 유효하지 않습니다.");}
 }
 internal static SftpClient ConnectSftp(RemoteCodexFeed.Profile profile){
  var info=new PasswordConnectionInfo(profile.host,profile.port,profile.user,profile.password){Timeout=TimeSpan.FromSeconds(6)};
  foreach(string key in info.HostKeyAlgorithms.Keys.ToArray())if(key!=profile.hostKeyType)info.HostKeyAlgorithms.Remove(key);
  var client=new SftpClient(info){OperationTimeout=TimeSpan.FromSeconds(12),BufferSize=32768};
  client.HostKeyReceived+=(_,e)=>e.CanTrust=Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=')==profile.hostKey;
  try{client.Connect();return client;}catch{client.Dispose();throw;}
 }
 internal static JsonElement Files(SshClient client,object request){
  using var command=client.CreateCommand("python3 -c "+RemoteCodexFeed.Quote(FileScript)+" "+RemoteCodexFeed.Quote(JsonSerializer.Serialize(request)));command.CommandTimeout=TimeSpan.FromSeconds(8);
  string output=command.Execute();if(command.ExitStatus!=0)throw new IOException("원격 사진 저장 공간 또는 파일 검증에 실패했습니다.");
  using var doc=JsonDocument.Parse(output);return doc.RootElement.Clone();
 }
 internal static Upload Transfer(ConsoleBridge bridge,string thread,string[] paths,string caption,CancellationToken token){
  Caption(caption);if(paths.Length is <1 or >4)throw new IOException("사진은 4개까지 보낼 수 있습니다.");
  var bound=RemoteCodexFeed.BoundTo(bridge.Latest??throw new IOException("터미널을 읽는 중입니다."),thread);
  var profile=RemoteCodexFeed.RequireProfile(bound,bridge.Latest!);
  using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(60));var ct=deadline.Token;
  Upload? upload=null;
  try{
   var photos=paths.Select((path,i)=>{using var file=File.OpenRead(path);return new Photo(i+".png",file.Length,Convert.ToHexString(SHA256.HashData(file)));}).ToArray();
   using var ssh=RemoteCodexFeed.Connect(profile);using var sftp=ConnectSftp(profile);
   using var cancel=ct.Register(()=>{try{sftp.Dispose();}catch{}try{ssh.Dispose();}catch{}});
   ct.ThrowIfCancellationRequested();RemoteCodexFeed.Verify(ssh,bound);
   string id=Guid.NewGuid().ToString("N");string dir=Files(ssh,new{op="prepare",id,photos}).Str("directory");upload=new(bound,dir,photos);Validate(upload);
   for(int i=0;i<paths.Length;i++){ct.ThrowIfCancellationRequested();using var input=File.OpenRead(paths[i]);sftp.UploadFile(input,upload.Paths[i],false);sftp.ChangePermissions(upload.Paths[i],600);}
   Files(ssh,new{op="verify",directory=upload.directory,photos});
   ct.ThrowIfCancellationRequested();RemoteCodexFeed.RequireProfile(bound,bridge.Latest!);RemoteCodexFeed.Verify(ssh,bound);return upload;
  }catch(Exception e){
   if(upload!=null)try{using var cleanup=RemoteCodexFeed.Connect(profile);Files(cleanup,new{op="remove",directory=upload.directory,photos=upload.photos});}catch{}
   if(token.IsCancellationRequested)throw new OperationCanceledException(token);
   throw new IOException(ct.IsCancellationRequested?"사진 업로드 시간이 초과됐습니다. 터미널에는 입력하지 않았습니다.":"원격 사진 업로드에 실패했습니다. 터미널에는 입력하지 않았습니다.",e);
  }
 }
 // No Enter until identity, native image markers and exact caption agree.
 internal static void Deliver(Upload upload,string caption,Action verify,Action<int,string> draft,Action<string> paste,Action enter){
  Validate(upload);caption=Caption(caption);int count=0;
  foreach(string path in upload.Paths){verify();draft(count,"");paste(path);draft(++count,"");}
  if(caption.Length>0){verify();draft(count,"");paste(caption);}
  draft(count,caption);verify();draft(count,caption);enter();
 }
 internal static object Input(IntPtr read,IntPtr write,JsonElement message){
  string request=message.Str("request");bool touched=false;long deadline=Environment.TickCount64+25000;
  try{
   var upload=message.GetProperty("upload").Deserialize<Upload>()??throw new IOException("원격 첨부 정보가 없습니다.");Validate(upload);
   if(upload.binding.thread!=message.Str("thread"))throw new IOException("첨부 대상 대화가 다릅니다.");
   var profile=RemoteCodexFeed.RequireProfile(upload.binding,ConsoleNative.Read(read));using var client=RemoteCodexFeed.Connect(profile);
   Files(client,new{op="verify",directory=upload.directory,photos=upload.photos});
   void Verify(){if(Environment.TickCount64>deadline)throw new IOException("원격 입력 확인 시간이 초과됐습니다.");RemoteCodexFeed.RequireProfile(upload.binding,ConsoleNative.Read(read));RemoteCodexFeed.Verify(client,upload.binding);if(Environment.TickCount64>deadline)throw new IOException("원격 입력 확인 시간이 초과됐습니다.");}
   void Draft(int count,string caption){long end=Environment.TickCount64+(touched?2000:0);while(true)try{if(count==0&&caption.Length==0)NotificationInput.EmptyMain(ConsoleNative.Read(read));else ImageInput.CheckDraft(read,count,caption);return;}catch(IOException)when(Environment.TickCount64<end){Thread.Sleep(100);}}
   Deliver(upload,message.Str("text"),Verify,Draft,text=>{touched=true;ConsoleNative.WriteRemotePaste(write,text);ConsoleNative.SettleInput(write);},()=>{if(Environment.TickCount64>deadline)throw new IOException("원격 입력 확인 시간이 초과됐습니다.");ConsoleNative.Write(write,JsonSerializer.SerializeToElement(new{op="key",key="ENTER"}));});
   return new{notificationResult=request,ok=true,message="원격 Codex에 사진과 메시지를 보냈습니다."};
  }catch(Exception){return new{notificationResult=request,ok=false,message=touched?"원격 첨부 입력을 확인하지 못해 전송을 멈췄습니다. 자동 재전송은 하지 않았습니다. 터미널 입력칸을 확인하세요.":"원격 대화 또는 빈 입력칸을 확인하지 못했습니다. 원래 터미널에서 현재 Codex 입력칸을 확인하세요."};}
 }
 // Same-user, non-symlink private directories. Copies expire after seven days on next upload.
 internal const string FileScript="""
import os,sys,json,re,stat,hashlib,time
r=json.loads(sys.argv[1]);uid=os.getuid();root='/tmp/terminallink-attachments-'+str(uid)
flags=os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW
def private(fd):
    s=os.fstat(fd)
    if s.st_uid!=uid or stat.S_IMODE(s.st_mode)!=0o700:raise ValueError('unsafe directory')
def child(name):
    if not re.fullmatch('[a-f0-9]{32}',name):raise ValueError('invalid directory')
    fd=os.open(name,flags,dir_fd=base);private(fd);return fd
def photos():
    p=r['photos']
    if not 1<=len(p)<=4:raise ValueError('count')
    for i,x in enumerate(p):
        if x['name']!=str(i)+'.png' or not 0<x['size']<=8388608 or not re.fullmatch('[0-9A-F]{64}',x['sha256']):raise ValueError('photo')
    return p
def remove(name):
    fd=child(name)
    try:
        names=os.listdir(fd)
        if any(not re.fullmatch('[0-3]\.png',n) for n in names):raise ValueError('unexpected file')
        for n in names:
            s=os.stat(n,dir_fd=fd,follow_symlinks=False)
            if not stat.S_ISREG(s.st_mode) or s.st_uid!=uid or s.st_nlink!=1:raise ValueError('unsafe file')
        for n in names:os.unlink(n,dir_fd=fd)
    finally:os.close(fd)
    os.rmdir(name,dir_fd=base)
try:os.mkdir(root,0o700)
except FileExistsError:pass
base=os.open(root,flags);private(base)
try:
    if r['op']=='prepare':
        p=photos();total=0
        for n in os.listdir(base):
            fd=child(n)
            try:
                stale=os.fstat(fd).st_mtime<time.time()-7*86400
                for f in os.listdir(fd):
                    s=os.stat(f,dir_fd=fd,follow_symlinks=False)
                    if not re.fullmatch('[0-3]\.png',f) or not stat.S_ISREG(s.st_mode) or s.st_uid!=uid or s.st_nlink!=1:raise ValueError('unsafe stored file')
                    if not stale:total+=s.st_size
            finally:os.close(fd)
            if stale:remove(n)
        if total+sum(x['size'] for x in p)>536870912:raise ValueError('quota')
        name=r['id']
        if not re.fullmatch('[a-f0-9]{32}',name):raise ValueError('id')
        os.mkdir(name,0o700,dir_fd=base);print(json.dumps({'directory':root+'/'+name}))
    else:
        directory=r['directory'];name=directory.removeprefix(root+'/')
        if directory!=root+'/'+name:raise ValueError('path')
        if r['op']=='remove':remove(name);print('{}')
        elif r['op']=='verify':
            p=photos();fd=child(name)
            try:
                if sorted(os.listdir(fd))!=sorted(x['name'] for x in p):raise ValueError('contents')
                for x in p:
                    f=os.open(x['name'],os.O_RDONLY|os.O_NOFOLLOW,dir_fd=fd)
                    with os.fdopen(f,'rb') as stream:
                        s=os.fstat(stream.fileno())
                        if not stat.S_ISREG(s.st_mode) or s.st_uid!=uid or s.st_nlink!=1 or stat.S_IMODE(s.st_mode)!=0o600 or s.st_size!=x['size']:raise ValueError('file identity')
                        h=hashlib.sha256()
                        while True:
                            b=stream.read(32768)
                            if not b:break
                            h.update(b)
                        if h.hexdigest().upper()!=x['sha256']:raise ValueError('hash')
                print('{}')
            finally:os.close(fd)
        else:raise ValueError('operation')
finally:os.close(base)
""";
}
