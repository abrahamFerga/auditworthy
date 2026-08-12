using System.Text.RegularExpressions;
using Xunit;

namespace Auditworthy.IntegrationTests;

/// <summary>
/// Every committed dev-auth caller that sends <c>X-Dev-Subject</c> must send <c>X-Dev-Name</c> and
/// <c>X-Dev-Email</c> beside it.
/// </summary>
/// <remarks>
/// <para>
/// #64's fix is a convention — "every caller remembers a header" — and a convention with nothing
/// enforcing it is a comment. The proof is that the PR which introduced the rule broke it in its
/// own diff: <c>RUNBOOK.md</c> §3 said "change <c>X-Dev-Name</c> whenever you change
/// <c>X-Dev-Subject</c>", and §4's copy-paste snippet twenty-four lines later did not send it at
/// all. One caller in five, missed while writing the rule down.
/// </para>
/// <para>
/// <c>Two_dev_subjects_are_two_different_names_in_the_audit</c> cannot catch that. It exercises
/// <see cref="IntegrationFixture.AdminClient"/>, so it stays green while <c>auditworthy.http</c> and
/// <c>RUNBOOK.md</c> rot — and <c>IdentityAttributionTests</c> already shows new test files
/// hand-rolling their own <see cref="HttpClient"/> rather than going through the fixture, so the
/// failure mode recurs by construction rather than by accident.
/// </para>
/// <para>
/// This walks the repository instead, so it holds for callers nobody thought to write a test for.
/// It fires no requests and needs no host or Docker — it is a text guard, deliberately outside the
/// <c>api</c> collection, because a rule about what the committed files say should not be able to
/// fail for want of a container.
/// </para>
/// </remarks>
public sealed class DevAuthHeaderConventionTests
{
    /// <summary>
    /// How many dev-auth call sites the repository held when this guard was last reviewed.
    /// Committed for the same reason as <c>RequestCatalogTests.KnownCatalogGets</c>: every count
    /// taken from the files moves with the files, so only a number outside them can notice that the
    /// parser has stopped matching or that callers were deleted. Update it in the same commit that
    /// adds or removes one.
    /// </summary>
    private const int KnownDevAuthCallSites = 24;

    /// <summary>
    /// How far from an <c>X-Dev-Subject</c> its <c>X-Dev-Name</c> and <c>X-Dev-Email</c> may sit. A
    /// dev-auth caller is a contiguous block of headers in every form this repo uses — an
    /// <c>.http</c> request, a PowerShell hashtable, five <c>DefaultRequestHeaders.Add</c> lines —
    /// and the widest real gap today is four lines (<c>IntegrationFixture.cs:80</c> to <c>:84</c>,
    /// where the email is the last of the five). Five leaves room for one more header without
    /// licensing a match from an unrelated block.
    /// </summary>
    private const int PairingWindow = 5;

    /// <summary>
    /// A line that <em>sends</em> the header, as opposed to one that merely names it in prose.
    /// The header must be followed by something that supplies a value, which covers all three
    /// committed shapes — <c>X-Dev-Subject: value</c>, <c>"X-Dev-Subject"="value"</c> and
    /// <c>Add("X-Dev-Subject", value)</c> — and excludes <c>RUNBOOK.md:155</c>, where the header is
    /// inside backticks in a sentence about the rule itself.
    /// </summary>
    private static Regex Sends(string header) =>
        new(Regex.Escape(header) + "\"?\\s*[:=,]\\s*\\S", RegexOptions.IgnoreCase);

    private static readonly Regex SendsSubject = Sends("X-Dev-Subject");
    private static readonly Regex SendsName = Sends("X-Dev-Name");
    private static readonly Regex SendsEmail = Sends("X-Dev-Email");

    /// <summary>Build output, vendored packages, and the throwaway agent worktrees — which are
    /// whole second copies of this repo and would otherwise double every count here.</summary>
    private static readonly string[] SkippedDirectories =
        [".git", ".vs", ".idea", "bin", "obj", "dist", "node_modules", "data", "worktrees", ".packages"];

    private static readonly string[] TextExtensions =
        [".http", ".md", ".cs", ".ps1", ".sh", ".py", ".ts", ".tsx", ".js", ".json", ".yml", ".yaml"];

    [Fact]
    public void Every_dev_auth_caller_sends_a_display_name_and_an_email()
    {
        var root = FindRepoRoot();
        var files = TextFilesUnder(root).ToList();

        var failures = new List<string>();
        var callSites = 0;
        var filesWithCallSites = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);

            var subjects = LinesMatching(lines, SendsSubject);
            var names = LinesMatching(lines, SendsName);
            var emails = LinesMatching(lines, SendsEmail);

            if (subjects.Count == 0)
            {
                continue;
            }

            callSites += subjects.Count;
            filesWithCallSites.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));

            foreach (var subjectLine in subjects)
            {
                var missing = new List<string>();

                if (!names.Any(n => Math.Abs(n - subjectLine) <= PairingWindow))
                {
                    missing.Add("X-Dev-Name");
                }

                if (!emails.Any(e => Math.Abs(e - subjectLine) <= PairingWindow))
                {
                    missing.Add("X-Dev-Email");
                }

                if (missing.Count == 0)
                {
                    continue;
                }

                failures.Add(
                    $"{Path.GetRelativePath(root, file).Replace('\\', '/')}:{subjectLine + 1}: "
                    + $"missing {string.Join(" and ", missing)} — {lines[subjectLine].Trim()}");
            }
        }

        // 1. The claim this guard exists to make. Reported first: it is the specific defect, and the
        //    coverage assertions below can fire for unrelated, routine reasons.
        Assert.True(failures.Count == 0,
            "These committed callers send X-Dev-Subject without an X-Dev-Name and/or an "
            + "X-Dev-Email beside it, so every subject they reach the API as is the constant "
            + "\"Dev User\" at the constant \"dev@plenipo.local\" — two different people, one "
            + "indistinguishable actor in the append-only audit, in the approvals queue's proposer "
            + "and in Admin → Users (#64):\n  "
            + string.Join("\n  ", failures)
            + "\nSend both, derived from the subject. See RUNBOOK.md §3.");

        // 2. The walk actually reached the repository, rather than finding an empty tree and
        //    concluding that a rule nothing violates is a rule everything obeys. Named files, not a
        //    count: if root discovery breaks in CI, every count below also goes to zero together.
        Assert.True(
            filesWithCallSites.Contains("auditworthy.http") && filesWithCallSites.Contains("RUNBOOK.md"),
            "The two canonical dev-auth callers were not found by the walk, so this guard checked "
            + $"nothing. Walked {files.Count} file(s) under {root}; found call sites in: "
            + (filesWithCallSites.Count == 0 ? "(none)" : string.Join(", ", filesWithCallSites)));

        // 3. Coverage did not shrink and the parser did not silently stop matching. Equality, not a
        //    floor: growth has to be acknowledged too, or the constant drifts and the next deletion
        //    lands underneath it unnoticed.
        Assert.True(callSites == KnownDevAuthCallSites,
            $"Found {callSites} dev-auth call site(s); this guard was written against "
            + $"{KnownDevAuthCallSites}. If you added or removed callers deliberately, update "
            + $"{nameof(KnownDevAuthCallSites)} in the same commit — that edit is the record of the "
            + "decision. If you did not, either callers were deleted or the shapes this test parses "
            + "have drifted, and it is now guarding less than it claims.");
    }

    /// <summary>
    /// The catalog's <c>@name</c> and <c>@email</c> must be what the subject derives, so swapping
    /// <c>@subject</c> and leaving either behind fails the build.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Raised by the #68 review as the gap the pairing guard above cannot see: it checks that a
    /// header is <em>sent</em>, not that it <em>agrees</em> with the subject beside it.
    /// <c>auditworthy.http</c> defines the three as independent variables, and the file's own
    /// instruction is to swap <c>@subject</c> to assert an RBAC boundary — so a reader who does
    /// that and forgets the other two reproduces #64 across all 19 requests with the pairing guard
    /// still green.
    /// </para>
    /// <para>
    /// This closes the committed half of that. It cannot close the other half: an edit made in the
    /// working copy and never committed is invisible to a test that reads files, and no test can
    /// reach it. That residue is real and is called out in <c>auditworthy.http</c>'s own preamble
    /// rather than left for the next reviewer to rediscover.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_catalog_preamble_derives_its_name_and_email_from_its_subject()
    {
        var catalog = Path.Combine(FindRepoRoot(), "auditworthy.http");
        var lines = File.ReadAllLines(catalog);

        var subject = PreambleVariable(lines, "subject");
        var name = PreambleVariable(lines, "name");
        var email = PreambleVariable(lines, "email");

        Assert.Equal(IntegrationFixture.DevDisplayName(subject), name);
        Assert.Equal(IntegrationFixture.DevEmail(subject), email);
    }

    /// <summary>
    /// Reads a <c>@key = value</c> from the catalog preamble. Fails loudly rather than returning a
    /// default: a variable that has been renamed away must break this test, not silently satisfy it
    /// by comparing two empty strings.
    /// </summary>
    private static string PreambleVariable(string[] lines, string key)
    {
        var pattern = new Regex("^@" + Regex.Escape(key) + @"\s*=\s*(?<value>.+?)\s*$");

        foreach (var line in lines)
        {
            var match = pattern.Match(line);

            if (match.Success)
            {
                return match.Groups["value"].Value;
            }
        }

        throw new InvalidOperationException(
            $"auditworthy.http no longer defines @{key}. The dev-auth preamble is @subject, @name "
            + "and @email as a set — if one was renamed or removed, this guard is checking nothing "
            + "and the catalog can drift back into #64 unobserved.");
    }

    private static List<int> LinesMatching(string[] lines, Regex pattern)
    {
        var hits = new List<int>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsCommentProse(lines[i]) && pattern.IsMatch(lines[i]))
            {
                hits.Add(i);
            }
        }

        return hits;
    }

    /// <summary>
    /// A line of commentary is not a caller, however faithfully it quotes one. This file is the
    /// proof: its own <c>&lt;summary&gt;</c> quotes all three sending shapes verbatim, and without
    /// this the guard reports itself. Commented-out callers are excluded on the same reasoning —
    /// code that does not run does not reach the API as anyone.
    /// </summary>
    private static bool IsCommentProse(string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith('*')
            || trimmed.StartsWith("<!--", StringComparison.Ordinal);
    }

    private static IEnumerable<string> TextFilesUnder(string root)
    {
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            var dir = pending.Pop();

            foreach (var child in Directory.EnumerateDirectories(dir))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
                {
                    pending.Push(child);
                }
            }

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>
    /// Walks up from the test assembly to the repo root, which has no fixed depth in CI. Anchored on
    /// the request catalog because it is the file this guard is most about.
    /// </summary>
    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "auditworthy.http")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find the repo root (a directory holding auditworthy.http) above {AppContext.BaseDirectory}.");
    }
}
