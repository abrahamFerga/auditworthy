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
    /// <summary>
    /// How many GET requests <c>auditworthy.http</c> held when this guard was last reviewed.
    /// Deliberately a committed number: it is the only thing here that a deleted request cannot
    /// move, because every count taken from the file moves with the file. Update it in the same
    /// commit that changes the catalog.
    /// </summary>
    private const int KnownCatalogGets = 16;

    [Fact]
    public async Task Every_GET_in_the_committed_catalog_resolves()
    {
        var catalog = FindRepoFile("auditworthy.http");
        using var client = fixture.AdminClient();

        var lines = await File.ReadAllLinesAsync(catalog);

        // What the loop is about to walk, counted with the loop's own predicate.
        var declared = lines.Count(l => l.TrimStart().StartsWith("GET ", StringComparison.Ordinal));

        // A SECOND count, taken with a deliberately looser and independent predicate: any leading
        // whitespace, a lowercase verb, a tab instead of a space. Two counts derived from the same
        // rule cannot cross-check each other — `declared` shares the loop's predicate, so both drop
        // to zero together the moment the catalog's format drifts, and an equality between them
        // asserts nothing. This one keeps matching where the strict parser has stopped, so the
        // disagreement is what surfaces the regression.
        var looselyDeclared = lines.Count(LooksLikeGetLine);

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

        // Separate claims, because they fail for different reasons and a combined assertion would
        // report the wrong one.

        // 1. What this guard primarily exists to produce: the endpoints the catalog documents and
        //    the API does not serve. Reported FIRST so a routine, explicitly anticipated event
        //    lower down — someone adding a parameterized GET, which trips claim 5 — cannot mask it.
        Assert.True(failures.Count == 0,
            "auditworthy.http documents endpoints that do not resolve:\n  " + string.Join("\n  ", failures));

        // 2. The strict parser has not silently stopped matching. Two independent predicates over
        //    the same file: they agree today at 15, and they disagree the moment a line is written
        //    as `get `, or with a tab, or indented — all of which the loop would skip in silence
        //    while every count derived from its own rule agreed with it.
        //
        //    Checked BEFORE the pinned count below, and the order carries information: a format
        //    drift drops `declared` too, so the pinned count would also fire — but it would report
        //    a deletion, which is the wrong diagnosis. Whichever assertion fires first is the one a
        //    reader believes, so the more specific claim goes first.
        Assert.True(declared == looselyDeclared,
            $"{catalog}: the strict parser matched {declared} GET line(s), a looser one matched "
            + $"{looselyDeclared}. The catalog's format has drifted away from what this test "
            + "parses, so the lines in the gap are documented but never fired.");

        // 3. Coverage did not shrink. No count taken from the file can catch a DELETED request:
        //    `declared`, `looselyDeclared` and the loop all fall together when a GET is removed,
        //    which is why the previous `declared > 0` was strictly weaker than the `checked_ > 5`
        //    it replaced — under it, fourteen of fifteen could vanish and this still passed. Only a
        //    number committed alongside the catalog holds that line, so here it is. It is checked
        //    for equality, not as a floor: growth has to be acknowledged too, or the constant drifts
        //    upward unnoticed and the next deletion lands back below it undetected.
        Assert.True(declared == KnownCatalogGets,
            $"{catalog} holds {declared} GET request(s); this guard was written against "
            + $"{KnownCatalogGets}. If you added or removed requests deliberately, update "
            + $"{nameof(KnownCatalogGets)} in the same commit — that edit is the record of the "
            + "decision. If you did not, the catalog lost coverage and this is the only check "
            + "that can tell.");

        // 4. Nothing vanished between parsing and firing. An accounting identity, not a guess.
        Assert.True(checkedCount + skipped.Count == declared,
            $"{catalog} declares {declared} GET request(s); {checkedCount} were fired and "
            + $"{skipped.Count} skipped, which does not add up. The loop is dropping requests.");

        // 5. Skips are a failure, not a footnote. Skipping is defensible for a request needing a
        //    value only a human has — but it must be a decision someone makes, not something that
        //    happens quietly. Silence is how coverage shrinks without anyone noticing.
        Assert.True(skipped.Count == 0,
            $"{skipped.Count} catalog GET(s) were skipped for unresolved placeholders:\n  "
            + string.Join("\n  ", skipped)
            + "\nGive the placeholder a value this test can supply, or the catalog keeps a corner "
            + "nothing exercises.");
    }

    /// <summary>
    /// A looser reading of "this line is a GET" than the loop uses, on purpose: leading whitespace,
    /// any casing, and a tab rather than a space all count. Every line the strict parser matches is
    /// also matched here, so the two counts can only diverge in one direction — lines the catalog
    /// intends as requests and the loop silently walks past.
    /// </summary>
    private static bool LooksLikeGetLine(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.Length > 4
            && trimmed.StartsWith("get", StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(trimmed[3])
            && trimmed[4..].TrimStart().Length > 0;
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
