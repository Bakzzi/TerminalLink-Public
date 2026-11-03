using System.Text.Json;
namespace TerminalLink;

// Exact original-record receipts, never screen disappearance or an Enter ACK.
internal static class NativeRequestReceipt {
    internal sealed record Receipt(string Id, string Answer);
    internal static Receipt[] Read(string text) {
        const string start = "<send_user_message_question_reply>", end = "</send_user_message_question_reply>";
        text = text.Trim();
        if (!text.StartsWith(start, StringComparison.Ordinal) || !text.EndsWith(end, StringComparison.Ordinal)) return [];
        try {
            using var doc = JsonDocument.Parse(text[start.Length..^end.Length]);
            var result = new List<Receipt>();
            foreach (var reply in doc.RootElement.EnumerateArray()) {
                using var key = JsonDocument.Parse(reply.Str("questionItemId")); var id = key.RootElement;
                if (id.ValueKind != JsonValueKind.Array || id.GetArrayLength() != 3 || id[0].GetString() != "request_user_input_async" || !id[2].TryGetInt32(out int index) || index < 0 || !reply.TryGetProperty("answer", out var answer) || answer.ValueKind != JsonValueKind.String) continue;
                result.Add(new(id[1].GetString() + ":" + index, answer.GetString()!));
            }
            return result.ToArray();
        } catch (Exception e) when (e is JsonException or InvalidOperationException) { return []; }
    }
    internal static bool Accepted(Conversation chat, string thread, string item, string answer, long after) => chat.id == thread &&
        chat.messages.Any(m => m.role == "user" && m.sequence > after && Read(m.text).Any(r => r.Id == item && r.Answer == answer));
}
