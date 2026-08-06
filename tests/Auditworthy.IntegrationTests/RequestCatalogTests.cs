using System.Linq;
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

        var lines = await File.ReadAllLinesAsync(catalog);

        // Derived from the file, never hardcoded. The previous `> 5` was a guess against a catalog
        // that actually holds 15 GETs, so ten could have vanished — through a format change the
        // parser stopped matching, or requests being deleted — while the guard still reported
        // success over a third of its claimed coverage. A floor that does not track the file is a
        // floor that stops meaning anything the first time the file grows.
        var declared = lines.Count(l => l.TrimStart().StartsWith("GET ", StringComparison.Ordinal));

        var failures = new List<string>();
        var skipped = new List<string>();
        var checkedCount = 0;

        foreach (var raw in lines)
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
            // from a previous response), and cannot be fired blind. Recorded rather than silently
            // dropped: skipping is a legitimate answer, but a *silent* skip lets coverage shrink
            // without anyone noticing — the day someone adds `GET {{base}}/api/jobs/{{jobId}}`, the
            // catalog grows an untested corner and the guard still says everything resolves.
            if (url.Contains("{{", StringComparison.Ordinal))
            {
                skipped.Add(url);
                continue;
            }

            checkedCount++;
            var response = await client.GetAsync(url);

            // 404 = the route does not exist. 405 = it exists under a different verb. Both mean the
            // catalog is lying about how to reach something. Any other status — including 403 —
            // is a real answer from a real route, which is all this guard claims to check.
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            {
                failures.Add($"{url} → {(int)response.StatusCode} {response.StatusCode}");
            }
        }

        // Three separate claims, because they fail for different reasons and a combined assertion
        // would report the wrong one.

        // 1. The parser still matches something at all. `declared` and the loop share a predicate,
        //    so they cannot diverge — which means equality alone can NEVER catch a format change
        //    that stops both matching. Only a nonzero check can, and it needs no maintained number.
        Assert.True(declared > 0,
            $"No GET requests parsed from {catalog}. Either the catalog is empty, or its format "
            + "changed and this parser silently matches nothing — in which case every claim below "
            + "is vacuous.");

        // 2. Nothing vanished between parsing and firing. An accounting identity, not a guess.
        Assert.True(checkedCount + skipped.Count == declared,
            $"{catalog} declares {declared} GET request(s); {checkedCount} were fired and "
            + $"{skipped.Count} skipped, which does not add up. The loop is dropping requests.");

        // 3. Skips are a failure, not a footnote. Skipping is defensible for a request needing a
        //    value only a human has — but it must be a decision someone makes, not something that
        //    happens quietly. Silence is how coverage shrinks without anyone noticing.
        Assert.True(skipped.Count == 0,
            $"{skipped.Count} catalog GET(s) were skipped for unresolved placeholders:\n  "
            + string.Join("\n  ", skipped)
            + "\nGive the placeholder a value this test can supply, or the catalog keeps a corner "
            + "nothing exercises.");

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
