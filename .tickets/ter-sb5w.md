---
id: ter-sb5w
status: open
deps: [ter-ozm8]
links: []
created: 2026-10-18T15:20:44Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [docs, demo]
---
# README: a guide through the code that proves each physics claim

User request (2026-10-18): the best proof that Tertius does the physics it claims is a guide in the README that tells a reader what to read. For each claim of the comparison post (large worlds and automatic frames past 8,389 km, ships in ships, objects across an edge, fast objects sharing a moving frame, thrust felt as weight, orbits and turns, spin gravity), name the files, the key functions and the tests that a reader opens to make sure that it is real, with the measured numbers (docs/plans/numeric-frames.md, 'Thresholds from measured precision'). It pairs with publishing the repository. Write it after the rails epic; add the spin gravity part after ter-s9m0.


## Notes

**2026-10-18T15:42:44Z**

Published on 2026-10-18: README.md and LICENSE (0BSD) merged into main at 1e7e1be and pushed to origin (tangled, git@knot.xenolandscapes.com:did:plc:6mfsw4jthsacfcdbfmfetvin). The line numbers are from that commit. Still to do: update the guide when the rails epic ter-ozm8 lands (MotionDriver, SetFrameOrbit and PlaceOnOrbit give way to KeplerMotion, SpinMotion and step records), and add the spin gravity part after ter-s9m0.

**2026-10-18T21:23:39Z**

ter-ru5u removed MotionDriver and World.SetFrameDriver. GUIDE.md still names them, and the rewrite must fix these lines: 70 (read MotionDriver in FrameTypes.cs), 168 (MotionDriver in FrameTypes.cs), 169 (World.SetFrameDriver in World.Entities.cs) and 192 (ApplySpin in World.Sync.cs, gone since the rails components). A hull's motion is now its body's BodyMotion, kept in FrameTree.HullMotion, and the intent Self.Body.SetMotion changes it at step 4 of the sync point (ARCHITECTURE.md, "Changes of motion").
