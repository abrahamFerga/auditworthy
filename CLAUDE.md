# CLAUDE.md

@AGENTS.md

Everything above applies. Claude Code does not read `AGENTS.md` natively, so it is imported here —
an import rather than a symlink because symlinks need Administrator or Developer Mode on Windows,
and this repo is developed there.

## Claude-specific

- **Use the `run-auditworthy` skill** (`.claude/skills/run-auditworthy/`) to run, observe or test
  this product; it indexes `RUNBOOK.md`, which is the source of truth. Re-run
  `/deliver:install-runbook` only to reconcile that surface after it drifts, never to run the app.
- **Plugins are declared, not vendored.** `.claude/settings.json` and `workflow.json` must agree. A
  marketplace is keyed by the `name` field in its own `.claude-plugin/marketplace.json` — **not** by
  its `owner/repo` slug. Getting that wrong resolves zero plugins, silently.
- **Read `plenipo-platform` before writing module code**, so you extend the platform instead of
  rebuilding a weaker copy of it; `plenipo-module-sdk` while declaring a tool or tab; and
  `loop-discipline` before claiming anything is done.
