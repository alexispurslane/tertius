---
id: ter-aiuy
status: in_progress
deps: []
links: []
created: 2026-10-19T01:37:17Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [demo, engine]
---
# A convex radius on each box and hull description

Found by ter-r1hi (2026-10-18). The engine gives every box and convex hull Jolt's fixed 5 cm convex radius (ShapeCache, Foundation.DefaultConvexRadius). Jolt shrinks such a shape by that radius and rounds it back out, so every edge is a 5 cm curve. A ring or torus is a compound of one convex hull per segment, so each joint between two segment hulls is a groove: a 0.25 m ball sinks 1.6 to 4.4 mm into it. The user chose (2026-10-18) that each hull description carries its own convex radius, so that segment hulls use 0. For consistency the box description gets the same field, because a compound of boxes has the same groove. BoxShapeDesc and ConvexHullShapeDesc get a ConvexRadius, the largest rounding that physics may give the shape, with today's 0.05 m as the default, so existing callers keep today's shapes. Physics uses less when the shape is too thin (a box: its smallest half extent, as today; a hull: Jolt reduces it). ShapeDesc.Problem refuses a radius that is negative or not finite. ARCHITECTURE.md describes it.

