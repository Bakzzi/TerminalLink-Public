using System.Text.Json;

namespace TerminalLink.Requests;

// One connector-owned coordinator for original-record requests. The native
// adapter retains console/thread checks; clients never inject request keys.
internal sealed class NativeRequestService : IDisposable {
    readonly EventHub hub;
    readonly Func<List<TerminalWindow>> allowed;
    readonly ProtectedRequestJournal journal;
    readonly RequestCoordinator manager;
    readonly RequestIpcServer ipc;
    readonly object sync = new();
    readonly Dictionary<string, string> observations = new();
    readonly Dictionary<string, long> sequences = new();
    readonly Dictionary<string, long> receiptOffsets = new();
    readonly string epoch = Guid.NewGuid().ToString("N");
    readonly RequestAdmission admission=new();
    bool disposed;
    internal bool Busy=>admission.Busy;
    internal bool PauseForUpdate()=>admission.Pause();
    string storageError="";
    internal NativeRequestService(EventHub hub, Func<List<TerminalWindow>> allowed, string? isolatedRoot = null) {
        this.hub = hub; this.allowed = allowed;
        journal = new(isolatedRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerminalLink", "requests-v1"));
        manager = new(journal);
        ipc = new(manager, Resolve, isolatedRoot == null ? null : "TerminalLink.Requests.Fixture." + Guid.NewGuid().ToString("N"), native: Handle);
    }
    ConsoleTarget? Target(string id) => ConsoleNative.Targets(allowed()).FirstOrDefault(t => t.Id == id && t.Valid);
    IRequestAdapter? Resolve(string id) => Target(id) is {} target ? new Adapter(hub.Track(target)) : null;
    static RequestDefinition Definition(string instance, string thread, QueuedQuestion q) => new(
        new(instance, thread, q.turn.Length > 0 ? q.turn : "async-call:" + q.id[..q.id.LastIndexOf(':')], q.id),
        "userInput", [new(q.id, q.title, q.options.Select(o => new RequestOption(o, "")).ToArray(), true)]);
    internal void Observe(string instance, Conversation? conversation) {
        try { ObserveCore(instance,conversation); storageError=""; }
        catch(Exception e) when(e is IOException or System.Security.Cryptography.CryptographicException or InvalidOperationException) {
            // Request storage failure must not stop unrelated conversation refreshes.
            storageError="요청 상태 저장소를 읽거나 저장하지 못했습니다. 답변은 보내지 않았습니다.";
        }
    }
    void ObserveCore(string instance, Conversation? conversation) {
        lock (sync) {
            if(disposed)return;
            if (!ConversationReader.IsOriginal(conversation)) {
                manager.MarkConnection(instance, ConnectionState.Stale, "original-unavailable"); observations.Remove(instance); return;
            }
            var definitions = (conversation!.requests ?? []).Where(q => q.id.Contains(':')).Select(q => Definition(instance, conversation.id, q)).ToArray();
            string signature = JsonSerializer.Serialize(new { conversation.id, definitions });
            if (!observations.TryGetValue(instance, out var previous) || previous != signature) {
                manager.BeginSource(instance, epoch);
                sequences.TryGetValue(instance, out long sequence);
                manager.Observe(instance, epoch, sequence, true, definitions);
                sequences[instance] = sequence + 1; observations[instance] = signature;
            }
            string receiptSource=instance+"\n"+conversation.id;
            receiptOffsets.TryGetValue(receiptSource,out long after);
            foreach (var submission in manager.Pending(instance).Where(s => s.Key.Thread == conversation.id)) {
                foreach (var message in conversation.messages.Where(m => m.sequence>after && m.role == "user")) {
                    foreach (var receipt in NativeRequestReceipt.Read(message.text)) {
                        if (receipt.Id == submission.Key.Item) manager.ConfirmAnswers(submission.Id,
                            new() { [receipt.Id] = [receipt.Answer] });
                    }
                }
            }
            receiptOffsets[receiptSource]=conversation.messages.LastOrDefault()?.sequence??0;
        }
    }
    internal async Task<JsonElement> Handle(JsonElement message, CancellationToken token) {
        using var lease=admission.Enter();
        if(lease==null)return Reply(message.Str("request"),false,"Rejected","PC 프로그램 업데이트 중입니다. 잠시 뒤 다시 확인해 주세요.");
        return await HandleCore(message,token);
    }
    async Task<JsonElement> HandleCore(JsonElement message,CancellationToken token) {
        string id = message.Str("id"), nonce = message.Str("request");
        try {
            var target = Target(id) ?? throw new IOException("해당 터미널이 닫혔거나 공유가 해제되었습니다.");
            var terminal = hub.Track(target);
            if (message.Str("op") == "requestStatus") {
                var saved = manager.Status(message.Str("submissionId") is {Length:>0} canonical?canonical:nonce);
                return saved?.Key.Instance == id ? Result(saved,nonce) : Reply(nonce, false, "Unknown", "전송 기록을 확인하지 못했습니다. 자동으로 다시 보내지 않습니다.");
            }
            var savedSubmission = manager.Status(nonce);
            // A retry queries the durable operation; it never sends Enter again.
            if (savedSubmission != null) {
                if (savedSubmission.Key.Instance != id || savedSubmission.Key.Thread != message.Str("thread") || savedSubmission.Key.Item != message.Str("requestKey")) throw new IOException("답변 대상이 변경되었습니다.");
                var oldQuestion=manager.Snapshot(id).Requests.Single(r=>r.Definition.Key==savedSubmission.Key).Definition.Questions.Single();
                int oldChoice=message.TryGetProperty("choice",out var selected)?selected.GetInt32():-1;
                string value=oldChoice>=0&&oldChoice<oldQuestion.Options.Length?oldQuestion.Options[oldChoice].Label:message.Str("text").Replace("\r\n","\n").Replace("\r","\n");
                if(message.Str("question")!=oldQuestion.Title||!manager.MatchesAnswers(nonce,new(){[oldQuestion.Id]=[value]}))return Reply(nonce,false,"Rejected","같은 전송 번호의 답변 내용이 변경되었습니다.");
                return Result(savedSubmission);
            }
            var chat = terminal.Conversation;
            if (!ConversationReader.IsOriginal(chat) || chat!.id != message.Str("thread")) {
                terminal.Retry();
                long until = Environment.TickCount64 + 5000;
                while (Environment.TickCount64 < until) {
                    await Task.Delay(100, token); chat = terminal.Conversation;
                    if (ConversationReader.IsOriginal(chat) && chat!.id == message.Str("thread")) break;
                }
            }
            Observe(id, chat);
            if(storageError.Length>0)throw new IOException(storageError);
            var view = manager.Snapshot(id).Requests.SingleOrDefault(r => r.Definition.Key.Thread == message.Str("thread") && r.Definition.Key.Item == message.Str("requestKey"));
            if (view == null || view.Life != LifeState.Open) throw new IOException("이 요청은 종료되었습니다. 작성한 답변은 유지됩니다.");
            var q = view.Definition.Questions.Single();
            if (message.Str("question") != q.Title) throw new IOException("질문이 변경되었습니다. 작성한 답변은 유지됩니다.");
            if(message.TryGetProperty("choices",out var options) && !options.EnumerateArray().Select(o=>o.GetString()??"").SequenceEqual(q.Options.Length==0?Array.Empty<string>():q.Options.Select(o=>o.Label).Append("직접 입력")))throw new IOException("선택지가 변경되었습니다. 작성한 답변은 유지됩니다.");
            int choice = message.TryGetProperty("choice", out var c) ? c.GetInt32() : -1;
            string answer = choice >= 0 && choice < q.Options.Length ? q.Options[choice].Label : choice == -1 ? message.Str("text").Replace("\r\n", "\n").Replace("\r", "\n") : "";
            if (answer.Length == 0 || answer.Length > 4096) throw new IOException("답변을 4096자 이내로 입력하세요.");
            var existing = manager.Snapshot(id).Submissions.LastOrDefault(s => s.Key == view.Definition.Key && s.Delivery != DeliveryState.Rejected);
            if (existing != null) return manager.MatchesAnswers(existing.Id,new(){[q.Id]=[answer]})?Result(existing,nonce):Reply(nonce,false,"Rejected","이 질문의 다른 답변이 이미 전송 중입니다. 먼저 전송 결과를 확인해 주세요.");
            var command = new AnswerCommand(nonce, view.Definition.Key, view.Revision, new() { [q.Id] = [answer] });
            return Result(await manager.Submit(command, new Adapter(terminal, view.Definition), token));
        } catch (Exception e) when (e is IOException or InvalidOperationException or InvalidDataException) {
            var saved=manager.Status(nonce);
            if(saved?.Key.Instance==id&&saved.Key.Thread==message.Str("thread")&&saved.Key.Item==message.Str("requestKey"))return Result(saved);
            return Reply(nonce, false, "Rejected", e is InvalidOperationException ? "요청 상태를 다시 확인해 주세요. 작성한 답변은 유지됩니다." : e.Message);
        }
    }
    static JsonElement Result(Submission result, string? nonce = null) => Reply(nonce ?? result.Id, result.Delivery == DeliveryState.Accepted, result.Delivery.ToString(), result.Delivery switch {
        DeliveryState.Accepted => "답변이 도착한 것을 확인했습니다.",
        DeliveryState.Rejected => RequestFailure.Message(result.Reason),
        _ => "답변 도착 여부를 확인 중입니다. 자동으로 다시 보내지 않습니다."
    }, result.Id);
    static JsonElement Reply(string nonce, bool ok, string delivery, string message, string submissionId = "") => JsonSerializer.SerializeToElement(new { type = "notificationResult", request = nonce, ok, delivery, message, submissionId });
    sealed class Adapter(EventHub.Terminal terminal, RequestDefinition? definition = null) : IRequestAdapter, IRequestFailureInfo {
        public string RejectionCode { get; private set; }="RejectedBeforeWrite";
        public async Task<DispatchOutcome> Dispatch(AnswerCommand command, CancellationToken cancellation) {
            var q = definition?.Questions.Single();
            if (q == null) return DispatchOutcome.RejectedBeforeWrite;
            string answer = command.Answers[q.Id].Single(); int choice = Array.FindIndex(q.Options, o => o.Label == answer);
            var choices = q.Options.Length == 0 ? Array.Empty<string>() : q.Options.Select(o => o.Label).Append("직접 입력").ToArray();
            var result = await terminal.Bridge.Notification(JsonSerializer.SerializeToElement(new { op = "notificationInput", request = command.SubmissionId, kind = "request", thread = command.Key.Thread, requestKey = q.Id, question = q.Title, choices, followup = true, questionKind = true, choice, text = choice < 0 ? answer : "", confirmReceipt = true }), cancellation, TimeSpan.FromSeconds(50));
            if(result.Str("delivery")=="Rejected")RejectionCode=RequestFailure.Code(result.Str("message"));
            return result.Str("delivery") switch { "Accepted" => DispatchOutcome.Accepted, "Rejected" => DispatchOutcome.RejectedBeforeWrite, _ => DispatchOutcome.SentUnconfirmed };
        }
    }
    public void Dispose() { lock(sync)disposed=true;ipc.DisposeAsync().AsTask().GetAwaiter().GetResult();journal.Dispose(); }
}
