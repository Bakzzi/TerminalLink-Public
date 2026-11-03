#if TESTING
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace TerminalLink;
internal static class SharedUploadChecks {
 internal static void Run(Action<bool,string> check){
  string root=Path.Combine(Path.GetTempPath(),"TerminalLink-upload-fixture-"+Guid.NewGuid().ToString("N"));
  var store=new SharedFiles(root);
  JsonElement Send(SharedUpload upload,object command)=>JsonSerializer.SerializeToElement(upload.Process(JsonSerializer.SerializeToElement(command)));
  string Req()=>Guid.NewGuid().ToString("D");
  try{
   using(var upload=new SharedUpload(store)){
    for(int i=0;i<4;i++){
     byte[] bytes=Encoding.UTF8.GetBytes("fixture photo "+i+" 한글");string sha=Convert.ToHexString(SHA256.HashData(bytes)),id=Req();
     var begin=Send(upload,new{op="sharedUploadBegin",request=Req(),upload=id,name="photo"+i+".png",size=bytes.Length,sha256=sha});
     var chunk=Send(upload,new{op="sharedUploadChunk",request=Req(),upload=id,offset=0,data=Convert.ToBase64String(bytes)});
     var done=Send(upload,new{op="sharedUploadEnd",request=Req(),upload=id});
     string saved=done.GetProperty("file").Str("id");var file=store.Find(saved);
     check(begin.Bool("ok")&&chunk.Bool("ok")&&done.Bool("ok")&&file!=null&&file.sha256==sha&&file.expires-file.created==86400000&&File.ReadAllBytes(store.Data(saved)).SequenceEqual(bytes),"Phone file "+(i+1)+" publishes exact bytes and expires after 24 hours");
    }
    check(store.List().Length==4,"Four uploaded files are independently listed for PC and mobile");
    string id2=Req(),sha2=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("ok")));
    check(Send(upload,new{op="sharedUploadBegin",request=Req(),upload=id2,name="fixture.txt",size=2,sha256=sha2}).Bool("ok"),"Start pending upload");
    check(!Send(upload,new{op="sharedUploadChunk",request=Req(),upload=Req(),offset=0,data="b2s="}).Bool("ok"),"Other upload token cannot modify current transfer");
    check(Send(upload,new{op="sharedUploadChunk",request=Req(),upload=id2,offset=0,data="b2s="}).Bool("ok")&&Send(upload,new{op="sharedUploadEnd",request=Req(),upload=id2}).Bool("ok"),"Rejected token preserves original transfer");
    string bad=Req();Send(upload,new{op="sharedUploadBegin",request=Req(),upload=bad,name="bad.bin",size=2,sha256=sha2});
    check(!Send(upload,new{op="sharedUploadChunk",request=Req(),upload=bad,offset=1,data="b2s="}).Bool("ok")&&store.List().Length==5,"Wrong offset cancels staged data without publishing");
    string corrupt=Req();Send(upload,new{op="sharedUploadBegin",request=Req(),upload=corrupt,name="bad.bin",size=2,sha256=sha2});Send(upload,new{op="sharedUploadChunk",request=Req(),upload=corrupt,offset=0,data="bm8="});
    check(!Send(upload,new{op="sharedUploadEnd",request=Req(),upload=corrupt}).Bool("ok")&&store.List().Length==5,"Hash mismatch never publishes a damaged file");
    string cancelled=Req();Send(upload,new{op="sharedUploadBegin",request=Req(),upload=cancelled,name="cancel.bin",size=2,sha256=sha2});
    check(Send(upload,new{op="sharedUploadCancel",request=Req(),upload=cancelled}).Bool("ok")&&store.List().Length==5,"Cancel does not publish partial data");
    check(!Send(upload,new{op="sharedUploadBegin",request=Req(),upload=Req(),name="oversize.bin",size=SharedFiles.MaxFile+1,sha256=sha2}).Bool("ok")&&!Directory.EnumerateDirectories(root,".incoming-*").Any(),"Oversized file is rejected before staging");
    string empty=Req(),emptyHash=Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
    check(Send(upload,new{op="sharedUploadBegin",request=Req(),upload=empty,name="empty.txt",size=0,sha256=emptyHash}).Bool("ok")&&Send(upload,new{op="sharedUploadEnd",request=Req(),upload=empty}).Bool("ok"),"Empty files can be uploaded");
    string incomplete=Req();Send(upload,new{op="sharedUploadBegin",request=Req(),upload=incomplete,name="partial.bin",size=2,sha256=sha2});
    store.CleanupIncoming(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+2*86400000L);
    check(Directory.EnumerateDirectories(root,".incoming-*").Count()==1,"Active upload is never removed by stale cleanup");
   }
   check(!Directory.EnumerateDirectories(root,".incoming-*").Any()&&store.List().Length==6,"Connection disposal removes only its incomplete staging file");
   string stale=Guid.NewGuid().ToString("N"),staleDir=store.Incoming(stale);Directory.CreateDirectory(staleDir);
   File.WriteAllText(Path.Combine(staleDir,"owner.json"),JsonSerializer.Serialize(new IncomingMarker("TerminalLinkFileBoxUpload",stale,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-2*86400000L)));
   File.WriteAllText(Path.Combine(staleDir,"lease.lock"),"");File.WriteAllText(Path.Combine(staleDir,"content.bin"),"fixture");File.SetLastWriteTimeUtc(Path.Combine(staleDir,"content.bin"),DateTime.UtcNow.AddDays(-2));
   store.CleanupIncoming(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());check(!Directory.Exists(staleDir),"Abandoned verified upload staging expires without touching shared files");
   string uncertain=Guid.NewGuid().ToString("N"),uncertainDir=store.Incoming(uncertain);Directory.CreateDirectory(uncertainDir);
   File.WriteAllText(Path.Combine(uncertainDir,"owner.json"),JsonSerializer.Serialize(new IncomingMarker("TerminalLinkFileBoxUpload",uncertain,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-2*86400000L)));
   File.WriteAllText(Path.Combine(uncertainDir,"lease.lock"),"");File.WriteAllText(Path.Combine(uncertainDir,"content.bin"),"fixture");File.WriteAllText(Path.Combine(uncertainDir,"unknown.txt"),"preserve");File.SetLastWriteTimeUtc(Path.Combine(uncertainDir,"content.bin"),DateTime.UtcNow.AddDays(-2));
   store.CleanupIncoming(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());check(File.Exists(Path.Combine(uncertainDir,"unknown.txt"))&&File.Exists(Path.Combine(uncertainDir,"content.bin")),"Unknown staged content is never deleted by automatic cleanup");
   File.Delete(Path.Combine(uncertainDir,"unknown.txt"));store.DiscardIncoming(uncertain);
  }finally{
   foreach(var f in store.List())store.Delete(f.id);
   string gate=Path.Combine(root,"publish.lock");if(File.Exists(gate))File.Delete(gate);if(Directory.Exists(root))Directory.Delete(root,false);
  }
 }
}
#endif
