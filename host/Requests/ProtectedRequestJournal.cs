using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TerminalLink.Requests;

// A corrupt/unreadable journal fails visibly; resetting it could repeat an answer.
// The owner holds a file lock for its entire lifetime, including across IPC calls.
internal sealed class ProtectedRequestJournal : IRequestJournal, IDisposable {
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TerminalLink.RequestJournal.v1");
    readonly string path;
    readonly FileStream ownership;
    readonly object gate=new(); bool disposed;
    internal ProtectedRequestJournal(string directory) {
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "requests-v1.dpapi");
        ownership = new FileStream(Path.Combine(directory, "owner.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    public RequestDocument? Load() {
        if (!File.Exists(path)) return null;
        byte[] plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<RequestDocument>(plain) ?? throw new InvalidDataException("request-journal-empty"); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Save(RequestDocument document) {
        lock(gate){
        if(disposed)throw new IOException("request-journal-closed");
        byte[] plain = JsonSerializer.SerializeToUtf8Bytes(document);
        string temporary = path + ".tmp";
        try {
            byte[] encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                file.Write(encrypted); file.Flush(true);
            }
            File.Move(temporary, path, true);
        } finally { CryptographicOperations.ZeroMemory(plain); if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public void Dispose() {lock(gate){disposed=true;ownership.Dispose();}}
}
