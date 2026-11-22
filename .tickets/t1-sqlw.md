---
id: t1-sqlw
status: open
deps: []
links: []
created: 2026-11-22T13:48:00Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [tooling]
---
# No suite builds Tertius.Benchmarks: a change that breaks the benchmark project shows only in the pre-push hook of main

Found on 2026-11-22 at the merge of the back-face layer into main. MeshInfo got the required parameter Closed in t1-6ucc, and benchmarks/Tertius.Benchmarks/SceneBatcherBenchmarks.cs still built its two meshes without it. Every ticket's Checked and Release suite passed, because 'dotnet test --solution Tertius.slnx' and tools/test-all.sh build only the test projects. The pre-commit hook did not build the benchmark project either. The first build of the whole solution was the pre-push hook's 'dotnet build Tertius.slnx -c Checked' at the push of main, which failed with CS7036 at lines 52 and 53. The fix of the two lines is in the same commit as this ticket. What is left: one place that builds the benchmark project before a merge, for example the pre-commit hook when a changed module is one that the benchmarks reference, or a build of Tertius.slnx in tools/test-all.sh.


## Notes

**2026-11-22T14:29:30Z**

2026-11-22, the back-face line's orchestrator. A second case of the same gap: no hook and no test applies tools/renderer-survey/probe.patch, so the back-face layer's new specialization constant broke the survey's probe on main without a failing check (t58-k95j). A cheap guard for it: when a commit changes a file that the patch names, the pre-commit hook runs 'git apply --check tools/renderer-survey/probe.patch'.
