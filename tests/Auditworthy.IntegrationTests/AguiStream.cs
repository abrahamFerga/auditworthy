using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// One AG-UI turn, parsed from the <c>text/event-stream</c> body of
/// <c>POST /api/agui/{moduleId}</c>.
/// </summary>
/// <remarks>
/// The event vocabulary here was read off a live Plenipo 0.1.0-alpha.28 host, not from docs. A
/// healthy turn is:
/// <code>
/// RUN_STARTED
/// [TOOL_CALL_START(name) → TOOL_CALL_END]*
/// TEXT_MESSAGE_START → TEXT_MESSAGE_CONTENT(delta)*
/// [CUSTOM(approval_required)] → CUSTOM(token_usage) → TEXT_MESSAGE_END
/// RUN_FINISHED(result.conversationId)
/// </code>
/// Note that the tool calls arrive <b>before</b> the assistant text, and that
/// <c>CUSTOM(token_usage)</c> lands before <c>TEXT_MESSAGE_END</c>. <c>RUN_ERROR</c> is always a
/// failure, never noise.
/// </remarks>
public sealed class AguiStream
{
    private readonly List<JsonElement> _events = [];

    private AguiStream() { }

    public IReadOnlyList<string> EventTypes { get; private set; } = [];

    /// <summary>Names of the tools the runner actually invoked, in order.</summary>
    public IReadOnlyList<string> ToolCalls { get; private set; } = [];

    /// <summary>The <c>name</c> of every <c>CUSTOM</c> event, e.g. <c>approval_required</c>.</summary>
    public IReadOnlyList<string> CustomEvents { get; private set; } = [];

    /// <summary>The assistant's reply, reassembled from the TEXT_MESSAGE_CONTENT deltas.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The conversation the turn belongs to — how a test finds its own approval.</summary>
    public Guid? ConversationId { get; private set; }

    public bool RequiredApproval => CustomEvents.Contains("approval_required");

    public bool Failed => EventTypes.Contains("RUN_ERROR");

    /// <summary>The text of the RUN_ERROR, for an assertion message worth reading.</summary>
    public string? Error => _events
        .Where(e => Type(e) == "RUN_ERROR")
        .Select(e => e.TryGetProperty("message", out var m) ? m.ToString() : e.ToString())
        .FirstOrDefault();

    public static async Task<AguiStream> PostAsync(
        HttpClient client, string moduleId, string message, CancellationToken cancellationToken = default)
    {
        var payload = new { messages = new[] { new { id = "m1", role = "user", content = message } } };
        var response = await client.PostAsJsonAsync($"/api/agui/{moduleId}", payload, cancellationToken);
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    public static AguiStream Parse(string body)
    {
        var stream = new AguiStream();
        var text = new StringBuilder();

        foreach (var line in body.Split('\n'))
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var json = line["data:".Length..].Trim();
            if (json.Length == 0) continue;

            stream._events.Add(JsonDocument.Parse(json).RootElement.Clone());
        }

        foreach (var e in stream._events)
        {
            switch (Type(e))
            {
                case "TEXT_MESSAGE_CONTENT" when e.TryGetProperty("delta", out var delta):
                    text.Append(delta.GetString());
                    break;
                case "RUN_FINISHED" when e.TryGetProperty("result", out var result)
                                      && result.TryGetProperty("conversationId", out var id):
                    stream.ConversationId = id.GetGuid();
                    break;
            }
        }

        stream.EventTypes = stream._events.Select(Type).ToList();
        stream.ToolCalls = stream._events
            .Where(e => Type(e) == "TOOL_CALL_START" && e.TryGetProperty("toolCallName", out _))
            .Select(e => e.GetProperty("toolCallName").GetString()!)
            .ToList();
        stream.CustomEvents = stream._events
            .Where(e => Type(e) == "CUSTOM" && e.TryGetProperty("name", out _))
            .Select(e => e.GetProperty("name").GetString()!)
            .ToList();
        stream.Text = text.ToString();

        return stream;
    }

    private static string Type(JsonElement e) =>
        e.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
}
