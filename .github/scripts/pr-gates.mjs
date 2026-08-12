#!/usr/bin/env node
// Deterministic pull-request gates (verification ladder L1/L2). No dependencies — node >= 18.
//
//   node .github/scripts/pr-gates.mjs <path-to-unified-diff>
//
//   env: PR_BODY      the pull request body
//        PR_HEAD_REF  the head branch name
//
// Exits 0 when every gate passes, 1 with every failure listed. Runs as a required status check, so
// it is what actually stands between an agent and `main` — the prose in a skill file does not.
//
// Two groups of gates:
//
//   Evidence gates run on every feat/*, fix/*, chore/* and codex/* candidate. Each candidate must
//   open with the same protocol envelope the merger requires and close its source issue. Runtime
//   and red-before-green evidence are also required for every protected diff, including attended
//   work, so permission comes from proof rather than a mutable label.
//
//   The protected-diff detector runs on EVERY pull request. Ordinary configuration and manifest
//   changes may proceed with evidence. The small control-policy root of trust and removals of
//   security invariants are immutable through this path: the evaluator is loaded from the protected
//   base, so a PR cannot authorize its own weakening. Those changes need an administrative
//   bootstrap outside the agent merger.

import { readFileSync, existsSync } from 'node:fs';

const diffPath = process.argv[2];
const body = process.env.PR_BODY ?? '';
const headRef = process.env.PR_HEAD_REF ?? '';

const PROTOCOL_ENVELOPE = /^\s*<!--\s*plenipo-agent\s+kind=(?:platform-request|verdict|upgrade-available|breaking-change|finding|handoff|blocked)\s+from=[a-z0-9._-]+(?:\s+ref=[a-z0-9._-]+#\d+)?\s+status=(?:open|answered|accepted|rejected|blocked|done)\s*-->/i;
const isLoopPr = /^(feat|fix|chore|codex)\//.test(headRef);
const hasProtocolEnvelope = PROTOCOL_ENVELOPE.test(body);

const failures = [];
const passes = [];
const gate = (name, ok, why) => (ok ? passes.push(name) : failures.push(`${name}: ${why}`));

if (isLoopPr) {
  gate(
    'protocol_envelope',
    hasProtocolEnvelope,
    'the body must open with a valid plenipo-agent protocol envelope. The scheduled merger applies the same rule.'
  );
}

// A heading alone is not evidence; require content under it. Split on lines rather than with a
// lookahead: JS has no \Z, and `$` under /m/ would end the section at the first newline.
const section = (heading) => {
  const lines = body.split('\n');
  const head = new RegExp(`^#{1,4}\\s*${heading}\\s*$`, 'i');
  const start = lines.findIndex((line) => head.test(line.trim()));
  if (start === -1) return '';
  const rest = lines.slice(start + 1);
  const end = rest.findIndex((line) => /^#{1,4}\s/.test(line));
  return (end === -1 ? rest : rest.slice(0, end)).join('\n').trim();
};

// ── Protected-diff detector — every pull request ────────────────────────────
const LOCKED_CONTROL_PATHS = new Set([
  '.github/scripts/pr-gates.mjs',
  '.github/scripts/merge-gate.mjs',
  '.github/workflows/agent-gates.yml',
  '.github/workflows/agent-merge.yml',
  '.github/workflows/ci.yml',
  'workflow.json',
  'plugins/plenipo/skills/setup/assets/pr-gates.mjs',
  'plugins/plenipo/skills/setup/assets/merge-gate.mjs',
  'plugins/plenipo/skills/setup/assets/agent-gates.yml',
  'plugins/plenipo/skills/setup/assets/agent-merge.yml',
]);

const CONTENT_RULES = [
  [/HasQueryFilter/, 'tenant isolation (HasQueryFilter)'],
  [/RequiresApproval/, 'the approval gate (RequiresApproval)'],
  [/AddPlenipoRole/, 'a role baseline (AddPlenipoRole)'],
  [/Permissions\s*\./, 'a permission string'],
];
// These symbols are executable invariants, not magic words. Scanning Markdown made ordinary docs
// edits look like tenant-isolation removals. Restrict removal checks to source files while still
// retaining the old source path across a rename or deletion.
const INVARIANT_SOURCE_PATH = /\.(?:cs|csx|fs|fsx|vb|ts|tsx|js|jsx|razor)$/i;

// ── Guard tests — an explicit list, deliberately not a heuristic ──────────────
// LOCAL TO AUDITWORTHY. The rules above are blind to a whole class of change: deleting an
// *assertion* that guards the spine. PR #32 removed two approval-lane assertions and this check
// reported green, because none of the CONTENT_RULES strings appear in a diff that only deletes
// `Assert.` lines. Its green was silence, not a verdict.
//
// Scoped to a named list rather than "any removed Assert." on purpose. Flagging every removed
// assertion fires on ordinary refactoring — merging two tests, replacing three weak asserts with one
// strong one — and a gate that cries wolf gets muted within a week, which is worse than the gap it
// was closing. These three files are the ones asserting the approval gate, the analyst boundary, and
// the manifest/tenant-filter invariants; losing an assertion in any of them is a change to what the
// product can still prove about itself.
//
// Routed to lockedHits rather than hits: under the previous policy this was overridable with the
// `human-approved` label, but the label lane is gone and an assertion guarding the approval gate is
// an executable invariant, which this file already treats as immutable. Not upstream yet — worth
// filing to plenipo-agents so every product inherits it.
const GUARD_TESTS = [
  [/(^|\/)ChatAndApprovalTests\.cs$/, 'the approval gate'],
  [/(^|\/)RoleBaselineTests\.cs$/, 'the analyst permission boundary'],
  [/(^|\/)ManifestGuardTests\.cs$/, 'the manifest and tenant-filter invariants'],
];
const ASSERTION = /^\s*Assert\./;

const PATH_RULES = [
  [/^\.github\//, 'CI and workflow configuration'],
  [/(^|\/)CODEOWNERS$/, 'CODEOWNERS'],
  [/(^|\/)nuget\.config$/i, 'the package feed'],
  [/appsettings[^/]*\.json$/i, 'runtime configuration'],
  [/^eng\//, 'the verifier itself'],
  [/(^|\/)\.claude-plugin\//, 'plugin and marketplace manifests'],
  [/^workflow\.json$/, 'the autonomy policy'],
];

if (!diffPath || !existsSync(diffPath)) {
  console.error(`pr-gates: cannot read the diff at "${diffPath}"`);
  process.exit(1);
}

const hits = [];
const lockedHits = [];
let oldFile = '';
let file = '';
const recordProtectedPath = (rawPath) => {
  const path = rawPath.replace(/^[ab]\//, '').trim();
  if (!path || path === '/dev/null') return path;
  if (LOCKED_CONTROL_PATHS.has(path)) {
    lockedHits.push(`${path} — immutable merge-policy control`);
  }
  for (const [rule, what] of PATH_RULES) {
    if (rule.test(path)) hits.push(`${path} — ${what}`);
  }
  return path;
};

for (const line of readFileSync(diffPath, 'utf8').split('\n')) {
  // A pure Git rename has no ---/+++ headers, so preserve the old protected path explicitly.
  if (line.startsWith('rename from ')) {
    oldFile = recordProtectedPath(line.slice('rename from '.length));
    file = oldFile;
    continue;
  }
  if (line.startsWith('rename to ')) {
    file = recordProtectedPath(line.slice('rename to '.length));
    continue;
  }
  if (line.startsWith('--- ')) {
    oldFile = recordProtectedPath(line.slice(4));
    if (oldFile !== '/dev/null') file = oldFile;
    continue;
  }
  if (line.startsWith('+++ ')) {
    const newFile = recordProtectedPath(line.slice(4));
    file = newFile === '/dev/null' ? oldFile : newFile;
    continue;
  }
  // A modified line shows up as both '-' and '+', so scanning removals catches edits AND deletions
  // while leaving pure additions alone.
  if (line.startsWith('-') && !line.startsWith('---')) {
    const invariantPath = oldFile && oldFile !== '/dev/null' ? oldFile : file;
    if (INVARIANT_SOURCE_PATH.test(invariantPath)) {
      for (const [rule, what] of CONTENT_RULES) {
        if (rule.test(line)) {
          lockedHits.push(`${invariantPath} — removes or edits ${what}: ${line.slice(1).trim()}`);
        }
      }
      // Same removal-scan logic, so an assertion that was *edited* (which appears as both '-' and
      // '+') is caught alongside one deleted outright. Weakening an assertion and deleting it end in
      // the same place: the product can no longer prove the thing.
      const guard = GUARD_TESTS.find(([rule]) => rule.test(invariantPath));
      if (guard && ASSERTION.test(line.slice(1))) {
        lockedHits.push(
          `${invariantPath} — removes or edits an assertion guarding ${guard[1]}: ${line.slice(1).trim()}`
        );
      }
    }
  }
}

const controlHits = [...new Set(lockedHits)];
if (controlHits.length) {
  failures.push(
    'control_policy_locked: this PR edits an immutable merge control or removes a security invariant.\n' +
      controlHits.map((hit) => `      - ${hit}`).join('\n') +
      '\n      The protected-base evaluator cannot authorize changes to its own root of trust; use the repository administrative bootstrap path.'
  );
}

const protectedHits = [...new Set(hits)];
if (protectedHits.length) {
  passes.push(
    'protected_diff_detected\n' + protectedHits.map((hit) => `      - ${hit}`).join('\n')
  );
}

// ── Evidence gates — loop branches and every protected diff ─────────────────
if (isLoopPr) {
  gate(
    'closes_an_issue',
    /\bcloses #\d+/i.test(body),
    'the body has no "Closes #<n>". Without it the issue never closes and the board rots.'
  );
}

const requiresEvidence = isLoopPr || protectedHits.length > 0;
if (requiresEvidence) {
  const evidence = section('Runtime evidence');
  gate(
    'has_runtime_evidence',
    evidence.length > 40,
    'no substantive "## Runtime evidence" section with the request exercised and the output observed. ' +
      'A green build proves the code is well formed and nothing else.'
  );

  const regression = section('Regression test');
  gate(
    'has_red_before_green',
    /\bred\b/i.test(regression) && /\bgreen\b/i.test(regression),
    'no "## Regression test" section stating the test was seen red before the fix and green after. ' +
      'A test never seen red is not a regression test.'
  );

}

// ── Report ───────────────────────────────────────────────────────────────────
for (const pass of passes) console.log(`  ok   ${pass}`);
if (failures.length) {
  console.log('');
  for (const failure of failures) console.log(`  FAIL ${failure}`);
  console.log(`\n${failures.length} gate(s) failed.\n`);
  process.exit(1);
}
const scopeNote = requiresEvidence ? '' : ' (not a loop branch: evidence gates skipped)';
console.log(`\nOK — ${passes.length} gate(s) passed${scopeNote}.\n`);
