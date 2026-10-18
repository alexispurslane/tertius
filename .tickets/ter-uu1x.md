---
id: ter-uu1x
status: closed
deps: []
links: [ter-kugq, ter-jwzd, ter-dhkf]
created: 2026-10-17T14:34:17Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# Numeric frames: fewer of them, and more reliable

Design: docs/plans/numeric-frames.md. This epic adds two sections to it: "Thresholds from measured precision" and "Ghosts as infinitely heavy".

Goal: make numeric frames only where float precision needs them, so there are fewer of them and the merge test runs less often. Then the merge test can afford more inputs: everything that a group cannot move counts as infinitely heavy, so a numeric frame holds no ghosts.

The precision measurements (ter-kugq, merged 2026-10-17, tests in tests/Tertius.Physics.Tests/Precision, explicit) give the numbers. Distance from the origin added no error up to 9,000 km. Speed adds error only along an axis where a velocity changes. Relative errors between bodies that move together stay small: at 1 km/s the gap error is 1.3 mm per minute and the relative velocity error 0.024 mm/s. A body that thrusts drifts fast relative to its neighbors: 2.1 mm/s per minute at 50 m/s, 85 mm/s per minute at 1 km/s.

User decisions (2026-10-17):
- A group with a body that thrusts (characters included) keeps a create speed of 50 m/s. The user accepted the drift of about 2 mm/s per minute of thrust at that speed, against the doc's target of 1 mm/s.
- A group of free bodies only (coasting, colliding, pushing each other, falling together) gets a create speed of about 1 km/s.
- A moving platform that carries things is always a semantic frame (see ter-jwzd).
- Change 1 of the three general changes: make falling frames only where float precision needs them, with one rule that no frame inside a turning frame falls.
- Change 2: everything that a group cannot move counts as infinitely heavy: anchors, interiors and ghosts. The anchors and interiors are ter-dhkf. The ghosts are this epic.


## Notes

**2026-10-17T20:02:07Z**

HANDOFF for the next session (2026-10-17). Start here and from tk ready.

State of main (237f7ed):
- ter-dhkf merged: anchors are infinitely heavy in the merge test (static bodies, hulls of Static and Spin frames, and since the review the interior of every frame owner). A still kinematic body counts as infinitely heavy for its group's velocity and center but is not an anchor (user decision). A fast or far Kepler hull gets an orbit frame K (NumericMode.Orbiting) that follows its orbit; its station frame hangs at K's origin. A moved body keeps its ghosts. Intents name their frame and sync step 7b converts them after a hand-off or a rehome. Scene: scripts/station.json (DemoScenes.Station.cs, StationSliceTests).
- ter-1w06 merged: below FrameTree.ZeroGravityThreshold (0.05 m/s2) a character is a rotation-locked rigid capsule (JoltPhysicsBackend.CharacterForms.cs); it walks again from 0.1 m/s2, or from 0.05 m/s2 while it touches something. Scene: scripts/weightless-push.json.
- ter-kugq merged: explicit precision measurements in tests/Tertius.Physics.Tests/Precision (run with --explicit only, output folder from TERTIUS_PRECISION_OUT). The numbers behind this epic are in the epic description above.

Order of this epic:
1. ter-3xs6 (thresholds). Open design point, to design twice with the user at the start: what counts as "a body that thrusts" (any character, a body that got a force in a recent window, or a flag that a component sets). The drift grows with how long the thrust lasts, not with its size. Free-body groups: CreateSpeed 1,000 m/s, LeaveSpeed 400 m/s. Thrust groups keep 50 and 20. CreateDistance about 2^23 m, LeaveDistance half; check nesting frames.
2. ter-9yvr (no falling frame inside a turning frame). Ask the user before closing ter-vja8, which it supersedes.
3. ter-4psc (ghosts count as infinitely heavy, the stick-out ghosts go), after ter-3xs6. Linked: ter-tenw, ter-me1y.
ter-3xs6 and ter-9yvr both edit NumericFrameRules.cs and FrameTree.Numeric.cs, so run them one after the other.

Process (global CLAUDE.md, new today): one Opus reviewer per piece of work; a skeptic only for four or more high findings; a fix agent confirms each finding with a failing test; every brief tells agents to cut command output and splits big stages into agents that commit in turn. Coverage and Stryker run only in the orchestrator (tools/README.md). The two ContactAllocationTests fail only with coverage on (ter-odo6). Use subshells, not cd, for commands in worktrees, or the session's folder moves.

Other tickets filed today, not in this epic: ter-w7cw (a character in the air pushes without reaction), ter-myew (rigid form gets 7 mm/s from the airlock sill), ter-qcdo (a weightless character lowers a whole system's sleep threshold), ter-61ds (the character's supporting plane sits above its center).

**2026-10-17T20:59:04Z**

2026-10-17: change 1's rule that no numeric frame falls inside a turning frame is dropped (ter-9yvr closed): the user wants no stopgaps for planned work, and ter-vja8 in the spin gravity epic builds those frames. Order now: ter-3xs6, then ter-4psc, then the spin gravity epic ter-s9m0 (ter-1bnq, ter-vja8, ter-1b21, ter-n44b).

**2026-10-18T01:54:51Z**

Closed 2026-10-18: ter-3xs6 and ter-4psc merged, ter-9yvr closed by the user's rule against stopgaps (ter-vja8 in the spin gravity epic builds falling frames inside turning frames). Open follow-ups: ter-91p8, ter-me1y.
