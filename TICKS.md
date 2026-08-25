# Ticks

One line per loop tick, newest last. Each verb appends here so a repeated timer can tell what it
already did — above all the `test` sweep, whose step-1 skip check compares the default branch HEAD
against the most recent `swept <sha>` line below. A tick that is not journalled is a tick the next
run will redo.

Format: `<utc> · <verb> · <what> · <terminal state> · <outcome> · <evidence level>`

```text
2026-08-25T15:44Z · test · swept 1f7a580 · Success · 0 filed · 3 still-reproduces (#23 #25 #77) · 1 needs-triage (#31) · L3
```
