---
id: ter-nx32
status: open
deps: [ter-upyg]
links: []
created: 2026-10-12T10:56:56Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [physics, demo, slice]
---
# Vertical slice: shrink the ship's bubble to an ellipsoid that hugs the hull

The user asked for the ship's frame to fit just around the hull, so that a thrown crate and a person hand off to earth near the ship, in view, instead of 102.9 m out.

The smallest ellipsoid that holds a box has the box's half extents times sqrt(3). With a shell of 2 m around the hull (half extents 10, 4, 30 m), the bubble's radii are about (20.8, 10.4, 55.4) m. The shell keeps a body up to 2 m in radius from touching the hull from earth, where the hull is on the FrameHull layer and loose bodies do not collide with it. The exit margin becomes max(2 % of 55.4 m, the body's radius): the person leaves about 22.8 m from the ship's center along -X and comes back at about 21.7 m.

To do: the bubble in DemoScenes.cs, the scene doc and the plan doc (the chase camera, 71 m out, stays in the ship's frame because it has no body), and the spacewalk and throw scripts and their slice tests, whose distances and ticks change. A person outside the hull now spends more time in earth, so the float rounding of ter-fa7j shows in an ordinary spacewalk.

## Acceptance Criteria

The ship's bubble is the ellipsoid, a thrown crate and the person hand off within about 25 m of the ship's center, and every slice test passes with retuned scripts.

