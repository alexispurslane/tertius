---
id: ter-2fn5
status: closed
deps: [ter-jng0]
links: []
created: 2026-10-12T22:20:38Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s02r
tags: [physics, frames]
---
# Move groups of bodies between a semantic frame and its numeric frames without events

Design: docs/plans/numeric-frames.md. Step 3. Whole groups that touch or are joined move together, sleepers stay asleep, held contact pairs carry over, characters are recreated, and no FrameChanged, repeated ContactStarted or topic change goes out. A frame owner's frame moves under the numeric frame of its hull, and its semantic parent stays the same. Every narrowing of a velocity to a float subtracts the home's frame velocity first.

