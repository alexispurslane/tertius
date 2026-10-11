---
id: ter-uucg
status: closed
deps: []
links: []
created: 2026-10-11T20:04:14Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [simulation, physics, rendering, slice]
---
# Rebuild the orbit scene with the sun as the root, a spinning Earth surface and a free-falling ship

The user asked for a more accurate frame tree for slice step 1, as a stress test before step 2 (ter-ai0t). Decisions (two designs, the user chose a hybrid on 2026-10-11):

- sun: the Static root frame, with the sun's G·M (1.32712440018e20 m³/s²), drawn as an emissive sphere with the real radius (6.957e8 m). It is world.SunFrame.
- earth: a Kepler frame round the sun with Earth's real orbit (a 1.496e11 m, e 0.0167). It does not spin. Its AxialTilt carries the 23.44 degree obliquity, so its axes follow the equator, as the real ECI frame does. It holds Earth's point gravity and no body.
- earth-surface: a child of earth at its center, driven by a NEW motion driver, MotionDriver.Spin (the user's choice over a Kepler orbit of radius 0). It turns once per sidereal day (86,164.09 s), holds the drawn sphere, and casts Earth's shadow.
- ship: a child of earth with MotionDriver.Physics (the user's choice over Kepler). Jolt integrates its free fall in Earth's point gravity, with linear and angular damping 0. Its start state comes from a circular orbit 400 km up. Measured drift: about 350 m behind the exact circle after one orbit, growing by about 0.4 km per orbit (about 7 km per day), from Jolt's float velocities. (An earlier Python model gave 85 m; the review's model of Jolt's real step matches the dumps to 1 cm.)
- The ship must orbit the non-spinning earth frame, because a Kepler or physics orbit in a spinning frame turns with that frame.

Findings that the work must handle: the sun would eclipse its own light (BuildOccluders adds every point-gravity frame with a render radius); only one of earth and earth-surface may cast the shadow; dumps' rootPose becomes heliocentric; docs/plans/content-formats.md puts a corvette in a spinning terra frame, which is the bug above.

## Acceptance Criteria

The orbit scene has the tree sun > earth > (earth-surface, ship > chase), with far in earth. The Spin driver is tested in Core, the World and Jolt (closed-form rotation, kinematic hull, hand-off with omega x r, refusals). The sun lights the scene and is drawn. The ship stays near its circular orbit, faces Earth, and passes through Earth's shadow. scripts/orbit.json runs, and the docs describe the new tree.

