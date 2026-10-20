---
id: ter-qipk
status: closed
deps: []
links: []
created: 2026-10-20T22:30:28Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-xr2b
tags: [demo, design]
---
# Design the shuttle pilot as one flat state machine

The user decided on 2026-10-20 that the shuttle pilot of docs/plans/live-world.md is one flat finite state machine: one custom [Union] of every phase of the cycle, one pure Next(state, event) -> state, and a switch from each state to its delegate. Messages from other entities (Depart, PlaceGrant, TowStart) and the delegates' reports (a phase's goal reached, or a fault) are the events. A delegate flies one tick, may change only its own state's data, and returns at most one report. Only Next changes the case. The user chose this flat table over a two-level machine (legs, each with its own phases). Update docs/plans/live-world.md (The pilot and every section that depends on it), the part tickets, and CLAUDE.md's table of changed proposals.

