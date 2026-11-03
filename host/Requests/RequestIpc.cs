using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TerminalLink.Requests;

internal sealed record RequestCommand(int Version, string Operation, string Instance = "", string SubmissionId = "", AnswerCommand? Answer = null, JsonElement? Native = null);
internal sealed record RequestReply(int Version, bool Ok, string Error, RequestSnapshot? Snapshot = null, Submission? Submission = null, JsonElement? Native = null);

internal static class RequestWire {
    internal const int MaxFrame = 4 * 1024 * 1024;
    internal static async Task Write<T>(Stream stream, T value, CancellationToken token) {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(value);
        if (data.Length > MaxFrame) throw new InvalidDataException("request-frame-too-large");
        byte[] size = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(size, data.Length);
        await stream.WriteAsync(size, token); await stream.WriteAsync(data, token); await stream.FlushAsync(token);
    }
    internal static async Task<T> Read<T>(Stream stream, CancellationToken token) {
        byte[] size = new byte[4]; await stream.ReadExactlyAsync(size, token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (length < 1 || length > MaxFrame) throw new InvalidDataException("request-frame-size");
        byte[] data = new byte[length]; await stream.ReadExactlyAsync(data, token);
        return JsonSerializer.Deserialize<T>(data) ?? throw new InvalidDataException("request-frame-empty");
    }
    // The same dispatcher is used by local IPC and the authenticated mobile transport.
    // The caller supplies its authorized instance resolver; no target comes from a title.
    internal static async Task<RequestReply> Dispatch(RequestCoordinator manager, RequestCommand command,
        Func<string, IRequestAdapter?> authorized, CancellationToken token) {
        if (command.Version != 1) return new(1, false, "unsupported-request-contract");
        try {
            string instance = command.Answer?.Key.Instance ?? command.Instance;
            var adapter = authorized(instance);
            if (adapter == null) return new(1, false, "request-instance-unavailable");
            return command.Operation switch {
                "snapshot" => new(1, true, "", Snapshot: manager.Snapshot(instance)),
                "submit" when command.Answer != null => new(1, true, "", Submission: await manager.Submit(command.Answer, adapter, token)),
                "status" => Status(manager, instance, command.SubmissionId),
                _ => new(1, false, "unsupported-request-operation")
            };
        } catch (InvalidDataException e) { return new(1, false, e.Message); }
          catch (InvalidOperationException e) { return new(1, false, e.Message); }
          catch (IOException) { return new(1, false, "request-state-storage-unavailable"); }
          catch (CryptographicException) { return new(1, false, "request-state-storage-unavailable"); }
    }
    static RequestReply Status(RequestCoordinator manager, string instance, string id) {
        var found = manager.Status(id);
        return found?.Key.Instance == instance ? new(1, true, "", Submission: found) : new(1, false, "unknown-submission");
    }
}

// The connector owns the production pipe. Native and protocol adapters remain separate.
internal sealed class RequestIpcServer : IAsyncDisposable {
    readonly CancellationTokenSource life = new();
    readonly RequestCoordinator manager;
    readonly Func<string, IRequestAdapter?> authorized;
    readonly string name;
    readonly Func<JsonElement, CancellationToken, Task<JsonElement>>? native;
    readonly Task loop;
    readonly SemaphoreSlim slots = new(8, 8);
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> clients = new();
    int serial;
    internal static string PipeName => "TerminalLink.Requests.v1." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User!.Value)))[..24];
    internal RequestIpcServer(RequestCoordinator manager, Func<string, IRequestAdapter?> authorized, string? isolatedName = null, Func<JsonElement, CancellationToken, Task<JsonElement>>? native = null) {
        this.manager = manager; this.authorized = authorized; this.native = native; name = isolatedName ?? PipeName; loop = Task.Run(Listen);
    }
    async Task Listen() {
        try {
            while (!life.IsCancellationRequested) {
                await slots.WaitAsync(life.Token);
                var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 16, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await pipe.WaitForConnectionAsync(life.Token); }
                catch { await pipe.DisposeAsync(); slots.Release(); throw; }
                int id = Interlocked.Increment(ref serial);
                var task = Serve(pipe); clients[id] = task;
                _ = task.ContinueWith(_ => { clients.TryRemove(id, out var ignored); slots.Release(); }, TaskScheduler.Default);
            }
        } catch (OperationCanceledException) when (life.IsCancellationRequested) { }
    }
    async Task Serve(NamedPipeServerStream pipe) {
        await using (pipe) {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(life.Token); deadline.CancelAfter(TimeSpan.FromSeconds(60));
            try {
                var command = await RequestWire.Read<RequestCommand>(pipe, deadline.Token);
                var reply = command.Version == 1 && command.Operation == "native" && command.Native is {} data && native != null
                    ? new RequestReply(1, true, "", Native: await native(data, deadline.Token))
                    : await RequestWire.Dispatch(manager, command, authorized, deadline.Token);
                await RequestWire.Write(pipe, reply, deadline.Token);
            } catch (Exception e) when (e is IOException or OperationCanceledException or JsonException) { }
        }
    }
    public async ValueTask DisposeAsync() {
        life.Cancel(); await loop.ConfigureAwait(false); await Task.WhenAll(clients.Values.ToArray()).ConfigureAwait(false); life.Dispose();
    }
}
internal static class RequestIpcClient {
    internal static async Task<RequestReply> Send(RequestCommand command, CancellationToken token, string? isolatedName = null) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(55));
        await using var pipe = new NamedPipeClientStream(".", isolatedName ?? RequestIpcServer.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(2000, deadline.Token);
        await RequestWire.Write(pipe, command, deadline.Token);
        return await RequestWire.Read<RequestReply>(pipe, deadline.Token);
    }
}
