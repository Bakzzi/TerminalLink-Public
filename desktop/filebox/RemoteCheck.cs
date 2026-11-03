using System.Text;
using System.Text.Json;

namespace TerminalLink;

internal static class RemoteCheck {
 internal static void Run(){
  var profiles=Bridge.Profiles();if(profiles.Length!=1)throw new IOException("Remote check requires exactly one registered profile");var p=profiles[0];
  using var ssh=Bridge.ConnectShell(p);using var sftp=Bridge.Connect(p);
  string Run(string script,int seconds=25){using var c=ssh.CreateCommand("python3 -c "+Bridge.Quote(script));c.CommandTimeout=TimeSpan.FromSeconds(seconds);string output=c.Execute();if(c.ExitStatus!=0)throw new IOException("Remote fixture failed: "+c.Error);return output;}
  using var unit=JsonDocument.Parse(Run(Bridge.Resource("remote_check.py")));
  string create="""
import importlib.util,json,tempfile,uuid
from pathlib import Path
spec=importlib.util.spec_from_file_location('fb',Path.home()/'.local/share/terminallink-filebox-v1/publish.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
with tempfile.TemporaryDirectory(prefix='terminallink-filebox-smoke-') as temp:
    p=Path(temp)/'TerminalLink-upload-check.txt'
    p.write_text('TerminalLink encrypted SSH filebox verification '+uuid.uuid4().hex,encoding='utf-8')
    rid=m.publish([str(p)])
    print(json.dumps({'id':rid,'manifest':m.read_json(m.request_path(rid)/'manifest.json')}))
""";
  using var created=JsonDocument.Parse(Run(create));string id=created.RootElement.GetProperty("id").GetString()!;
  var req=created.RootElement.GetProperty("manifest").Deserialize<UploadRequest>(Bridge.Json)!;
  var store=new SharedFiles();SharedFile? shared=null;
  try{
   Bridge.Pass(sftp,p);
   string status="import importlib.util,json;from pathlib import Path;s=importlib.util.spec_from_file_location('fb',Path.home()/'.local/share/terminallink-filebox-v1/publish.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m);print(json.dumps(m.status('"+id+"',0)))";
   var receipt=JsonSerializer.Deserialize<Receipt>(Run(status),Bridge.Json)??throw new IOException("Missing live receipt");
   if(!receipt.ok||receipt.request!=id||receipt.files.Length!=1)throw new IOException("Remote success receipt missing");shared=receipt.files[0];Bridge.VerifyCopy(store,shared,req.files[0]);
   using var download=new SharedDownload(store);
   var listed=JsonSerializer.SerializeToElement(download.Process(JsonSerializer.SerializeToElement(new{op="sharedList",request=Guid.NewGuid().ToString()})));
   if(!listed.GetProperty("files").EnumerateArray().Any(x=>x.GetProperty("id").GetString()==shared.id))throw new IOException("Mobile shared-file protocol did not list the uploaded file");
   int count=store.List().Length;Bridge.Pass(sftp,p);if(store.List().Length!=count)throw new IOException("Retry duplicated a confirmed file");
   Bridge.Atomic(Path.Combine(AppContext.BaseDirectory,"filebox-remote-checks.json"),new{ok=true,helperPassed=unit.RootElement.GetProperty("passed").GetInt32(),actualSshUpload=true,hostKeyPinned=true,pcHashVerified=true,mobileListProtocol=true,retryNoDuplicate=true,expiresAfterHours=24,source=typeof(Bridge).Assembly.GetName().Version?.ToString()});
  }finally{
   if(shared==null){
    string journal=Path.Combine(Bridge.Root,"receipts",p.Key,id+".json");
    if(File.Exists(journal)){var pending=JsonSerializer.Deserialize<Journal>(File.ReadAllText(journal),Bridge.Json);if(pending?.files.Length==1)shared=pending.files[0];}
   }
   if(shared!=null){Bridge.VerifyCopy(store,shared,req.files[0]);store.Delete(shared.id);}
   // Delete only this invocation's synthetic request, after proving its generated filename/hash.
   string cleanup="""
import importlib.util,json,sys
from pathlib import Path
s=importlib.util.spec_from_file_location('fb',Path.home()/'.local/share/terminallink-filebox-v1/publish.py');m=importlib.util.module_from_spec(s);s.loader.exec_module(m)
r=json.loads(sys.argv[1]);p=m.request_path(r['id']);data=m.read_json(p/'manifest.json')
assert len(data['files'])==1 and data['files'][0]['name']=='TerminalLink-upload-check.txt' and data['files'][0]['sha256']==r['sha256']
paths=list(p.iterdir());assert all(f.name in ('manifest.json','receipt.json','0.bin') for f in paths)
for f in paths:m.private(f,False)
for f in paths:f.unlink()
p.rmdir()
""";
   using var c=ssh.CreateCommand("python3 -c "+Bridge.Quote(cleanup)+" "+Bridge.Quote(JsonSerializer.Serialize(new{id,sha256=req.files[0].sha256})));c.CommandTimeout=TimeSpan.FromSeconds(10);c.Execute();if(c.ExitStatus!=0)throw new IOException("Synthetic remote request cleanup failed");
  }
 }
}
