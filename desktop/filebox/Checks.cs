using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TerminalLink;

internal static class Checks {
 internal static void Run(){
  string root=Path.Combine(Path.GetTempPath(),"TerminalLink-filebox-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  int passed=0;var results=new List<string>();
  void Check(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;results.Add(name);}
  void Reject(Action call,string name){bool failed=false;try{call();}catch(IOException){failed=true;}Check(failed,name);}
  try{
   byte[] bytes=Encoding.UTF8.GetBytes("SSH 파일함 테스트\nimmutable payload\n");string sha=Bridge.Hash(bytes);long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
   var spec=new FileSpec(0,"result 한글.txt",bytes.Length,sha);var req=new UploadRequest(1,Guid.NewGuid().ToString("N"),now,[spec]);
   Bridge.Validate(req,now);Check(true,"valid unicode filename");
   Reject(()=>Bridge.Validate(req with{id="../bad"},now),"reject traversal ID");
   Reject(()=>Bridge.Validate(req with{created=now-86400001},now),"reject expired queue");
   Reject(()=>Bridge.Validate(req with{created=now+300001},now),"reject future queue");
   foreach(string name in new[]{"../file","C:\\secret","..","foo:bar","NUL.txt","trailing.","trailing "})Reject(()=>Bridge.Validate(req with{files=[spec with{name=name}]},now),"reject name "+name);
   Reject(()=>Bridge.Validate(req with{files=[spec with{index=1}]},now),"reject reordered index");
   Reject(()=>Bridge.Validate(req with{files=[spec with{size=SharedFiles.MaxFile+1}]},now),"reject oversized file");
   Reject(()=>Bridge.Validate(req with{files=[spec with{sha256="bad"}]},now),"reject malformed hash");
   Reject(()=>Bridge.Validate(req with{files=[]},now),"reject empty request");
   Reject(()=>Bridge.Validate(req with{files=Enumerable.Range(0,5).Select(i=>spec with{index=i,size=SharedFiles.MaxFile}).ToArray()},now),"reject oversized batch");
   using(var s=new MemoryStream(bytes))using(var o=new MemoryStream()){Bridge.CopyVerified(s,o,spec);Check(o.ToArray().SequenceEqual(bytes),"stream exact bytes");}
   Reject(()=>{using var s=new MemoryStream(bytes[..^1]);using var o=new MemoryStream();Bridge.CopyVerified(s,o,spec);},"reject truncated stream");
   Reject(()=>{using var s=new MemoryStream(bytes.Concat(new byte[]{0}).ToArray());using var o=new MemoryStream();Bridge.CopyVerified(s,o,spec);},"reject oversized stream");
   Reject(()=>{using var s=new MemoryStream(new byte[bytes.Length]);using var o=new MemoryStream();Bridge.CopyVerified(s,o,spec);},"reject corrupt stream");
   var store=new SharedFiles(Path.Combine(root,"box"));string journal=Path.Combine(root,"journal.json"),staging=Path.Combine(root,"staging");int downloads=0;
   void Download(FileSpec f,string p){downloads++;File.WriteAllBytes(p,bytes);}
   var receipt=Bridge.Import(req,"manifest-one",journal,store,staging,Download,()=>false);
   Check(receipt.ok&&receipt.request==req.id&&receipt.files.Length==1,"receipt identifies exact request");
   Check(receipt.files[0].sha256==sha&&receipt.files[0].size==bytes.Length,"receipt preserves size and hash");
   Check(receipt.files[0].expires-receipt.files[0].created==86400000,"24 hour publication lifetime");
   Check(store.List().Length==1&&File.ReadAllBytes(store.Data(receipt.files[0].id)).SequenceEqual(bytes),"actual shared store contains verified bytes");
   var repeated=Bridge.Import(req,"manifest-one",journal,store,staging,Download,()=>false);
   Check(repeated.files[0].id==receipt.files[0].id&&downloads==1&&store.List().Length==1,"retry does not republish or redownload");
   Reject(()=>Bridge.Import(req,"different-manifest",journal,store,staging,Download,()=>false),"same request cannot change its manifest");
   Check(!Directory.EnumerateFileSystemEntries(staging).Any(),"temporary downloads cleaned");
   string crash=Path.Combine(root,"crash.json");Bridge.Atomic(crash,new Journal("crash",receipt.files[0].created-1,new SharedFile?[1]));
   var recovered=Bridge.Import(req,"crash",crash,store,staging,Download,()=>false);
   Check(recovered.files[0].id==receipt.files[0].id&&downloads==1,"recover publish-before-journal crash without duplication");
   Reject(()=>Bridge.Import(req,"cancel",Path.Combine(root,"cancel.json"),store,staging,Download,()=>true),"cancel prevents publication");
   var bad=req with{id=Guid.NewGuid().ToString("N"),files=[spec with{name="bad.txt"}]};
   Reject(()=>Bridge.Import(bad,"bad",Path.Combine(root,"bad.json"),store,staging,(f,p)=>File.WriteAllBytes(p,[1]),()=>false),"corrupt download never published");
   Check(store.List().Length==1,"failed download leaves box unchanged");
   var profile=new SshProfile("example.invalid",22,"example","not-used",Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('='),"ssh-ed25519");
   Check(Bridge.Pinned(profile,bytes)&&!Bridge.Pinned(profile,[1]),"host key pin rejects changed server key");
   var info=Bridge.Connection(profile);Check(info.HostKeyAlgorithms.Keys.SequenceEqual(new[]{"ssh-ed25519"}),"only pinned key algorithm enabled");
   Check(Bridge.Resource("SKILL.md").Contains("pending")&&Bridge.Resource("publish.py").Contains("receipt.json"),"helper and agent instructions embedded");
   Bridge.Atomic(Path.Combine(AppContext.BaseDirectory,"filebox-checks.json"),new{passed,failed=0,checks=results});
  }finally{
   // Exclusive per-run test directory, no user data and no links created by these tests.
   foreach(string f in Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories))File.Delete(f);
   foreach(string d in Directory.EnumerateDirectories(root,"*",SearchOption.AllDirectories).OrderByDescending(x=>x.Length))Directory.Delete(d,false);
   Directory.Delete(root,false);
  }
 }
}
