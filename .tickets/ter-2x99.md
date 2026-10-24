---
id: ter-2x99
status: in_progress
deps: [ter-4jhz]
links: []
created: 2026-10-24T16:22:41Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Slop metrics: verbosity of src

Add verbosity to tools/Tertius.Slop: |flagged lines ∪ clone lines| / SLOC of src. Clone lines come from jscpd (src only, the same settings as the commit report). Flagged lines come from structural rules written as Roslyn syntax checks: the C# analogues of the scb-check slop rules (github.com/gabeorlanski/scb-check, src/scb_check/resources/slop_rules and rules/trivial_wrapper.py, low_use_short_function.py), which are written for Python, plus four rules of our own: a method whose whole body passes one call through to another method, a local variable that only feeds the next line, a comment that repeats the code under it, and a guard that repeats a check that the caller already made. Rules that STYLE.md contradicts are left out, with the reason. JSON gains verbosity, clone lines, flagged lines and per-rule counts.

