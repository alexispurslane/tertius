---
id: ter-dtw2
status: closed
deps: [ter-qawf, ter-hynj, ter-1i4z, ter-fe04, ter-ai0t, ter-f8sh, ter-upyg, ter-mu7k]
links: []
created: 2026-10-11T13:52:08Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [host]
---
# Host and scripted runs, with the vertical slice

Design: docs/plans/host-and-scripted-runs.md. This comes right after round eight. The vertical slice shows that the engine really does the right thing, and that result guides test hardening, the design review and the snapshot tests.

## Acceptance Criteria

Each step of the vertical slice has a scripted test. An agent opens the captured images and the state dumps of each step and reports what they show.


## Notes

**2026-10-12T21:30:05Z**

Hardening before the merge, on the lines that the slice changed against main:
- Coverage, Debug and Release together: 2142/2415 lines (88.7%) and 1073/1218 branches (88.1%), against 2141/2415 and 1072/1218 before the fixes. Without WindowHost.cs, which needs a window, and Program.cs: 2133/2166 lines (98.5%).
- Stryker on Tertius.Demo: 96.66% (614 killed, 109 timeout, 21 survived, 4 no coverage). The fixes added 18 Host tests and five ignore comments with reasons.
- Stryker on Tertius.Core: 100% of 155 (131 killed, 24 timeout). 52 more mutants in the changed lines did not compile, so 29 hand mutations covered their lines. Three found gaps, which now have tests; the mutant trueAnomaly - turns is equivalent, because it changes the angle by a multiple of 4 pi.
- Stryker on Tertius.Host: 79.21%, not triaged, by the user's choice.
- Every suite passes in Debug (285, 1357, 318, 189, 505) and Release (255, 1379, 319, 189, 505).
