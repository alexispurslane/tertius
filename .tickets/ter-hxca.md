---
id: ter-hxca
status: open
deps: []
links: [ter-u63a]
created: 2026-11-01T23:57:17Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [testing, tools]
---
# tools/mutate: a batch runner for a list of hand-chosen mutants, C# and GLSL alike

The user's decision of 2026-11-01 (ter-u63a): the rendering tickets' hardening keeps mutants that an agent chooses, but a script runs them. Today an agent runs each hand mutation itself (edit, build, test, restore, touch), 3 to 5 tool calls per mutant, about 140k tokens for ter-pjza's 27 mutants. The script takes the agent's list and runs the loop that CLAUDE.md's Testing section describes, so the agent writes one list, runs one command, reads one table and triages the survivors. Work that is not rendering uses Stryker on its changed lines (tools/stryker), which cannot mutate GLSL and timed out on the GPU tests in the trial.

