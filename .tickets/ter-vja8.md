---
id: ter-vja8
status: in_progress
deps: [ter-1bnq]
links: [ter-9yvr]
created: 2026-10-16T23:00:45Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s9m0
tags: [physics, frames]
---
# Numeric frames inside turning frames

Design: docs/plans/spin-gravity.md. Section 'Numeric frames inside turning frames'. A falling frame's origin moves on its inertial line with the terms at its origin; its members feel only the difference. Fix and pin the spike's failure: a person crossing a station at 40 m/s got a falling frame five ticks before landing, landed 0.7 m deep and went through the floor.


## Notes

**2026-10-17T20:44:05Z**

2026-10-17: kept open by the user. The rule of ter-9yvr (no numeric frame falls inside a turning frame) covers the station scene, so this ticket no longer blocks ter-n44b. It now comes after ter-n44b and still needs the rotation terms of ter-1bnq. It is later work for large turning frames, such as a craft that thrusts at hundreds of m/s in the surface frame of a spinning planet.

**2026-10-17T20:59:04Z**

2026-10-17, later: the user rejected stopgaps for planned work, so ter-9yvr closes and this ticket blocks ter-n44b again: the spinning station needs falling frames that handle rotation, because a character counts as thrusting and gets a falling frame from 50 m/s.
