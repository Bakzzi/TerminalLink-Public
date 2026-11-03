using System.Security.Cryptography;
using System.Text.Json;

namespace TerminalLink.Requests;

// The connector owns this object. All clients submit/query the same durable state.
// No native key injection, window matching, or conversation history parsing here.
internal sealed class RequestCoordinator {
    readonly object gate = new();
    readonly IRequestJournal journal;
    RequestDocument state;
    long revision;
    internal string Epoch { get; } = Guid.NewGuid().ToString("N");
    internal RequestCoordinator(IRequestJournal journal) {
        this.journal = journal;
        var restored = journal.Load() ?? new([], [], []);
        // A crash can occur after writing to the provider but before saving its ACK.
        state = restored with {
            Requests = restored.Requests.Select(r => r with { Connection = ConnectionState.Stale }).ToArray(),
            Submissions = restored.Submissions.Select(s => s.Delivery == DeliveryState.Dispatching
                ? s with { Delivery = DeliveryState.SentUnconfirmed, Reason = "restart-result-unknown" } : s).ToArray(),
            Cursors = restored.Cursors.Select(c => c with { Synchronized = false }).ToArray()
        };
        revision = state.Requests.Select(r => r.Revision).DefaultIfEmpty().Max();
        if (restored.Requests.Length > 0 || restored.Submissions.Length > 0) journal.Save(state);
    }
    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    void Commit(RequestDocument next) {
        // Save before publishing or performing an external side effect. Disk failure is not ignored.
        journal.Save(next); state = next; revision++;
    }
    internal RequestSnapshot Snapshot(string instance) {
        lock (gate) return Copy(new RequestSnapshot(1, Epoch, revision, instance, true,
            state.Requests.Where(r => r.Definition.Key.Instance == instance).ToArray(),
            state.Submissions.Where(s => s.Key.Instance == instance).ToArray()));
    }
    internal static string NotificationText(RequestView request) => request.Definition.Questions.Any(q => q.Secret)
        ? "비공개 답변이 필요한 요청이 있습니다. 앱에서 확인하세요." : string.Join("\n", request.Definition.Questions.Select(q => q.Title));
    internal Submission? Status(string id) {
        lock (gate) return state.Submissions.SingleOrDefault(s => s.Id == id);
    }
    internal Submission[] Pending(string instance) { lock (gate) return state.Submissions.Where(s => s.Key.Instance == instance && s.Delivery is DeliveryState.Dispatching or DeliveryState.SentUnconfirmed).ToArray(); }
    internal void BeginSource(string instance, string epoch) {
        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(epoch)) throw new InvalidDataException("invalid-source");
        lock (gate) {
            var cursor = state.Cursors.SingleOrDefault(c => c.Instance == instance);
            if (cursor?.Epoch == epoch) return;
            Commit(state with { Cursors = state.Cursors.Where(c => c.Instance != instance).Append(new(instance, epoch, -1, false)).ToArray(),
                Requests = state.Requests.Select(r => r.Definition.Key.Instance == instance ? r with { Connection = ConnectionState.Stale, Reason = "resync-required" } : r).ToArray() });
        }
    }
    static void Validate(RequestDefinition request) {
        var key = request.Key;
        if (new[] { key.Instance, key.Thread, key.Turn, key.Item }.Any(string.IsNullOrWhiteSpace)
            || request.Kind != "userInput" || request.Questions.Length == 0
            || request.Questions.Any(q => string.IsNullOrWhiteSpace(q.Id) || string.IsNullOrWhiteSpace(q.Title))
            || request.Questions.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count() != request.Questions.Length)
            throw new InvalidDataException("invalid-request-definition");
    }
    // Returns false for duplicates, old events, gaps or an unsynchronized new epoch.
    // A complete provider snapshot is the only way to recover after an event gap.
    internal bool Observe(string instance, string epoch, long sequence, bool complete,
        RequestDefinition[] pending, RequestKey[]? resolved = null, RequestKey[]? withdrawn = null) {
        if (string.IsNullOrWhiteSpace(epoch) || sequence < 0) throw new InvalidDataException("invalid-source-cursor");
        foreach (var request in pending) { Validate(request); if (request.Key.Instance != instance) throw new InvalidDataException("scope-mismatch"); }
        resolved ??= []; withdrawn ??= [];
        if (resolved.Concat(withdrawn).Any(k => k.Instance != instance) || pending.Select(r => r.Key).Distinct().Count() != pending.Length)
            throw new InvalidDataException("scope-or-key-mismatch");
        lock (gate) {
            var cursor = state.Cursors.SingleOrDefault(c => c.Instance == instance);
            if (cursor?.Epoch != epoch) return false;
            if (sequence < cursor.Sequence || sequence == cursor.Sequence && (cursor.Synchronized || !complete)) return false;
            bool continuous = cursor?.Epoch == epoch && cursor.Synchronized && sequence == cursor.Sequence + 1;
            if (!complete && !continuous) {
                MarkConnection(instance, ConnectionState.Stale, "resync-required"); return false;
            }
            var views = state.Requests.ToDictionary(r => r.Definition.Key);
            foreach (var request in pending) {
                if (views.TryGetValue(request.Key, out var prior)) {
                    // Late/replayed opens cannot resurrect a completed logical request.
                    if (prior.Life != LifeState.Open) continue;
                    bool unchanged = JsonSerializer.Serialize(prior.Definition) == JsonSerializer.Serialize(request);
                    views[request.Key] = prior with { Definition = Copy(request), Revision = unchanged ? prior.Revision : revision + 1,
                        Connection = ConnectionState.Fresh, Reason = "" };
                } else views.Add(request.Key, new(Copy(request), revision + 1, LifeState.Open, ConnectionState.Fresh, ""));
            }
            var keys = pending.Select(r => r.Key).ToHashSet();
            foreach (var prior in views.Values.ToArray()) {
                if (prior.Definition.Key.Instance != instance) continue;
                var key = prior.Definition.Key;
                if (withdrawn.Contains(key)) views[key] = prior with { Life = LifeState.Withdrawn, Connection = ConnectionState.Fresh, Reason = "provider-withdrawn" };
                else if (resolved.Contains(key)) views[key] = prior with { Life = LifeState.Resolved, Connection = ConnectionState.Fresh, Reason = "provider-resolved" };
                else if (complete && prior.Life == LifeState.Open && !keys.Contains(key))
                    views[key] = prior with { Life = LifeState.Resolved, Connection = ConnectionState.Fresh, Reason = "absent-from-complete-snapshot" };
            }
            Commit(state with { Requests = views.Values.ToArray(), Cursors = state.Cursors.Where(c => c.Instance != instance)
                .Append(new(instance, epoch, sequence, true)).ToArray() });
            return true;
        }
    }
    internal void MarkConnection(string instance, ConnectionState connection, string reason) {
        lock (gate) {
            if (!state.Requests.Any(r => r.Definition.Key.Instance == instance && (r.Connection != connection || r.Reason != reason))
                && !state.Cursors.Any(c => c.Instance == instance && c.Synchronized)) return;
            Commit(state with { Requests = state.Requests.Select(r => r.Definition.Key.Instance == instance
                ? r with { Connection = connection, Reason = reason } : r).ToArray(),
                Cursors = state.Cursors.Select(c => c.Instance == instance ? c with { Synchronized = false } : c).ToArray() });
        }
    }
    static string PayloadHash(AnswerCommand command) {
        var canonical = command.Answers.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new { id = p.Key, answers = p.Value }).ToArray();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { command.Key, canonical })));
    }
    internal async Task<Submission> Submit(AnswerCommand incoming, IRequestAdapter adapter, CancellationToken cancellation) {
        // Freeze caller-owned collections before awaiting or releasing the lock.
        var command = Copy(incoming);
        if (!Guid.TryParseExact(command.SubmissionId, "D", out _)) throw new InvalidDataException("invalid-submission-id");
        string hash = PayloadHash(command);
        lock (gate) {
            var prior = state.Submissions.SingleOrDefault(s => s.Id == command.SubmissionId);
            if (prior != null) {
                if (prior.Key != command.Key || prior.PayloadHash != hash) throw new InvalidDataException("submission-id-content-conflict");
                return prior;
            }
            var request = state.Requests.SingleOrDefault(r => r.Definition.Key == command.Key) ?? throw new InvalidDataException("unknown-request");
            if (request.Life != LifeState.Open || request.Connection != ConnectionState.Fresh) throw new InvalidOperationException("request-not-ready");
            if (request.Revision != command.RequestRevision) throw new InvalidOperationException("request-definition-changed");
            if (state.Submissions.Any(s => s.Key == command.Key && s.Delivery != DeliveryState.Rejected)) throw new InvalidOperationException("request-has-submission-query-status");
            if (command.Answers.Count != request.Definition.Questions.Length) throw new InvalidDataException("all-questions-required");
            foreach (var question in request.Definition.Questions) {
                if (!command.Answers.TryGetValue(question.Id, out var answers) || answers.Length != 1 || string.IsNullOrWhiteSpace(answers[0])) throw new InvalidDataException("missing-answer");
                if (!question.AllowText && !question.Options.Any(o => o.Label == answers[0])) throw new InvalidDataException("explicit-choice-required");
            }
            cancellation.ThrowIfCancellationRequested();
            Commit(state with { Submissions = state.Submissions.Append(new(command.SubmissionId, command.Key, hash, DeliveryState.Dispatching, "")).ToArray() });
        }
        DispatchOutcome outcome;
        try { outcome = await adapter.Dispatch(command, cancellation).ConfigureAwait(false); }
        catch { outcome = DispatchOutcome.SentUnconfirmed; } // Exception does not prove nothing was written.
        lock (gate) {
            var prior = state.Submissions.Single(s => s.Id == command.SubmissionId);
            if (prior.Delivery == DeliveryState.Accepted) return prior; // Provider receipt may race transport completion.
            var delivery = outcome switch { DispatchOutcome.Accepted => DeliveryState.Accepted,
                DispatchOutcome.RejectedBeforeWrite => DeliveryState.Rejected, _ => DeliveryState.SentUnconfirmed };
            string reason=delivery==DeliveryState.Rejected&&adapter is IRequestFailureInfo info?info.RejectionCode:outcome.ToString();
            var result = prior with { Delivery = delivery, Reason = reason };
            Commit(state with { Submissions = state.Submissions.Select(s => s.Id == result.Id ? result : s).ToArray(),
                Requests = delivery == DeliveryState.Accepted ? state.Requests.Select(r => r.Definition.Key == result.Key
                    ? r with { Life = LifeState.Resolved, Reason = "answer-accepted" } : r).ToArray() : state.Requests });
            return result;
        }
    }
    internal bool ConfirmAnswers(string submissionId, Dictionary<string, string[]> answers) {
        lock (gate) {
            var prior = state.Submissions.SingleOrDefault(s => s.Id == submissionId);
            if (prior == null || prior.Delivery == DeliveryState.Rejected || PayloadHash(new(submissionId, prior.Key, 0, answers)) != prior.PayloadHash) return false;
            ConfirmAccepted(submissionId, prior.PayloadHash); return true;
        }
    }
    internal bool MatchesAnswers(string submissionId, Dictionary<string,string[]> answers) {
        lock(gate) { var prior=state.Submissions.SingleOrDefault(s=>s.Id==submissionId);return prior!=null&&PayloadHash(new(submissionId,prior.Key,0,answers))==prior.PayloadHash; }
    }
    // The adapter must correlate an actual provider receipt to this exact submission.
    internal void ConfirmAccepted(string submissionId, string payloadHash) {
        lock (gate) {
            var prior = state.Submissions.SingleOrDefault(s => s.Id == submissionId) ?? throw new InvalidDataException("unknown-submission");
            if (prior.PayloadHash != payloadHash || prior.Delivery == DeliveryState.Rejected) throw new InvalidDataException("receipt-mismatch");
            if (prior.Delivery == DeliveryState.Accepted) return;
            Commit(state with { Submissions = state.Submissions.Select(s => s.Id == submissionId ? s with { Delivery = DeliveryState.Accepted, Reason = "provider-receipt" } : s).ToArray(),
                Requests = state.Requests.Select(r => r.Definition.Key == prior.Key ? r with { Life = LifeState.Resolved, Reason = "answer-accepted" } : r).ToArray() });
        }
    }
}
