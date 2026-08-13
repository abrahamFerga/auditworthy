using System.Text.RegularExpressions;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// <c>RUNBOOK.md</c> §2's Mode B block must wait for Postgres before it starts the API, and its
/// <c>/alive</c> poll must be able to tell "not up yet" from "already dead".
/// </summary>
/// <remarks>
/// <para>
/// #71: the block did <c>docker run -d … pgvector/pgvector:pg17</c> and started the API in the very
/// next statement. On a container created fresh, Postgres is still initialising and tears the
/// connection down mid-TLS, so the host dies about three seconds in with
/// <c>Npgsql … EndOfStreamException</c> and exit code <c>-532462766</c>. Two cold attempts failed;
/// two warm attempts succeeded, which is exactly why the defect survived — the second person to run
/// the block has a warm container and cannot reproduce it.
/// </para>
/// <para>
/// The second half is the one that matters more here. The block's readiness loop was
/// <c>1..60 { Start-Sleep 2; try { …/alive… } catch {} }</c>, which swallows every failure — so it
/// spent two minutes polling a process that had already exited and then printed nothing. Mode B is
/// the *scripted verification* path: an agent uses it to prove a change works. A readiness loop
/// that cannot distinguish "not up yet" from "dead" does not merely waste two minutes, it reports a
/// crashed host as an inconclusive one, which is a verification-integrity failure.
/// </para>
/// <para>
/// This is a text guard over the committed runbook, in the shape
/// <c>DevAuthHeaderConventionTests</c> established: it fires no requests and needs no Docker,
/// because a rule about what a committed file says must not be able to fail for want of a
/// container. It cannot prove the block boots — only the runtime run in #71's PR can do that. What
/// it can do is stop the two waits being deleted again by someone tidying the block.
/// </para>
/// </remarks>
public sealed class ModeBReadinessConventionTests
{
    /// <summary>The heading that opens §2's Mode B block. Anchored exactly: a renamed heading must
    /// fail this guard loudly rather than let it quietly find nothing to check.</summary>
    private const string ModeBHeading = "### Mode B — headless";

    [Fact]
    public void Mode_b_waits_for_postgres_and_notices_a_dead_api()
    {
        var runbook = Path.Combine(FindRepoRoot(), "RUNBOOK.md");
        var block = ModeBScriptBlock(File.ReadAllLines(runbook));

        // 0. The walk found a block that really is Mode B, so nothing below passes vacuously.
        Assert.True(
            block.Contains("docker run", StringComparison.OrdinalIgnoreCase)
            && block.Contains("Start-Process", StringComparison.OrdinalIgnoreCase),
            "The block under \"" + ModeBHeading + "\" no longer contains both `docker run` and "
            + "`Start-Process`, so this guard is not looking at Mode B and is asserting nothing "
            + $"about it. Block read:\n{block}");

        var dockerRun = IndexOf(block, "docker run");
        var startProcess = IndexOf(block, "Start-Process");

        // 1. The defect itself: Postgres readiness is waited for, between creating the container and
        //    starting the API. Position matters — a pg_isready after Start-Process is decoration.
        var pgIsReady = IndexOf(block, "pg_isready");

        Assert.True(pgIsReady > dockerRun && pgIsReady < startProcess,
            "Mode B starts the API without waiting for Postgres to accept connections (#71). "
            + "`docker run -d` returns as soon as the container is created, not when Postgres is "
            + "serving; on a cold container the host dies mid-TLS with "
            + "`Npgsql … EndOfStreamException` and exit code -532462766. Put a `pg_isready` poll "
            + "between `docker run` and `Start-Process`. Found: docker run at line "
            + $"{dockerRun}, pg_isready at line {pgIsReady}, Start-Process at line {startProcess} "
            + $"(-1 = absent).\nBlock read:\n{block}");

        // 2. And it is a *wait*, not one hopeful call — a single pg_isready on a cold container
        //    fails, and a block that ignores that failure is back where it started.
        var pgWaitLine = LineAt(block, pgIsReady);

        Assert.True(
            Regex.IsMatch(block, @"pg_isready", RegexOptions.IgnoreCase)
            && LinesAround(block, pgIsReady, 4).Any(l => Regex.IsMatch(l, @"1\.\.\d+|while|until|for\s*\(")),
            "The `pg_isready` in Mode B is a single call, not a retry loop, so a cold container "
            + "that is not ready on the first ask still crashes the API (#71). Poll it until it "
            + $"succeeds, and fail loudly if it never does. Line found: {pgWaitLine}");

        // 3. The verification-integrity half: the /alive poll must inspect the process it started.
        //    `catch {}` around an HTTP call cannot distinguish "connection refused because the host
        //    is still booting" from "connection refused because the host is a corpse".
        var alivePoll = IndexOf(block, "/alive");

        Assert.True(alivePoll > startProcess,
            "Mode B no longer polls `/alive` after `Start-Process`, so it has no readiness signal "
            + $"at all. /alive at line {alivePoll}, Start-Process at line {startProcess}.");

        var hasExited = IndexOf(block, "HasExited");

        Assert.True(hasExited > startProcess,
            "Mode B's `/alive` readiness loop cannot tell \"not up yet\" from \"already dead\": it "
            + "wraps the request in `catch {}` and polls on regardless, so a host that crashed "
            + "three seconds in is indistinguishable from one that is still starting, for two full "
            + "minutes, and then prints nothing (#71). Check `$api.HasExited` inside the loop and "
            + "stop with the exit code. Mode B is the scripted-verification path — a loop that "
            + "reports a crash as a timeout makes every proof run through it untrustworthy.");

        // `.ExitCode`, not `ExitCode`: the readiness wait above reads `$LASTEXITCODE`, which
        // contains the shorter needle and made this assertion pass on the wrong line.
        var exitCode = IndexOf(block, ".ExitCode");

        Assert.True(exitCode > startProcess,
            "Mode B notices that the API exited but does not report `$api.ExitCode`, which is the "
            + "one value that names the failure (#71 was -532462766). Surface it — an agent reading "
            + $"this output is the whole audience for Mode B. Found .ExitCode at line {exitCode}, "
            + $"Start-Process at line {startProcess} (-1 = absent).");

        // `$api.ExitCode` is an empty string unless the handle was cached while the process was
        // still alive, so a block that reports the code without this reports nothing (#71).
        // Bounded on the poll loop, not on the `.ExitCode` read: the handle must be cached while
        // the process is still alive, which means before the loop that watches it die.
        var handle = IndexOf(block, ".Handle");

        Assert.True(handle > startProcess && handle < hasExited,
            "Mode B reads `$api.ExitCode` without caching the process handle first, so it prints an "
            + "empty exit code and the crash goes unnamed after all. Add `$null = $api.Handle` "
            + "immediately after `Start-Process` — verified against a real crash: without it "
            + "`$api.ExitCode` is '', with it it is -532462766.");
    }

    /// <summary>
    /// The first fenced code block after the Mode B heading. Throws rather than returning empty:
    /// a guard that cannot find its subject must fail, not pass.
    /// </summary>
    private static string ModeBScriptBlock(string[] lines)
    {
        var heading = Array.FindIndex(lines, l => l.StartsWith(ModeBHeading, StringComparison.Ordinal));

        if (heading < 0)
        {
            throw new InvalidOperationException(
                $"RUNBOOK.md no longer has a \"{ModeBHeading}\" heading. If Mode B was renamed, "
                + "update this guard in the same commit — otherwise the cold-start waits #71 added "
                + "are unprotected and can be deleted without anything going red.");
        }

        var open = Array.FindIndex(lines, heading, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));

        if (open < 0)
        {
            throw new InvalidOperationException(
                $"No fenced code block follows \"{ModeBHeading}\" in RUNBOOK.md.");
        }

        var close = Array.FindIndex(lines, open + 1, l => l.TrimStart().StartsWith("```", StringComparison.Ordinal));

        if (close < 0)
        {
            throw new InvalidOperationException(
                $"The code block after \"{ModeBHeading}\" in RUNBOOK.md is never closed.");
        }

        return string.Join('\n', lines[(open + 1)..close]);
    }

    /// <summary>Zero-based line index of the first line containing <paramref name="needle"/>, or -1.
    /// Comparisons in the assertions are ordering comparisons, so -1 sorts before everything and an
    /// absent token reads as "missing" rather than accidentally satisfying a bound.</summary>
    private static int IndexOf(string block, string needle)
    {
        var lines = block.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string LineAt(string block, int index)
    {
        var lines = block.Split('\n');

        return index >= 0 && index < lines.Length ? lines[index].Trim() : "(absent)";
    }

    private static IEnumerable<string> LinesAround(string block, int index, int window)
    {
        if (index < 0)
        {
            return [];
        }

        var lines = block.Split('\n');
        var from = Math.Max(0, index - window);
        var to = Math.Min(lines.Length, index + window + 1);

        return lines[from..to];
    }

    /// <summary>Walks up from the test assembly to the repo root, anchored on the runbook this
    /// guard reads.</summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "RUNBOOK.md")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repo root (a directory holding RUNBOOK.md) above {AppContext.BaseDirectory}.");
    }
}
