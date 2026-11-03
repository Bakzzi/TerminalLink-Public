using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
namespace TerminalLink;
class Fixture {
 static async Task Main(string[] args){
  if(AutomaticUpdates.Handle(args))return;
  if(args.Contains("--unit")){Unit();return;}
  string root=AutomaticUpdates.Install;Directory.CreateDirectory(root);
  string version=FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).FileVersion!;
  File.AppendAllText(Path.Combine(root,"started.txt"),version+Environment.NewLine);
  if(version.StartsWith("0.0.2")){
   if(File.Exists(Path.Combine(root,"fail-start")))return;
   AutomaticUpdates.Healthy();await Task.Delay(3000);return;
  }
  if(File.Exists(Path.Combine(root,"automatic-updates","last-result.json")))return;
  for(int n=0;n<25;n++){await AutomaticUpdates.Check(()=>!File.Exists(Path.Combine(root,"busy")),()=>"{\"fixture\":true}",()=>Environment.Exit(0),s=>File.AppendAllText(Path.Combine(root,"notice.txt"),s+Environment.NewLine));await Task.Delay(1000);}
 }
 static void Unit(){
  string root=AutomaticUpdates.Install;Directory.CreateDirectory(root);string manifest=Path.Combine(root,"unit.json");int count=0;
  void Check(bool value){if(!value)throw new Exception("Update check failed "+count);count++;}
  void Reject(Action action){try{action();}catch(IOException){count++;return;}throw new Exception("Unsafe update accepted");}
  var release=new AutomaticUpdates.Release("fixture","0.0.2.0",new string('A',64),10,new string('b',40));
  File.WriteAllText(manifest,JsonSerializer.Serialize(release));Check(AutomaticUpdates.Read(manifest,"fixture",new Version(0,0,1,0))==release);
  Reject(()=>AutomaticUpdates.Read(manifest,"desktop",new Version(0,0,1,0)));Reject(()=>AutomaticUpdates.Read(manifest,"fixture",new Version(0,0,2,0)));
  File.WriteAllText(manifest,JsonSerializer.Serialize(release with{sha256="../bad"}));Reject(()=>AutomaticUpdates.Read(manifest,"fixture",new Version(0,0,1,0)));
  string zip=Path.Combine(root,"bad.zip");using(var z=ZipFile.Open(zip,ZipArchiveMode.Create)){using var w=new StreamWriter(z.CreateEntry("../escaped.txt").Open());w.Write("bad");}
  Reject(()=>AutomaticUpdates.Extract(zip,Path.Combine(root,"extracted"),"Fixture.exe"));Check(!File.Exists(Path.Combine(root,"escaped.txt")));
  Reject(()=>AutomaticUpdates.VerifyBundle(zip,release));
  File.WriteAllText(Path.Combine(root,"unit-result.txt"),"PASS "+count+" update policy checks");
 }
}
