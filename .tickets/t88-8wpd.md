---
id: t88-8wpd
status: open
deps: []
links: []
created: 2026-11-25T04:36:08Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, characters]
---
# A character that walks onto the edge of a flush piece of a sharp compound rises 1.3 cm, which edge removal does not stop

Measured on ter-9wwx (2026-11-24), in the demo-surface boarding at 1 g, at 3 m/s: the person's capsule (radius 0.3 m) rose 1.3 cm over 0.2 m of walk (ticks 934 to 938 of scripts/blue-danube.json) as its center came within about 4 cm of the edge of a flush piece of the shuttle's interior compound. The piece was the tram wall's sill under the hatch, a convex hull of convex radius 0 whose top lay flush with the porch floor, and later with the one-piece walkway that overlapped it. Enhanced internal edge removal is on for the character (JoltPhysicsBackend.Bodies.cs, ter-fb3w), which documents a lift of at most 3.7 mm at such joints; this one is 3.5 times that. Overlapping the floor over the joint did not change the rise. Removing the piece, or sinking its top 1.5 cm under the floor, removed it. The edge of a new flush piece in the walk's way lifts the person at each joint; the ter-fb3w case was a ring's hulls, not a box beside a hull.

Visible effect: under the porch's low roof, the follow camera's arm is capped by the roof, so the 1.3 cm rise of the eye shortened the arm by 7 cm in three ticks and moved the person 2.2 percent of the half picture in one tick (DemoFollowCameraTests). The demo works round it: DemoScenes.Shuttle.Doorway cuts the tram's doorway 1.5 cm deeper than the hatch, so the sill piece lies under the walkway's top.

Wanted: a physics test of a capsule character that walks at 3 m/s from a box onto a flush convex hull of radius 0 in one static compound, which pins the rise; then a fix or a documented bound.

