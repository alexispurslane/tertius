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


## Notes

**2026-10-24T18:39:30Z**

Built on branch ter-2x99-slop-verbosity. Subcommands erosion, verbosity and all (both metrics in one JSON from one parse, for the ter-rnzy hooks: 1.2 s against 1.4 s for two runs). Verbosity = |flagged ∪ clone lines| / SLOC of src. Clone lines from jscpd with the commit report's settings; a missing jscpd prints a message that names the dev shell and exits 2. 32 Roslyn syntax rules: 28 ports of scb-check rules and the user's four (pass-through-method, single-use-local, comment-repeats-code, guard-repeats-caller-check). A finding covers its whole span; blank lines never count; the comment lines of comment-repeats-code count. guard-repeats-caller-check flags a null guard on a non-nullable parameter outside the public API, because the compiler's nullable check already covers every caller. code-quality-tooling.md decision 17 lists every port and every skipped scb-check rule with its reason. On 2026-10-24: verbosity 0.036, 1,250 of 34,450 lines (737 flagged, 516 cloned); single-use-local 384 lines, defensive-try-soup-function 221, pass-through-method 48. The post: 0.15 ± 0.06 established, 0.33 ± 0.10 agent code. Run time on a quiet machine: erosion 0.5 s, verbosity 0.9 s, all 1.2 s, of which jscpd 0.04 s. 273 tests; 105 hand mutations, 103 killed, 2 equivalent (code after a last continue or return is unreachable, which CS0162 rejects).
