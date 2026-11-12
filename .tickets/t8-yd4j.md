---
id: t8-yd4j
status: in_progress
deps: [ter-k7fd]
links: []
created: 2026-11-12T15:35:53Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# Cloud tests: a CPU endpoint for Stryker and the CPU test projects, and Rendering shards on the GPU endpoint

Alexis's decision of 2026-11-12 10:34. A serverless endpoint of cpu3c workers with 2 vCPUs (min 0, max 4) runs the official Stryker runs of Core, Simulation and Physics and the CPU-only test projects, with its own execution limit. The GPU endpoint keeps 6 workers; Rendering tests and mutate.py's GPU mutants run there as shards. 6 GPU + 4 CPU is the account's quota of 10: a GPU image switch fits the GPU endpoints into 6 and never lowers the CPU endpoint. One submit command for both kinds, with the same caps and audit log and each kind's price in the cost model. A cloud form of tools/stryker/changed.py with the same table and validity rules. Docs: WORKBENCH.md, tools/runpod/README.md, tools/README.md 'Mutation testing', CLAUDE.md's Testing bullets. The Mac's pre-commit hook, its test record and one Mac pass of the GPU tests before a merge stay.

