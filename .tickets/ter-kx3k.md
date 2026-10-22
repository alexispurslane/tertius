---
id: ter-kx3k
status: open
deps: []
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, host]
---
# Convert the class unions of Host to built-in unions

CaptureTarget, ScriptStep, MessageTarget and ScriptRunner.ReadyStep have class cases, so they become built-in union declarations (docs/plans/union-tooling.md, decision 1). Keep the JSON converters. Check that ScriptUnionTests still means what it says.

## Acceptance Criteria

The four are built-in unions. Every suite passes.

