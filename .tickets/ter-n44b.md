---
id: ter-n44b
status: closed
deps: [ter-1b21, ter-vja8, ter-r1hi]
links: []
created: 2026-10-16T23:00:45Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-s9m0
tags: [demo, docs]
---
# A station scene with spin gravity, its checked-in test, screenshots, and the decisions in ARCHITECTURE.md and CLAUDE.md

Design: docs/plans/spin-gravity.md. Sections 'Content' and 'Tests'. A station with a floor of at least 256 faces per ring, a person who stands, walks both ways and jumps, and crates that drop and are thrown; a checked-in Host test against inertial lines; Opus builds and runs, Sonnet reads the outputs; screenshots of a person on the inside of the ring. Change the scope rule in CLAUDE.md.


## Notes

**2026-10-19T13:11:21Z**

From ter-fb3w (2026-10-19): give a station the ring's exact mesh (RevolvedSolid.Mesh as a MeshShapeDesc) as its BodyDesc.InteriorShape, the floor people walk on, and keep the compound of segment hulls as its Shape. On the hull compound a sliding box trips at every joint; on the mesh it does not.

**2026-10-20T13:35:48Z**

2026-10-20, from ter-1b21: measure the m v^2 / r part of a walking person's weight on the faceted station floor, which the design now leaves to this scene (ter-1b21 measured only m (omega^2 r +- 2 omega v) on a flat dynamic plank, within 2 percent).

**2026-10-20T14:50:37Z**

2026-10-20, built: the scene spin-station (a ring of 256 faces, 100 m from the axis to the floor, at 0.31321 rad/s), scripts/spin-station.json and SpinStationSliceTests. Measured on the faceted floor: the inertial acceleration toward the axis of a person who walks at 3 m/s, center radius 99.095 m. Spinward, the mean is 11.69284 m/s², against (omega r + v)^2 / r = 11.69135 and omega^2 r + 2 omega v = 11.60053, with a peak of 19.05 in one tick. Antispinward, the mean is 7.93446, against 7.93283 and 7.84201, with a peak of 13.52. So the path holds the m v^2 / r part. The walk implies this result, because a person who stands at a fixed speed on the floor must follow that path. The press that Jolt gives a dynamic faceted floor under a walking person stays unmeasured (ter-cvqr).
