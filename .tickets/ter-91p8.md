---
id: ter-91p8
status: open
deps: []
links: [ter-4psc]
created: 2026-10-18T01:08:41Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A resting body that a ghost place links to a fast hull slides 0.58 m beside the ghost it touches

Found while fixing finding 2 of the ter-4psc review (2026-10-17).

Scene (NumericFrameStickOutGhostTests.RunCrateThatEntersAFallingShip): a crate rests at (0, -199.5, 0) in space and a rock rests against its bottom face. A ship flies past at 60 m/s along -X in its own falling frame N, with a 200 m bubble. The crate enters the ship, and its ghost goes into N, the ship's tree parent. The merge test links the rock to the place of the crate's ghost, so the rock joins N. In N the rock and the ghost both move at 60 m/s along +X, while in space both rest.

Effect: in the first step in N the rock moves 0.485 m instead of 1 m, and in the next steps a little less than 1 m, so it ends 0.578 m along -X from where it rests without numeric frames. Its velocity in space stays 0. Events are the same.

Cause: Jolt's linear cast. The rock is a dynamic body with MotionQuality.LinearCast, it moves 1 m a step in N, and the cast along +X hits the ghost that it touches and that moves with it. A run with MotionQuality.Discrete for dynamic bodies had no lag (the rock stayed within 2e-7 m).

The pure model (a group that may touch a ghost takes the ghost's home) makes a body fast in its home when the hull's group moves fast relative to the body. Decide whether the linear cast, the group's frame velocity or the link needs a change. The test of finding 2 checks the rock's Y and Z only, until this is fixed.

