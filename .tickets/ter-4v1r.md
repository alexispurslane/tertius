---
id: ter-4v1r
status: open
deps: []
links: []
created: 2026-10-27T12:19:39Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# CreateFollowingFrame takes a plain anchor's chord as an instant velocity

From the review of ter-2cmo. CreateFollowingFrame derives a new following frame's velocity, a chord, from its anchor's instant velocity less half a step of its acceleration. For a plain kinematic anchor it reads the velocity from the transform row (NumericBody.Velocity), which is Jolt's chord, not an instant velocity, and still subtracts half a step of gravity, so the frame starts half a step of gravity behind the anchor's chord. Since ter-2cmo, NumericBody.Chord gives the body's chord over the step that ended (zero excess for a body that physics stepped, the derivation for one that physics creates at the sync point). Use body.Chord for a plain anchor, with a test that a fast plain kinematic body on rails in a gravity field rests in its new following frame in the first steps, and update ARCHITECTURE.md, 'Following frames' (the sentence 'its frame starts with the chord of a free fall').

