---
id: ter-dtw2
status: open
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

