using System.Text.Json.Serialization;

namespace TerminalLink.Requests;

// Wire v1 is independent of terminal layout, transport request IDs and list order.
internal sealed record RequestKey(string Instance, string Thread, string Turn, string Item);
internal sealed record RequestOption(string Label, string Description);
internal sealed record RequestQuestion(string Id, string Title, RequestOption[] Options, bool AllowText, bool Secret = false);
internal sealed record RequestDefinition(RequestKey Key, string Kind, RequestQuestion[] Questions);
[JsonConverter(typeof(JsonStringEnumConverter<LifeState>))]
internal enum LifeState { Open, Resolved, Withdrawn }
[JsonConverter(typeof(JsonStringEnumConverter<ConnectionState>))]
internal enum ConnectionState { Fresh, Stale, Unsupported }
[JsonConverter(typeof(JsonStringEnumConverter<DeliveryState>))]
internal enum DeliveryState { Dispatching, SentUnconfirmed, Accepted, Rejected }
internal sealed record RequestView(RequestDefinition Definition, long Revision, LifeState Life,
    ConnectionState Connection, string Reason);
internal sealed record Submission(string Id, RequestKey Key, string PayloadHash, DeliveryState Delivery, string Reason);
internal sealed record RequestSnapshot(int Version, string Epoch, long Revision, string Instance, bool Complete,
    RequestView[] Requests, Submission[] Submissions);
internal sealed record AnswerCommand(string SubmissionId, RequestKey Key, long RequestRevision,
    Dictionary<string, string[]> Answers);
internal sealed record SourceCursor(string Instance, string Epoch, long Sequence, bool Synchronized);
internal sealed record RequestDocument(RequestView[] Requests, Submission[] Submissions, SourceCursor[] Cursors);
internal enum DispatchOutcome { Accepted, SentUnconfirmed, RejectedBeforeWrite }

// An adapter may report Accepted only with provider-specific response evidence.
// A completed socket write, Enter key or serverRequest/resolved is insufficient.
internal interface IRequestAdapter {
    Task<DispatchOutcome> Dispatch(AnswerCommand command, CancellationToken cancellation);
}
internal interface IRequestFailureInfo { string RejectionCode { get; } }
internal interface IRequestJournal {
    RequestDocument? Load();
    void Save(RequestDocument document);
}
