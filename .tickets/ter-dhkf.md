---
id: ter-dhkf
status: in_progress
deps: []
links: [ter-ykqs, ter-s02r, ter-kugq, ter-jwzd, ter-uu1x]
created: 2026-10-17T12:22:14Z
type: feature
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# Anchors count as infinitely heavy in the merge test, and a Kepler hull gets a numeric frame that follows its orbit

Decided with the user on 2026-10-17, as a literal extension of the existing merge rules of numeric frames (docs/plans/numeric-frames.md):

1. The merge test (speculative linking, MayTouch with the lookahead) includes anchors for every group, also a group that falls. Today LinkAnchors keeps a falling group apart from an anchor and gives its frame a ghost of the anchor instead. (User)
2. An anchor counts as infinitely heavy when the group chooses its home: a group that links an anchor takes the anchor's home, as a group of bodies today keeps the frame that holds the most of its mass. A static wall's home is its semantic frame's own system, or its resting frame. (User)
3. A moving anchor needs a home in which it is at rest. A hull whose Kepler driver moves it fast in its semantic parent becomes a member of a numeric frame K that follows its driver, as a physics-driven ship's dynamic hull is a member of a falling frame, and its semantic frame hangs under K, as the ship's frame hangs under its hull's numeric frame (Reparent(body.OwnedFrame, home)). The frame tree invariant that only a physics-driven frame may hang under a numeric frame changes for this. (User)

Why: a fast body in a falling frame passes through a wall, because the wall's anchor ghost is kinematic and Jolt gives a kinematic body no linear cast (ter-ykqs). With this rule the anchor is a real body in the group's system, so Jolt sweeps the fast body against it. The user's precision case still holds: crates that leave a Kepler station through its airlock join the station hull's frame K, where both are slow, so float velocities stay small.

Known consequences: a group that links two anchors that move differently cannot take both homes, so one of them keeps today's anchor ghost; an encounter of a fast group with a static anchor runs at the encounter speed in the anchor's home, as the doc already accepts for two bodies that pass each other; the code needs a special case for the infinite mass, because infinity times a velocity divided by infinity is NaN. A checked-in scene shows the airlock case and a crate thrown at 60 m/s against a thin wall inside a station.


## Notes

**2026-10-17T12:48:05Z**

2026-10-17: confirmed by test, and folded into this ticket with the user's approval. A frame's interior (BodyKind.Interior, the static body of BodyDesc.InteriorShape in the frame's own system) is no NumericBody, so it is no anchor, and no ghost of it reaches a numeric frame. A crate thrown at 60 or 120 m/s at a 1 m wall of a closed ship room falls in a numeric frame whose system holds only the crate, flies through the wall and out of the bubble, and hears no Collision; without numeric frames it stops at the wall (maxX 9.505 m, one Collision). Fix: the interior counts as an anchor of the frame that it lies in, so a fast group inside links it and takes its home, the frame's own system. The test is in the check's worktree: .claude/worktrees/wf_15e0eed0-47f-1/tests/Tertius.Physics.Tests/NumericFrameInteriorWallTests.cs (uncommitted).

**2026-10-17T18:04:57Z**

Merged main (ter-1w06, the two character forms, and the precision measurements) into this branch. One textual conflict: the scene list in src/Tertius.Demo/DemoScenes.cs, which now holds both station and weightless-push. Every other shared file merged by itself, and the reads found no clash: a form switch and a numeric move both use RemovePrimary and CreatePrimary, which keep ghosts, and the form follows EffectiveGravity of the physics home, which is the tidal pull in an orbit frame K. New tests on the real World and Jolt, tests/Tertius.Physics.Tests/CharacterFormNumericFrameTests.cs: a character beside a Kepler hull is rigid in K and walks in earth's own system; a rigid character that drifts out of a Kepler station has ghosts while it sticks out and stays rigid in K or in its own falling frame; an impulse of the hand-off tick into a ship with gravity converts at step 7b and pushes the walking form along the named frame's axes. Hand mutations: skipping the impulse conversion and reading the semantic gravity in SwitchCharacterForms each fail them. All suites green in Debug and Release.
