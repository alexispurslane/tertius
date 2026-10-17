---
id: ter-4psc
status: in_progress
deps: [ter-dhkf, ter-3xs6]
links: [ter-tenw, ter-me1y]
created: 2026-10-17T14:34:17Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-uu1x
tags: [physics, frames]
---
# Ghosts count as infinitely heavy in the merge test, and the stick-out ghosts go

Part of the epic on fewer, more reliable numeric frames (change 2, the ghost step). After ter-dhkf, anchors and interiors count as infinitely heavy: a group that links one takes the home where it is at rest. This ticket does the same for the places of ghosts: the merge test takes them as inputs, and a group that would touch a ghost takes the home of the body behind the ghost. Then a numeric frame holds no ghosts, except in the rare case of two anchors that move differently, and the stick-out machinery (StickOutGhost, GhostPlacesWithdrawn and their passes) can go.

Expected to remove: the cast gap of stick-out ghosts, a ghost in a child frame's own system that never reaches the child's numeric frames (ter-tenw), ghosts one step late after a move and the flicker of the stick-out test (ter-me1y). The merge test gets more inputs, which is why this ticket waits for the thresholds ticket: fewer numeric frames make the merge test run less often.

