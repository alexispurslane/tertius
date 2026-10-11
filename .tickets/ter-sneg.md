---
id: ter-sneg
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T13:52:12Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Decide which frame tree changes go through World methods

World.Frames gives the game the whole mutable FrameTree, so mirrored rows can disagree with the tree until the next tick.


## Notes

**2026-10-11T14:29:38Z**

Round eight: FrameTree.LinearVelocity, AngularVelocity and ProperAcceleration are still public refs, and SetLocalPose is public. A NaN written there between ticks reaches the reparent and rehome conversions. PhysicsHarness writes them, so this waits for the real physics in every test work.
