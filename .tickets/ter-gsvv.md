---
id: ter-gsvv
status: closed
deps: []
links: [ter-s02r]
created: 2026-10-16T13:12:27Z
type: bug
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# A body in a numeric frame never enters the bubble of a semantic frame whose hull is not in the same numeric frame

Found by the test ports of ter-4h9w, which turned numeric frames off in the 60 m/s hand-off scenes of HandOffTests, CharacterOrbitHandOffTests, GrowthTests and ReconcileTests to keep their scenarios. JoltPhysicsBackend.HandOffBodies (src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs) looks for bubble entries only among the children of each system's own frame, and a numeric frame has no bubble to leave (BubbleParentOf). So a body whose home is a numeric frame enters only a frame whose hull sits in that same numeric frame. Two moving ships merge first, so they work. A parked ship or station keeps its hull in the semantic frame's own system as an anchor, so a fast body that flies into its hangar never enters its frame and misses its interior. Fix: the entry test also considers the semantic frames under the home's semantic frame (and under each numeric frame on the chain), with the body's pose composed through the tree in doubles; then turn numeric frames back on in the ported tests that turned them off for this reason, and add a test of a fast body flying into a parked station's hangar.

