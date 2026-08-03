# CLAUDE.md

@AGENTS.md

Everything above applies. Claude Code does not read `AGENTS.md` natively, so it is imported here —
an import rather than a symlink because symlinks need Administrator or Developer Mode on Windows,
and this repo is developed there.

## Claude-specific

- **There is no `run-auditworthy` skill yet.** `/deliver:install-runbook` installs `RUNBOOK.md`, the
  Testcontainers fixture, the golden-eval harness, the committed `.http` catalog and
  `.claude/launch.json`. Run it before the first feature issue — a product nobody can run is a
  product nobody can verify.
- **Plugins are declared, not vendored.** `.claude/settings.json` and `workflow.json` must agree. A
  marketplace is keyed by the `name` field in its own `.claude-plugin/marketplace.json` — **not** by
  its `owner/repo` slug. Getting that wrong resolves zero plugins, silently.
- **Read `plenipo-platform` before writing module code**, so you extend the platform instead of
  rebuilding a weaker copy of it; `plenipo-module-sdk` while declaring a tool or tab; and
  `loop-discipline` before claiming anything is done.
