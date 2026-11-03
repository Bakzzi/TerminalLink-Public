using System.Text.Json;
using TerminalLink.Requests;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch (Exception e) when (e is InvalidDataException or InvalidOperationException or IOException) { passed++; return; } throw new Exception(name); }
RequestDefinition Def(string item = "item1", string thread = "thread1", int count = 2) => new(new("fixture-instance", thread, "turn1", item), "userInput",
    Enumerable.Range(1, count).Select(i => new RequestQuestion("q" + i, "Same question?", [new("Yes", "Continue"), new("No", "Stop")], true)).ToArray());
AnswerCommand Answer(RequestView view, string? id = null) => new(id ?? Guid.NewGuid().ToString("D"), view.Definition.Key, view.Revision,
    view.Definition.Questions.ToDictionary(q => q.Id, q => new[] { "Yes" }));
var memory = new MemoryJournal(); var manager = new RequestCoordinator(memory); var first = Def();
var admission=new RequestAdmission();var entered=admission.Enter();
Check(entered!=null&&admission.Busy&&!admission.Pause(),"active-request-defers-connector-update");
entered!.Dispose();entered.Dispose();Check(!admission.Busy&&admission.Pause(),"released-request-allows-atomic-update-pause");
Check(admission.Enter()==null,"paused-connector-rejects-late-dispatch");
manager.BeginSource("fixture-instance", "source1");
Check(manager.Observe("fixture-instance", "source1", 0, true, [first]), "initial-snapshot");
var original = manager.Snapshot("fixture-instance").Requests.Single();
manager.MarkConnection("fixture-instance", ConnectionState.Stale, "read-failed");
var stale = manager.Snapshot("fixture-instance").Requests.Single();
Check(stale.Life == LifeState.Open && stale.Definition.Questions.Length == 2 && stale.Revision == original.Revision, "read-loss-preserves-request");
Check(manager.Observe("fixture-instance", "source1", 0, true, [first]), "same-sequence-resync");
Check(!manager.Observe("fixture-instance", "source1", 0, true, []), "duplicate-snapshot-not-delete");
Check(!manager.Observe("fixture-instance", "source1", 3, false, []), "gap-rejected");
Check(manager.Snapshot("fixture-instance").Requests.Single().Life == LifeState.Open, "gap-not-completion");
Check(manager.Observe("fixture-instance", "source1", 3, true, [first]), "gap-full-resync");
Check(manager.Observe("fixture-instance", "source1", 4, false, [Def("item2")]), "new-request");
Check(manager.Snapshot("fixture-instance").Requests.Count(r => r.Life == LifeState.Open) == 2, "partial-not-delete");
Check(manager.Observe("fixture-instance", "source1", 5, false, [], resolved: [first.Key]), "late-completion");
Check(manager.Snapshot("fixture-instance").Requests.Single(r => r.Definition.Key.Item == "item2").Life == LifeState.Open, "late-completion-other-request-kept");
manager.Observe("fixture-instance", "source1", 6, false, [first]);
Check(manager.Snapshot("fixture-instance").Requests.Single(r => r.Definition.Key == first.Key).Life == LifeState.Resolved, "no-resurrection");
manager.BeginSource("fixture-instance", "source2");
Check(!manager.Observe("fixture-instance", "source1", 100, true, [Def("stale-epoch")]), "old-epoch-ignored");
var many = Enumerable.Range(0, 70).Select(i => Def("batch" + i)).Append(Def("same", "other-thread")).ToArray();
manager.Observe("fixture-instance", "source2", 0, true, many);
Check(manager.Snapshot("fixture-instance").Requests.Count(r => r.Life == LifeState.Open) == 71, "more-than-32-open");
var view = manager.Snapshot("fixture-instance").Requests.First(r => r.Life == LifeState.Open);
var command = Answer(view); var adapter = new Adapter();
var dispatch = manager.Submit(command, adapter, default);
await adapter.Started.Task;
Check(manager.Status(command.SubmissionId)?.Delivery == DeliveryState.Dispatching, "journal-before-write");
Check((await manager.Submit(command, adapter, default)).Delivery == DeliveryState.Dispatching && adapter.Writes == 1, "double-submit-idempotent");
await Reject(() => manager.Submit(Answer(view), adapter, default), "two-clients-serialized");
await Reject(() => manager.Submit(command with { Answers = command.Answers.ToDictionary(p => p.Key, p => new[] { "No" }) }, adapter, default), "nonce-content-conflict");
adapter.Finish.SetResult(DispatchOutcome.SentUnconfirmed); await dispatch;
Check(manager.Status(command.SubmissionId)?.Delivery == DeliveryState.SentUnconfirmed, "write-is-not-acceptance");
var restarted = new RequestCoordinator(memory);
Check(restarted.Status(command.SubmissionId)?.Delivery == DeliveryState.SentUnconfirmed, "restart-query-preserved");
var s = manager.Status(command.SubmissionId)!;
Check(!manager.ConfirmAnswers(s.Id, command.Answers.ToDictionary(p=>p.Key,p=>new[]{"Wrong"})), "wrong-original-answer-not-receipt");
Check(manager.ConfirmAnswers(s.Id,command.Answers), "original-answer-hash-confirms-submission");
manager.ConfirmAccepted(s.Id, s.PayloadHash);
Check(manager.Status(s.Id)?.Delivery == DeliveryState.Accepted, "correlated-receipt-accepts");
Check((await manager.Submit(command, adapter, default)).Delivery == DeliveryState.Accepted && adapter.Writes == 1, "lost-ack-does-not-resend");
await Reject(() => { manager.ConfirmAccepted(s.Id, "wrong"); return Task.CompletedTask; }, "mismatched-receipt-rejected");
var next = manager.Snapshot("fixture-instance").Requests.First(r => r.Life == LifeState.Open);
var partial = Answer(next); partial.Answers.Remove("q2");
await Reject(() => manager.Submit(partial, adapter, default), "all-questions-before-write");
var copy = manager.Snapshot("fixture-instance"); copy.Requests.Single(r => r.Definition.Key == next.Definition.Key).Definition.Questions[0] = new("changed", "Changed", [], true);
Check(manager.Snapshot("fixture-instance").Requests.Single(r => r.Definition.Key == next.Definition.Key).Definition.Questions[0].Id == "q1", "snapshot-immutable");
memory.FailWrites = true;
await Reject(() => manager.Submit(Answer(next), adapter, default), "disk-failure-blocks-write");
Check(adapter.Writes == 1, "no-external-write-after-disk-failure"); memory.FailWrites = false;

var held = new Adapter { RejectionCode="selection-unconfirmed" }; var heldCommand = Answer(next); var heldTask = manager.Submit(heldCommand, held, default); await held.Started.Task;
var crash = new RequestCoordinator(memory);
Check(crash.Status(heldCommand.SubmissionId)?.Delivery == DeliveryState.SentUnconfirmed, "crash-during-dispatch-unknown");
held.Finish.SetResult(DispatchOutcome.RejectedBeforeWrite); await heldTask;
Check(manager.Status(heldCommand.SubmissionId)?.Delivery == DeliveryState.Rejected, "proven-rejection-retryable");
Check(manager.Status(heldCommand.SubmissionId)?.Reason == "selection-unconfirmed", "proven-rejection-preserves-specific-fixed-code");

var incoming = JsonSerializer.SerializeToElement(new { id = 0, method = "item/tool/requestUserInput", @params = new {
    threadId = "thread1", turnId = "turn1", itemId = "codec", isBlocking = false,
    questions = new[] { new { id = "q1", header = "Test", question = "Same question?", isOther = true, isSecret = true,
        options = new[] { new { label = "Yes", description = "Continue" } } }, new { id = "q2", header = "Test", question = "Same question?", isOther = true, isSecret = false,
        options = new[] { new { label = "Yes", description = "Continue" } } } } } });
var parsed = CodexRequestProtocol.Read("fixture-instance", incoming)!;
Check(parsed.Definition.Questions.Length == 2 && parsed.Definition.Questions[0].Secret && !parsed.Blocking, "group-secret-and-nonblocking-schema");
Check(!RequestCoordinator.NotificationText(new(parsed.Definition, 1, LifeState.Open, ConnectionState.Fresh, "")).Contains("Same question?"), "secret-notification-redacted");
var codecCommand = new AnswerCommand(Guid.NewGuid().ToString("D"), parsed.Definition.Key, 1, new() { ["q2"] = ["No"], ["q1"] = ["Yes"] });
var reply = CodexRequestProtocol.Reply(parsed, codecCommand);
Check(reply.GetProperty("id").GetInt32() == 0 && reply.GetProperty("result").GetProperty("answers").EnumerateObject().Count() == 2, "rpc-zero-id-group-response");
var receipt = JsonSerializer.SerializeToElement(new { type = "response_item", payload = new { type = "function_call_output", call_id = "codec",
    output = reply.GetProperty("result").GetRawText(), internal_chat_message_metadata_passthrough = new { turn_id = "turn1" } }, metadata = new { client_authored = false } });
Check(CodexRequestProtocol.HasReceipt(receipt, codecCommand), "record-receipt-exact-answers");
Check(!CodexRequestProtocol.HasReceipt(receipt, codecCommand with { Key = codecCommand.Key with { Turn = "other-turn" } }), "receipt-turn-isolation");
Check(!CodexRequestProtocol.HasReceipt(JsonSerializer.SerializeToElement(new { method = "serverRequest/resolved" }), codecCommand), "resolved-not-receipt");

var privateRoot = Path.Combine(Path.GetTempPath(), "TerminalLink-request-fixture-" + Guid.NewGuid().ToString("N"));
try {
    using (var disk = new ProtectedRequestJournal(privateRoot)) {
        disk.Save(memory.Value!); var loaded = disk.Load()!;
        Check(loaded.Submissions.Length == memory.Value!.Submissions.Length, "dpapi-round-trip");
        Check(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(privateRoot, "requests-v1.dpapi"))).Contains("Same question?"), "no-plaintext-journal");
        await Reject(() => { using var duplicate = new ProtectedRequestJournal(privateRoot); return Task.CompletedTask; }, "single-writer-file-lock");
        disk.Dispose();
        await Reject(() => { disk.Save(memory.Value!); return Task.CompletedTask; }, "closed-owner-cannot-overwrite-next-journal");
    }
    var isolatedPipe = RequestIpcServer.PipeName + ".fixture." + Guid.NewGuid().ToString("N");
    await using var server = new RequestIpcServer(manager, id => id == "fixture-instance" ? adapter : null, isolatedPipe, native: (data, ct) => Task.FromResult(JsonSerializer.SerializeToElement(new { request = data.GetProperty("request").GetString(), delivery = "SentUnconfirmed", ok = false })));
    string nativeId=Guid.NewGuid().ToString();
    var nativeReply=await RequestIpcClient.Send(new(1,"native",Native:JsonSerializer.SerializeToElement(new{op="requestStatus",request=nativeId,id="fixture-instance"})),default,isolatedPipe);
    Check(nativeReply.Ok&&nativeReply.Native?.GetProperty("request").GetString()==nativeId&&nativeReply.Native?.GetProperty("delivery").GetString()=="SentUnconfirmed","native-desktop-envelope-and-status-reply");
    var read = await RequestIpcClient.Send(new(1, "snapshot", "fixture-instance"), default, isolatedPipe);
    Check(read.Ok && read.Snapshot!.Requests.Length > 32, "same-user-ipc-snapshot");
    var result = await RequestIpcClient.Send(new(1, "status", "fixture-instance", s.Id), default, isolatedPipe);
    Check(result.Ok && result.Submission!.Delivery == DeliveryState.Accepted, "ipc-reconnect-query");
    Check(!(await RequestIpcClient.Send(new(2, "snapshot", "fixture-instance"), default, isolatedPipe)).Ok, "version-negotiation");
    Check(!(await RequestIpcClient.Send(new(1, "snapshot", "unknown-instance"), default, isolatedPipe)).Ok, "instance-authorization");
    var bytes = new MemoryStream(new byte[] { 255, 255, 255, 127 });
    await Reject(async () => { await RequestWire.Read<RequestCommand>(bytes, default); }, "bounded-ipc-framing");
} finally { if (Directory.Exists(privateRoot)) Directory.Delete(privateRoot, true); }
await ScenarioChecks.Run(Check);
Console.WriteLine($"Request lifecycle: {passed} checks passed. Synthetic state/IPC only; no live terminal input.");

sealed class MemoryJournal : IRequestJournal {
    internal RequestDocument? Value; internal bool FailWrites;
    public RequestDocument? Load() => Value;
    public void Save(RequestDocument value) { if (FailWrites) throw new IOException("fixture-disk-failure"); Value = value; }
}
sealed class Adapter : IRequestAdapter, IRequestFailureInfo {
    public string RejectionCode { get; set; }="RejectedBeforeWrite";
    internal int Writes;
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource<DispatchOutcome> Finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task<DispatchOutcome> Dispatch(AnswerCommand command, CancellationToken token) {
        Interlocked.Increment(ref Writes); Started.TrySetResult(); return await Finish.Task.WaitAsync(token);
    }
}
