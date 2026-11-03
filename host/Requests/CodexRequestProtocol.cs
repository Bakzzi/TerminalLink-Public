using System.Text.Json;

namespace TerminalLink.Requests;

// Installed 0.163.0 schema adapter. No fallback to native keystrokes on failure.
// Runtime binding/activation remains gated by docs/REQUEST-LIFECYCLE.md.
internal static class CodexRequestProtocol {
    internal sealed record Incoming(JsonElement RpcId, RequestDefinition Definition, bool Blocking);
    static string Required(JsonElement element, string field) => element.GetProperty(field).GetString() is { Length: > 0 } value
        ? value : throw new InvalidDataException("missing-protocol-field");
    internal static Incoming? Read(string instance, JsonElement message) {
        if (!message.TryGetProperty("method", out var method) || method.GetString() != "item/tool/requestUserInput") return null;
        var rpcId = message.GetProperty("id");
        if (rpcId.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)) throw new InvalidDataException("invalid-rpc-id");
        var p = message.GetProperty("params");
        var key = new RequestKey(instance, Required(p, "threadId"), Required(p, "turnId"), Required(p, "itemId"));
        var questions = p.GetProperty("questions").EnumerateArray().Select(q => new RequestQuestion(
            Required(q, "id"), Required(q, "question"),
            q.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array
                ? options.EnumerateArray().Select(o => new RequestOption(Required(o, "label"), o.GetProperty("description").GetString() ?? "")).ToArray() : [],
            q.TryGetProperty("isOther", out var other) && other.ValueKind == JsonValueKind.True || !q.TryGetProperty("options", out var choices) || choices.ValueKind == JsonValueKind.Null || choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() == 0,
            q.TryGetProperty("isSecret", out var secret) && secret.ValueKind == JsonValueKind.True)).ToArray();
        return new(rpcId.Clone(), new(key, "userInput", questions), p.GetProperty("isBlocking").GetBoolean());
    }
    internal static JsonElement Reply(Incoming request, AnswerCommand command) {
        if (request.Definition.Key != command.Key) throw new InvalidDataException("rpc-request-mismatch");
        return JsonSerializer.SerializeToElement(new { id = request.RpcId,
            result = new { answers = command.Answers.ToDictionary(p => p.Key, p => new { answers = p.Value }) } });
    }
    // Only call for an already verified original record of this exact thread/instance.
    // A generic resolved notification, read failure or task completion is not a receipt.
    internal static bool HasReceipt(JsonElement record, AnswerCommand command) {
        try {
            if (record.GetProperty("type").GetString() != "response_item") return false;
            var p = record.GetProperty("payload");
            if (p.GetProperty("type").GetString() != "function_call_output" || p.GetProperty("call_id").GetString() != command.Key.Item) return false;
            if (p.GetProperty("internal_chat_message_metadata_passthrough").GetProperty("turn_id").GetString() != command.Key.Turn) return false;
            if (record.GetProperty("metadata").GetProperty("client_authored").GetBoolean()) return false;
            using var output = JsonDocument.Parse(p.GetProperty("output").GetString()!);
            var answers = output.RootElement.GetProperty("answers");
            if (answers.EnumerateObject().Count() != command.Answers.Count) return false;
            return command.Answers.All(pair => answers.TryGetProperty(pair.Key, out var value)
                && value.GetProperty("answers").EnumerateArray().Select(x => x.GetString()).SequenceEqual(pair.Value));
        } catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException) { return false; }
    }
}
