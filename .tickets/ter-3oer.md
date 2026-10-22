---
id: ter-3oer
status: open
deps: [ter-t0bh]
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling]
---
# Pre-push formatter: jb cleanupcode

Pin JetBrains.ReSharper.GlobalTools in .config/dotnet-tools.json. Add Tertius.slnx.DotSettings with formatting rules that match the current code. Add .githooks/pre-push that runs the reformat-only profile on the C# files that the pushed commits change and stops the push when a file changes. The dev shell sets core.hooksPath. Reformat the whole codebase in one commit and name it in .git-blame-ignore-revs.

## Acceptance Criteria

A push with a badly formatted file stops and leaves the file formatted; a push with formatted files goes through; tools/test-all.sh passes after the reformat.

