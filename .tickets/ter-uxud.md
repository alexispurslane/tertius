---
id: ter-uxud
status: open
deps: []
links: []
created: 2026-10-31T18:30:43Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [tests, tooling]
---
# The builders' required checks and the commit hook do not build the benchmarks, so an API change breaks them unnoticed

Found in the ter-ox1a merge, 2026-10-31. ter-ox1a removed SceneBatcher's exposure argument and gave Prepare a required highestStops. Its branch passed both required checks ('nix develop --command dotnet test --solution Tertius.slnx -c Checked', then -c Release) and every commit hook, but benchmarks/Tertius.Benchmarks/ImpostorManagerBenchmarks.cs and SceneBatcherBenchmarks.cs did not compile: CS1739 (no parameter named 'exposure') and CS7036 (no argument for 'highestStops'). Only 'dotnet build Tertius.slnx', which tools/test-all.sh runs first, builds the benchmarks; the merge fixed them by hand. The class of bug: any change of an API that a benchmark calls. The hook builds a benchmark only when a benchmark file changes (.githooks/pre-commit: 'the benchmarks only build'), and 'dotnet test --solution' builds only the test projects. Fix options: the commit hook builds the benchmarks project whenever a src project that it references changes, or the required checks in CLAUDE.md (and the briefs) start with 'dotnet build Tertius.slnx -c Checked'. Design it twice before building it.

