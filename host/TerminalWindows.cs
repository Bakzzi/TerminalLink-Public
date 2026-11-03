using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
namespace TerminalLink;

// All discovery stays on the PC. Only changes to the session catalog are sent to the phone.
internal static class TerminalWindows {
 static readonly object gate=new();
 static List<TerminalWindow> cached=new();static long stamp=-10000;
 static readonly Dictionary<(int,long),bool> owners=new();
 static readonly string user=WindowsIdentity.GetCurrent().User!.Value;
 static readonly int session=Process.GetCurrentProcess().SessionId;
 [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 internal static bool SameUser(Process process){
  try{
   var key=(process.Id,process.StartTime.ToUniversalTime().Ticks);
   lock(gate){if(owners.TryGetValue(key,out bool found))return found;}
   bool same=false;
   if(process.SessionId==session&&OpenProcessToken(process.Handle,8,out var token)){
    try{using var identity=new WindowsIdentity(token);same=identity.User?.Value==user;}finally{CloseHandle(token);}
   }
   lock(gate){if(owners.Count>512)owners.Clear();owners[key]=same;}return same;
  }catch{return false;}
 }
 internal static List<TerminalWindow> Read(bool force=false){
  lock(gate){
   if(force||Environment.TickCount64-stamp>=1500){cached=Native.Windows();stamp=Environment.TickCount64;}
   return cached.ToList();
  }
 }
 internal static List<TerminalWindow> Groups(IEnumerable<TerminalWindow> source)=>source
  .GroupBy(w=>(w.Pid,w.Started)).Select(g=>g.OrderBy(w=>w.Handle).First()).OrderBy(w=>w.Pid).ToList();
 internal static TerminalWindow Resolve(TerminalWindow original){
  if(Native.Valid(original))return original;
  return Read().FirstOrDefault(w=>w.Pid==original.Pid&&w.Started==original.Started)??original;
 }
 internal static string Key(TerminalWindow w)=>w.Pid+":"+w.Started;
}

internal sealed class WindowShareSelection {
 readonly HashSet<string> enabled=new(),seen=new();
 internal bool Automatic;
 internal void Check(TerminalWindow window,bool allow){string key=TerminalWindows.Key(window);seen.Add(key);if(allow)enabled.Add(key);else enabled.Remove(key);}
 internal void EnableAll(IEnumerable<TerminalWindow> windows){foreach(var w in windows)Check(w,true);}
 internal List<TerminalWindow> Update(List<TerminalWindow> windows){
  var live=windows.Select(TerminalWindows.Key).ToHashSet();
  enabled.RemoveWhere(k=>!live.Contains(k));seen.RemoveWhere(k=>!live.Contains(k));
  foreach(var window in windows){var key=TerminalWindows.Key(window);if(seen.Add(key)&&Automatic)enabled.Add(key);}
  return windows.Where(w=>enabled.Contains(TerminalWindows.Key(w))).ToList();
 }
}