---
id: ter-fb3w
status: open
deps: []
links: []
created: 2026-10-19T11:39:20Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# Contacts at the joints of segment hulls push a landing body sideways

Found by ter-r1hi (2026-10-19). A 0.25 m ball that lands near the joint between two segment hulls of a ring or torus moves sideways where the mesh would hold it: 7.0 mm when it lands 2 mm from the joint, 10.8 mm at 5 mm, 17.4 mm at 10 mm, 4.8 mm at 20 mm, and 9.1 mm uphill on a ring floor 5 mm from a joint. The push grows with landing speed. Jolt's own mesh shape of the same mesh pushes the same, so it is Jolt's contact at an internal edge, not the hull compound. BodyCreationSettings.EnhancedInternalEdgeRemoval on the static solid fixes torus-top landings 5 mm or more from a joint, not at 2 mm, and barely helps the ring floor. Find a fix for the ring floor (the station floor that people walk on) before the demo's boarding and walking scenes depend on it. The scratch test is in the ter-r1hi run notes (ScratchExperiments.cs.txt), and the measurements are in the remarks of src/Tertius.Demo/RevolvedSolid.cs.


## Notes

**2026-10-19T11:45:40Z**

Ideas (2026-10-19). Cause: Jolt treats the ball as a point with a 0.25 m radius. While the ball sinks into the floor during a landing, and its center is above segment A within sqrt(2 r d) of the joint (d = the depth it sinks), the closest point of segment B is B's edge at the joint, so B reports a contact whose normal points from that edge to the center, tilted away from the joint by about x / r radians. (1) First experiment: Jolt's mesh shape should not push. It marks an edge inactive when two triangles share it by vertex index and bend less than 5 degrees (a concave edge is always inactive), and a contact on an inactive edge takes the triangle's normal. The scratch run gave the same numbers to 0.1 mm for the mesh and the compound, which suggests that this never ran. Check the shared indices at the joints and at the seam where the last segment meets the first, PhysicsSettings.CheckActiveEdges in each FrameWorld system, and the movement direction rule of ActiveEdges::FixNormal. (2) If the mesh works, the station's interior (the body people walk on, kinematic at rest while the station spins, by the ghost design's F4) uses a mesh, which needs the engine to allow a mesh on a kinematic body (Jolt allows it); the hull outside keeps its hull compound. (3) Enhanced internal edge removal set on the moving body (Jolt applies it when either body of a pair has it), for dynamic surfaces that cannot be meshes. (4) Measure the cases that matter before choosing: a character walking across joints, a crate sliding across them, a ball rolling. Moving or beveling the segment hulls does not help: it moves B's edge, and the tilted contact moves with it.

**2026-10-19T13:09:56Z**

Experiment result (2026-10-19). The cause is three Jolt rules: active edges never apply to a landing (FixNormal keeps the edge normal for a body moving into the triangle), manifold reduction averages the neighbor's tilted normal, and a concave crease gives a real second face contact within about 6 mm. Shared indices and CheckActiveEdges were both fine. Measured: EnhancedInternalEdgeRemoval on either body removes the landing push beyond about 5 mm from a flat joint and 10 mm from a ring crease, and stops a sliding box from tripping at the joints of a hull compound (it trips at every joint without it; it never trips on the mesh). Fix: (1) demo, no engine change: the station uses the ring mesh as its InteriorShape (the floor people walk on) and keeps the hull compound as its Shape; BodyDesc(compound, Kinematic, InteriorShape: mesh) passes the checks today. But ter-imi5 (fork F4) makes the interior kinematic and at rest while the hull moves, so after it merges a mesh interior must still work as a kinematic body (Jolt allows a kinematic mesh with a mass, as mesh ghosts already do). (2) engine, waits for the user: every body whose shape is a mesh or a compound sets EnhancedInternalEdgeRemoval, after a benchmark of its cost. The experiment's knobs are in the scratchpad (fb3w/knobs.patch, fb3w/ScratchKnobs.cs).
