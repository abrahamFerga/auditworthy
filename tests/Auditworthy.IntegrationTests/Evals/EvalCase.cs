using System.Text.Json;
using System.Text.Json.Serialization;

namespace Auditworthy.IntegrationTests.Evals;

/// <summary>
/// One golden conversation: a user turn plus what the platform contract says must happen.
/// </summary>
/// <remarks>
/// Prompt-shaped changes — a module's <c>AgentInstructions</c>, a tool's <c>[Description]</c>, an
/// agent profile — change behaviour without changing a line of executable code, so the compiler and
/// the unit tests both stay green. These cases are the regression net for that class of change.
/// <para>
/// <see cref="JsonUnmappedMemberHandling.Disallow"/> is deliberate: a typo'd field in a case file
/// must fail loudly rather than silently assert nothing.
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EvalCase
{
    /// <summary>Case id, used as the test's display name. Match the file name.</summary>
    public required string Name { get; init; }

    /// <summary>Module id for the AG-UI route — <c>compliance</c> here.</summary>
    public required string Module { get; init; }

    /// <summary>The user's turn, phrased the way a real user would.</summary>
    public required string Message { get; init; }

    /// <summary>Dev-auth role the turn is sent as. Narrow it to assert an RBAC boundary.</summary>
    public string Role { get; init; } = "system_admin";

    /// <summary>Tools the turn must route to.</summary>
    public IReadOnlyList<string> ExpectToolCalls { get; init; } = [];

    /// <summary>Tools the turn must NOT reach — the RBAC and mis-routing assertion.</summary>
    public IReadOnlyList<string> ForbidToolCalls { get; init; } = [];

    /// <summary>Whether the turn must park on the human-approval gate.</summary>
    public bool ExpectApproval { get; init; }

    /// <summary>Substrings the reply must contain, case-insensitive.</summary>
    public IReadOnlyList<string> ReplyMustContain { get; init; } = [];

    /// <summary>Substrings the reply must not contain — usually a claim of a write that is parked.</summary>
    public IReadOnlyList<string> ReplyMustNotContain { get; init; } = [];

    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static EvalCase Load(string path) =>
        JsonSerializer.Deserialize<EvalCase>(File.ReadAllText(path), SerializerOptions)
        ?? throw new InvalidOperationException($"Eval case '{path}' deserialized to null.");
}
