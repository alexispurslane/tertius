---
id: ter-3oer
status: in_progress
deps: [ter-t0bh]
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling]
---
# Git hooks: build, tests and reports on commit, cleanup and docs on push

The user changed the plan twice on 2026-10-21. The final setup, which docs/plans/code-quality-tooling.md describes:

- .githooks/pre-commit changes no file. It builds the changed projects and their test projects (every analyzer, an error stops the commit), prints tools/commit_report.py (jscpd clones, CA1502 complexity and MA0051 length, for the changed lines only), and runs the tests of the changed code in Debug, in parallel per test project. A failed test stops the commit.
- .githooks/pre-push runs tools/cleanup.sh on the C# files that the pushed commits change: dotnet format style with the Roslyn modernization rules that .editorconfig sets to suggestion, then jb cleanupcode with the Tertius profile in Tertius.slnx.DotSettings. If a file changes, it builds, commits the cleanup and stops the push. Then it builds the docs site with tools/docs.sh.
- CA1502 (limit 15, analyzers/CodeMetricsConfig.txt) and MA0051 are suggestions. Every compile writes its diagnostics to obj/.../analyzers.sarif, which the report reads. This replaces the build gate and the baseline of ter-tstw.
- The dev shell sets core.hooksPath to .githooks.
- Clean up the whole codebase in one commit, and name it in .git-blame-ignore-revs.

## Acceptance Criteria

A commit with a new compile error stops. A commit with a failing test in a changed module stops. A commit with only ticket files skips the hook. The report names a long method that the commit changes. A push of a badly formatted file adds a cleanup commit and stops. A push with a broken docs build stops. tools/test-all.sh passes after the cleanup.
