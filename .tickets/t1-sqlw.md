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

