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

