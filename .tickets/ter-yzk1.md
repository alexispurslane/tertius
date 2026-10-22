---
id: ter-yzk1
status: closed
deps: []
links: []
created: 2026-10-22T00:45:58Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling]
---
# Duplicate-code report: jscpd

native/jscpd.nix builds jscpd 5 from crates.io with pinned hashes; the dev shell has it. tools/duplicates.sh writes a console and HTML report for src (and tests on request). Document it in tools/README.md.

## Acceptance Criteria

tools/duplicates.sh runs in the dev shell and lists the duplicated blocks.

