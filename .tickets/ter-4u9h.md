---
id: ter-4u9h
status: open
deps: []
links: []
created: 2026-10-25T00:26:18Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [tooling]
---
# Faster agent loops: skip hook tests for a tested tree, cheaper mutation builds

Measured on 2026-10-24 from agent logs: each commit's pre-commit hook reruns the tests that the agent just ran (1.7 to 3 min per commit), each hand mutation does a --no-incremental build plus a test run (20 to 60 s, 7 to 10 min per agent), and a BenchmarkDotNet run with --job short takes about 5 min. 1) Let the pre-commit hook skip the tests when they passed for the same git tree (git write-tree) in both configurations, recorded by test-all or a marker that dotnet test runs write. 2) Measure whether a plain incremental build of one test project picks up a source mutation correctly; if it does, limit --no-incremental in CLAUDE.md to mutations of analyzers and generators. 3) Document --inProcess and a one-method filter for benchmarks in tools/README.md. 4) Note in CLAUDE.md: at most two agents build at once on this machine.

