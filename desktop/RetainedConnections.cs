namespace TerminalLink;

// A tab switch releases idle readers immediately, but never tears down a pending send.
internal sealed class RetainedConnections<T> : IDisposable where T : class, IDisposable {
 sealed class Entry(string id,T value){internal readonly string Id=id;internal readonly T Value=value;internal int Users;}
 readonly object gate=new();readonly Dictionary<string,Entry> entries=new();string? selected;long selectionVersion;bool disposed;
 Entry Get(string id,Func<T> open){ObjectDisposedException.ThrowIf(disposed,this);if(!entries.TryGetValue(id,out var e))entries[id]=e=new(id,open());return e;}
 internal void Select(string id,long version,Func<T> open){T? retired=null;lock(gate){if(version<selectionVersion)return;Get(id,open);selectionVersion=version;var old=selected;selected=id;if(old!=null&&old!=id&&entries.TryGetValue(old,out var e)&&e.Users==0){entries.Remove(old);retired=e.Value;}}retired?.Dispose();}
 internal Lease Acquire(string id,Func<T> open){lock(gate){var e=Get(id,open);e.Users++;return new(e.Value,()=>Release(e));}}
 internal Lease? ReadSelected(string id){lock(gate){if(disposed||selected!=id||!entries.TryGetValue(id,out var e))return null;e.Users++;return new(e.Value,()=>Release(e));}}
 void Release(Entry e){T? retired=null;lock(gate){if(--e.Users==0&&(disposed||selected!=e.Id)){entries.Remove(e.Id);retired=e.Value;}}retired?.Dispose();}
 public void Dispose(){T[] retired;lock(gate){disposed=true;selected=null;retired=entries.Values.Where(e=>e.Users==0).Select(e=>e.Value).ToArray();foreach(var id in entries.Where(p=>p.Value.Users==0).Select(p=>p.Key).ToArray())entries.Remove(id);}foreach(var value in retired)value.Dispose();}
 internal sealed class Lease(T value,Action release) : IDisposable {Action? cleanup=release;internal T Value=>value;public void Dispose()=>Interlocked.Exchange(ref cleanup,null)?.Invoke();}
}
