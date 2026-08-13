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
/// <para>
/// <b>Needle collision is this guard's characteristic failure mode.</b> Every lookup here is
/// "first line containing a substring", and the block is full of prose — comments explaining the
/// code, and <c>throw</c> messages quoting the very tokens being searched for — that contains the
/// same substrings as the code. A needle that matches prose first stops guarding the code: the
/// assertion is satisfied by a sentence *about* the behaviour while the behaviour itself is gone.
/// Four of the seven lookups shipped that way and were caught by mutation testing (PR #91 review;
/// the <c>.ExitCode</c> one was the finding that sent the PR back):
/// <list type="bullet">
/// <item><c>docker run</c> also matched the comment that explains what <c>docker run -d</c>
/// returns, so deleting the command left the not-vacuous check green.</item>
/// <item><c>pg_isready</c> would match a comment naming it, so replacing the poll with
/// "# the pg_isready poll used to live here" left both readiness assertions green.</item>
/// <item><c>/alive</c> matched the <c>throw</c> message "before /alive answered" two lines
/// earlier than the actual request, so deleting the poll left the check green.</item>
/// <item><c>.ExitCode</c> matched the comment "without it $api.ExitCode is EMPTY" seven lines
/// before the throw that reports it, so stripping the exit code — deleting exactly what #71
/// asked for — left the check green.</item>
/// </list>
/// The fix is to make each needle match only real code: anchor on the command at the start of a
/// line, on the interpolated <c>$($api.ExitCode)</c> form that only a throw uses, or on a request
/// verb preceding the URL. <b>Every assertion below has been mutation-tested: delete the behaviour
/// it names and it goes red.</b> If you add one, prove it red the same way — a guard never seen
/// red may be asserting nothing.
/// </para>
/// </remarks>
public sealed class ModeBReadinessConventionTests
{
    /// <summary>The heading that opens §2's Mode B block. Anchored exactly: a renamed heading must
    /// fail this guard loudly rather than let it quietly find nothing to check.</summary>
    private const string ModeBHeading = "### Mode B — headless";

    /// <summary>The container-start command itself, at the start of a line — not the comment above
    /// it that explains what <c>docker run -d</c> returns.</summary>
    private static readonly Regex DockerRunCommand = new(@"^\s*docker\s+run\b", RegexOptions.IgnoreCase);

    /// <summary>A <c>pg_isready</c> that is executed, not one named in a comment.</summary>
    private static readonly Regex PgIsReadyCall = new(@"^\s*[^#]*pg_isready", RegexOptions.IgnoreCase);

    /// <summary>An actual request to <c>/alive</c>. The bare path also appears in two <c>throw</c>
    /// messages, one of them *before* the real poll, so the request verb is what makes this
    /// assertion able to fail when the poll is deleted.</summary>
    private static readonly Regex AliveRequest =
        new(@"(iwr|curl|Invoke-WebRequest|Invoke-RestMethod)\b[^\n]*/alive", RegexOptions.IgnoreCase);

    [Fact]
    public void Mode_b_waits_for_postgres_and_notices_a_dead_api()
    {
        var runbook = Path.Combine(FindRepoRoot(), "RUNBOOK.md");
        var block = ModeBScriptBlock(File.ReadAllLines(runbook));

        var dockerRun = IndexOfMatch(block, DockerRunCommand);
        var startProcess = IndexOf(block, "Start-Process");

        // 0. The walk found a block that really is Mode B, so nothing below passes vacuously.
        //    Anchored on the `docker run` *command*: the comment four lines below it also contains
        //    the words "docker run", and matching that let the command be deleted with this green.
        Assert.True(dockerRun >= 0 && startProcess >= 0,
            "The block under \"" + ModeBHeading + "\" no longer contains both a `docker run` "
            + "command and `Start-Process`, so this guard is not looking at Mode B and is "
            + $"asserting nothing about it. docker run at line {dockerRun}, Start-Process at line "
            + $"{startProcess} (-1 = absent).\nBlock read:\n{block}");

        // 1. The defect itself: Postgres readiness is waited for, between creating the container and
        //    starting the API. Position matters — a pg_isready after Start-Process is decoration.
        var pgIsReady = IndexOfMatch(block, PgIsReadyCall);

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
            pgIsReady >= 0
            && LinesAround(block, pgIsReady, 4).Any(l => Regex.IsMatch(l, @"1\.\.\d+|while|until|for\s*\(")),
            "The `pg_isready` in Mode B is a single call, not a retry loop, so a cold container "
            + "that is not ready on the first ask still crashes the API (#71). Poll it until it "
            + $"succeeds, and fail loudly if it never does. Line found: {pgWaitLine}");

        // 3. The verification-integrity half: the /alive poll must inspect the process it started.
        //    `catch {}` around an HTTP call cannot distinguish "connection refused because the host
        //    is still booting" from "connection refused because the host is a corpse".
        //    Matched on the request verb, not on the bare path: `/alive` also appears inside the
        //    "API exited … before /alive answered" throw two lines *earlier* than the real poll, so
        //    the bare needle stayed green with the request deleted.
        var alivePoll = IndexOfMatch(block, AliveRequest);

        Assert.True(alivePoll > startProcess,
            "Mode B no longer issues a request to `/alive` after `Start-Process`, so it has no "
            + $"readiness signal at all. /alive request at line {alivePoll}, Start-Process at line "
            + $"{startProcess} (-1 = absent). A `/alive` mentioned only in a throw message is not a "
            + "poll.");

        var hasExited = IndexOf(block, "HasExited");

        Assert.True(hasExited > startProcess,
            "Mode B's `/alive` readiness loop cannot tell \"not up yet\" from \"already dead\": it "
            + "wraps the request in `catch {}` and polls on regardless, so a host that crashed "
            + "three seconds in is indistinguishable from one that is still starting, for two full "
            + "minutes, and then prints nothing (#71). Check `$api.HasExited` inside the loop and "
            + "stop with the exit code. Mode B is the scripted-verification path — a loop that "
            + "reports a crash as a timeout makes every proof run through it untrustworthy.");

        // The interpolated `$($api.ExitCode)`, which only a message that reports the code can
        // contain. Two shorter needles were tried and both matched prose: `ExitCode` matches the
        // readiness wait's `$LASTEXITCODE`, and `.ExitCode` matches the comment on the
        // `$null = $api.Handle` line — which is *before* the loop, so stripping the code from the
        // throw left this green. Bounded on `hasExited` for the same reason: the report belongs
        // inside the loop that detects the death, and the bound is only meaningful once the needle
        // can no longer match a comment that precedes it.
        var exitCode = IndexOf(block, "$($api.ExitCode)");

        Assert.True(exitCode > hasExited,
            "Mode B notices that the API exited but does not report `$api.ExitCode`, which is the "
            + "one value that names the failure (#71 was -532462766). Surface it inside the "
            + "readiness loop, interpolated — an agent reading this output is the whole audience "
            + $"for Mode B. Found $($api.ExitCode) at line {exitCode}, HasExited at line "
            + $"{hasExited} (-1 = absent).");

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

    /// <summary>Zero-based line index of the first line matching <paramref name="pattern"/>, or -1.
    /// The regex counterpart of <see cref="IndexOf"/>, used wherever a plain substring would also
    /// match a comment or a throw message rather than the code being guarded.</summary>
    private static int IndexOfMatch(string block, Regex pattern)
    {
        var lines = block.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(lines[i]))
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
