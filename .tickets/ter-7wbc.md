---
id: ter-7wbc
status: open
deps: []
links: []
created: 2026-10-11T18:24:51Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [simulation, physics]
---
# A Kepler orbit's G·M can disagree with its parent frame's gravity field

A Kepler frame's orbit stores its parent's G·M in KeplerOrbit.ParentMu, and the parent frame's gravity field stores it again in GravityField.Point(mu). The Kepler driver moves the frame with ParentMu, and Jolt pulls free bodies in the parent frame with the field's mu. Nothing checks that the two agree. Found while explaining the orbit scene of ter-fe04, which passes EarthMu to both.

If they differ, an object that leaves a Kepler ship falls on another orbit than the ship's, even at the moment that it leaves. The vertical slice's throw between frames (ter-upyg) depends on this.

Design to decide: the World refuses an orbit whose ParentMu differs from the parent's point field, at spawn and in SetFrameOrbit and SetFrameGravity. Or the orbit takes its G·M from the parent's field, so the orbit no longer carries it. Or a frame with a Kepler orbit around a parent without a point field is refused.

## Acceptance Criteria

A Kepler frame and the free bodies in its parent frame always use the same G·M, or the World refuses the mismatch with a clear error.

