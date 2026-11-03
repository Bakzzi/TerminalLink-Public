using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace TerminalLink;

internal static class ConsoleTitles {
 internal static async Task<List<ConsoleTarget>> Read(List<ConsoleTarget> targets,CancellationToken cancellation){
  if(targets.Count==0)return targets;
  // One short-lived read-only worker inspects all allowed consoles without selecting a PC tab.
  var start=new ProcessStartInfo(Environment.ProcessPath!){
   UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,
   RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,
   StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8
  };
  start.ArgumentList.Add("--console-titles");
  Process? worker=null;
  using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(3000);
  try{
   worker=Process.Start(start)??throw new IOException("Title worker failed.");
   _=worker.StandardError.ReadToEndAsync();
   string request=JsonSerializer.Serialize(targets.Take(256).Select(t=>new{id=t.Id,pid=t.Pid,started=t.Started}));
   await worker.StandardInput.WriteLineAsync(request.AsMemory(),timeout.Token);
   await worker.StandardInput.FlushAsync(timeout.Token);worker.StandardInput.Close();
   string? line=await worker.StandardOutput.ReadLineAsync(timeout.Token);
   if(line==null||line.Length>2*1024*1024)return targets;
   var titles=JsonSerializer.Deserialize<Dictionary<string,string>>(line);
   if(titles==null)return targets;
   return targets.Select(t=>titles.TryGetValue(t.Id,out string? title)&&!string.IsNullOrWhiteSpace(title)?t with{Name=title}:t).ToList();
  }catch when(!cancellation.IsCancellationRequested){return targets;}
  finally{
   if(worker!=null){try{if(!worker.HasExited)worker.Kill();}catch{}worker.Dispose();}
  }
 }
}