using System.Net;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// Every GET in the committed request catalog must actually resolve against the live API.
/// </summary>
/// <remarks>
/// <para>
/// <c>RUNBOOK.md</c> §4 calls <c>auditworthy.http</c> "the canonical, runnable list of every
/// endpoint", and the file's own header warns that a catalog which lags the code "is how the next
/// agent concludes a working endpoint is missing". Two of its requests had drifted into a 404 and a
/// 405 — which is worse than a missing entry, because a wrong entry gets trusted.
/// </para>
/// <para>
/// This is deliberately a **drift guard**, not two assertions about two endpoints. Pinning only the
/// two that were broken would leave the next drift to be found by a human reading a report months
/// later; walking the file catches the class. It is cheap because every GET here is a
/// parameter-free collection read.
/// </para>
/// </remarks>
[Collection("api")]
public sealed class RequestCatalogTests(IntegrationFixture fixture)
{
    [Fact]
    public async Task Every_GET_in_the_committed_catalog_resolves()
    {
        var catalog = FindRepoFile("auditworthy.http");
        using var client = fixture.AdminClient();

        var failures = new List<string>();
        var checked_ = 0;

        foreach (var raw in await File.ReadAllLinesAsync(catalog))
        {
            var line = raw.Trim();
            if (!line.StartsWith("GET ", StringComparison.Ordinal))
            {
                continue;
            }

            // The catalog is written against a running host, so {{base}} is an absolute URL. The
            // test client already carries the base address, so strip it to a relative path.
            var url = line["GET ".Length..].Trim()
                .Replace("{{base}}", string.Empty, StringComparison.Ordinal)
                .Replace("{{module}}", "compliance", StringComparison.Ordinal);

            // An unresolved placeholder means the request needs a value a human pastes in (an id
            // from a previous response). Those cannot be fired blind, and skipping them is not a
            // gap this test is hiding — no GET in the catalog uses one today.
            if (url.Contains("{{", StringComparison.Ordinal))
            {
                continue;
            }

            checked_++;
            var response = await client.GetAsync(url);

            // 404 = the route does not exist. 405 = it exists under a different verb. Both mean the
            // catalog is lying about how to reach something. Any other status — including 403 —
            // is a real answer from a real route, which is all this guard claims to check.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                failures.Add($"{url} → {(int)response.StatusCode} {response.StatusCode}");
            }
        }

        Assert.True(checked_ > 5, $"Only {checked_} GET requests found in {catalog} — the parser is broken, not the catalog.");
        Assert.True(failures.Count == 0,
            "auditworthy.http documents endpoints that do not resolve:\n  " + string.Join("\n  ", failures));
    }

    /// <summary>Walks up from the test assembly to the repo root, which has no fixed depth in CI.</summary>
    private static string FindRepoFile(string fileName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Could not find {fileName} in any ancestor of {AppContext.BaseDirectory}.");
    }
}
