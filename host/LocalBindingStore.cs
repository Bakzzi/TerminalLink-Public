using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace TerminalLink;
// Only a verified display link, never a transcript or authority to send input.
internal sealed class LocalBindingStore {
 sealed record Link(string identity,string daemon,string thread,long sequence,string lastHash);
 readonly string directory;readonly Dictionary<string,Link> known=new();readonly Dictionary<string,ConversationMessage> lastSeen=new();
 static readonly byte[] entropy=Encoding.UTF8.GetBytes("TerminalLink.VerifiedDaemonRecordLink.v1");
 internal LocalBindingStore(string directory){this.directory=directory;}
 string PathFor(string identity)=>Path.Combine(directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))+".dpapi");
 static string Hash(ConversationMessage message)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(message)));
 internal void Remember(string identity,string daemon,Conversation conversation){
  if(identity.Length==0||daemon.Length==0||conversation.messages.Length==0)return;
  var last=conversation.messages[^1];if(known.TryGetValue(identity,out var link)&&link.daemon==daemon&&link.thread==conversation.id&&lastSeen.TryGetValue(identity,out var previous)&&ReferenceEquals(previous,last))return;var value=new Link(identity,daemon,conversation.id,last.sequence,Hash(last));
  if(known.TryGetValue(identity,out var prior)&&prior==value){lastSeen[identity]=last;return;}
  byte[]? plain=null;string temp="";
  try{
   Directory.CreateDirectory(directory);plain=JsonSerializer.SerializeToUtf8Bytes(value);
   byte[] encrypted=ProtectedData.Protect(plain,entropy,DataProtectionScope.CurrentUser);
   string path=PathFor(identity);temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(temp,encrypted);File.Move(temp,path,true);known[identity]=value;lastSeen[identity]=last;
  }catch{/* Disk failure must not discard the verified live conversation. */}
  finally{if(plain!=null)CryptographicOperations.ZeroMemory(plain);if(temp.Length>0)try{File.Delete(temp);}catch{}}
 }
 internal Conversation? Restore(string identity,string daemon,IEnumerable<Conversation> verified){
  try{
   if(!known.TryGetValue(identity,out var value)){
    byte[] plain=ProtectedData.Unprotect(File.ReadAllBytes(PathFor(identity)),entropy,DataProtectionScope.CurrentUser);
    try{value=JsonSerializer.Deserialize<Link>(plain);if(value==null)return null;}finally{CryptographicOperations.ZeroMemory(plain);}
    known[identity]=value;
   }
   if(value.identity!=identity||value.daemon!=daemon)return null;
   var candidates=verified.Where(c=>c.id==value.thread).ToArray();if(candidates.Length!=1)return null;
   var candidate=candidates[0];var last=candidate.messages.LastOrDefault(m=>m.sequence<=value.sequence);
   if(last==null||last.sequence!=value.sequence||Hash(last)!=value.lastHash)return null;
   // Catalog ownership and the unchanged recorded prefix confirm continuity; return the complete current record.
   return candidate;
  }catch{return null;}
 }
 internal void Forget(string identity){known.Remove(identity);lastSeen.Remove(identity);try{File.Delete(PathFor(identity));}catch{}}
}
