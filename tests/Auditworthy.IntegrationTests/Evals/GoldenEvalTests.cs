using Xunit;

namespace Auditworthy.IntegrationTests.Evals;

/// <summary>
/// Rung 4 of the test ladder: drives every <c>Evals/cases/*.json</c> through the real AG-UI
/// endpoint and asserts the platform contract — routing, gating, protocol.
/// </summary>
/// <remarks>
/// <b>Limit, stated up front:</b> the assistant runs on Plenipo's <c>Mock</c> provider, which
/// selects a tool by matching name tokens in the user's message rather than by reasoning. So these
/// cases prove the contract around the model — that the right tool is reachable, that an
/// unpermitted tool is never offered, that a write parks on the gate, and that the reply does not
/// claim a parked write happened. They prove nothing about answer quality. Do not add a case whose
/// expectation only a real model could satisfy; it will be a flake that teaches the next agent to
/// delete this harness.
/// </remarks>
[Collection("api")]
public sealed class GoldenEvalTests(IntegrationFixture fixture)
{
    private static readonly string CasesDirectory =
        Path.Combine(AppContext.BaseDirectory, "Evals", "cases");

    public static TheoryData<string> CaseFiles
    {
        get
        {
            var data = new TheoryData<string>();

            // A missing directory means the csproj's <Content Include="Evals\cases\*.json" /> was
            // dropped: without this the theory would silently run zero cases and report green.
            foreach (var file in Directory.EnumerateFiles(CasesDirectory, "*.json").Order())
            {
                data.Add(Path.GetFileName(file));
            }

            return data;
        }
    }

    [Fact]
    public void There_is_at_least_one_golden_case()
    {
        Assert.True(Directory.Exists(CasesDirectory),
            $"No eval cases were copied to '{CasesDirectory}' — check the Content item in the csproj.");
        Assert.NotEmpty(Directory.EnumerateFiles(CasesDirectory, "*.json"));
    }

    [Theory]
    [MemberData(nameof(CaseFiles))]
    public async Task Case(string fileName)
    {
        var testCase = EvalCase.Load(Path.Combine(CasesDirectory, fileName));

        using var client = fixture.AdminClient(roles: testCase.Role, subject: $"eval-{testCase.Role}");
        var turn = await AguiStream.PostAsync(client, testCase.Module, testCase.Message);

        // Implicit in every case: the turn ran and did not fault.
        Assert.False(turn.Failed, $"[{testCase.Name}] RUN_ERROR: {turn.Error}");
        Assert.Contains("RUN_STARTED", turn.EventTypes);
        Assert.Contains("RUN_FINISHED", turn.EventTypes);

        foreach (var tool in testCase.ExpectToolCalls)
        {
            Assert.True(turn.ToolCalls.Contains(tool),
                $"[{testCase.Name}] expected tool '{tool}'; the turn called [{string.Join(", ", turn.ToolCalls)}].");
        }

        foreach (var tool in testCase.ForbidToolCalls)
        {
            Assert.False(turn.ToolCalls.Contains(tool),
                $"[{testCase.Name}] tool '{tool}' must not be reachable as '{testCase.Role}', but it was called.");
        }

        Assert.True(turn.RequiredApproval == testCase.ExpectApproval,
            $"[{testCase.Name}] expected expectApproval={testCase.ExpectApproval}; "
          + $"CUSTOM events were [{string.Join(", ", turn.CustomEvents)}].");

        foreach (var fragment in testCase.ReplyMustContain)
        {
            Assert.True(turn.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"[{testCase.Name}] reply is missing \"{fragment}\". Reply was: {turn.Text}");
        }

        foreach (var fragment in testCase.ReplyMustNotContain)
        {
            Assert.False(turn.Text.Contains(fragment, StringComparison.OrdinalIgnoreCase),
                $"[{testCase.Name}] reply must not contain \"{fragment}\". Reply was: {turn.Text}");
        }
    }
}
