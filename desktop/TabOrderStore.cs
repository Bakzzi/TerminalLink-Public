using System.Text.Json;

namespace TerminalLink;

// Presentation order only. Never use this file to choose an input destination.
internal sealed class TabOrderStore {
 readonly string? path;
 string[] order=Array.Empty<string>();
 internal TabOrderStore(string? path){
  this.path=path;
  try{if(path!=null&&File.Exists(path)&&new FileInfo(path).Length<=1048576)order=(JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))??Array.Empty<string>()).Where(k=>!string.IsNullOrEmpty(k)).Distinct().ToArray();}
  catch(IOException){}catch(UnauthorizedAccessException){}catch(JsonException){}
 }
 internal static string Key(DesktopTab tab)=>$"{tab.Target.Pid}:{tab.Target.Started}";
 internal DesktopTab[] Arrange(IEnumerable<DesktopTab> tabs){
  var ranks=order.Select((key,index)=>(key,index)).ToDictionary(x=>x.key,x=>x.index);
  return tabs.OrderBy(t=>ranks.GetValueOrDefault(Key(t),int.MaxValue)).ToArray();
 }
 internal void Save(IEnumerable<DesktopTab> tabs){
  order=tabs.Select(Key).Distinct().ToArray();if(path==null)return;
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(order));File.Move(temp,path,true);}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
