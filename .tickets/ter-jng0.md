---
id: ter-jng0
status: closed
deps: [ter-gqbj]
links: []
created: 2026-10-12T22:20:38Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s02r
tags: [physics, frames]
---
# Add ownerless numeric frames to the frame tree, hidden from gameplay, falling freely with tidal gravity

Design: docs/plans/numeric-frames.md. Step 2. A numeric kind of frame with no owner, no bubble and the axes of its semantic frame. The public tree (World.Frames, Snapshot.Frames, dumps, FrameChanged, topics) reports only semantic frames. It moves in one of two modes: a falling frame, which the engine steps with Jolt's scheme in doubles, and a resting frame, which stays at one place in its tree parent. Each member feels the field at its true position minus the frame's acceleration, subtracted in doubles before the narrowing. Standing reads the effective gravity, and a character's up reads the semantic field.

## Acceptance Criteria

A crate in a numeric frame near the ship follows the orbit that its starting state gives in earth, to within a millimeter after ten minutes. Gameplay surfaces show the same frames with the numeric frame present.

