---
id: ter-zg77
status: open
deps: [ter-ru5u]
links: []
created: 2026-10-17T21:44:05Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# A following frame follows every fast kinematic hull

Part of the epic on frames that follow their owners. Read the epic and its design doc first (docs/plans/rails-components.md, 'Order of the work', item 4). It depends on ter-ru5u. ter-6r8c already gives every kinematic hull a following frame (the numeric frame that holds a fast or far body on rails at its origin). This ticket proves it for motion that is no orbit, renames the numeric names, and rewrites the numeric docs.

Scope:
- New test 8 of the doc: a test component drives a kinematic hull at 2 km/s along a straight line. The hull gets a following frame, and the hull rests in it. A crate that drifts out of the hull's frame joins that following frame.
- Rename NumericMode.Orbiting to NumericMode.Following and NumericBody.Orbits to Follows, because the anchor need not orbit. NumericFrameRules.OrbitsInFrame, BodyMode and GroupMode read Follows.
- Rewrite the sections 'Orbit frames', 'Anchors' (rule 3), 'Frame owners in a numeric frame', 'Bubbles and ghosts' and 'Limits' of docs/plans/numeric-frames.md, and the numeric rows of ARCHITECTURE.md.
