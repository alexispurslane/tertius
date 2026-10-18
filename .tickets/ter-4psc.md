---
id: ter-4psc
status: closed
deps: [ter-dhkf, ter-3xs6]
links: [ter-tenw, ter-me1y, ter-91p8]
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


## Notes

**2026-10-18T01:54:51Z**

Merged 2026-10-18 (ef8b6e6). The merge test of a semantic frame takes ghost places as inputs (FrameTree.Numeric.cs CollectGhostPlaces, LinkGhostPlaces; NumericFrameRules GhostPlace, MayStickOut, MayTouchBubble, MayReachBubble, MeetsGhost). A body that may touch the ghost of a body that sticks out of a child joins the child's hull; a body that may touch a ghost from the parent or a sibling stays in its frame's own system. MeetsGhost mirrors Jolt's layers: a hull never meets a ghost. Place existence has the split margin as a floor. Heir sends every frame to the dying frame's semantic parent. Gone: StickOutGhost, the second ghost in the semantic parent (ter-wllb), GhostPlacesWithdrawn. New: ghosts beside a Kepler hull that lives apart, DropWithdrawnAnchorGhosts, ghosts of bodies that stick out move with a moved hull (MoveGhostsThatStickOut), the contact wait rule also counts a ghost in a numeric frame, and the numeric update always hands physics its last batch, so physics reconciles at every sync point. Docs: docs/plans/numeric-frames.md 'Ghosts as infinitely heavy'. Coverage of changed lines 230/230 after the last tests. Follow-up: ter-91p8 (a resting body linked into a fast hull's frame slides 0.58 m beside the ghost it touches, from Jolt's linear cast).
