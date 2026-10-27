---
id: ter-2y6i
status: closed
deps: []
links: []
created: 2026-10-16T12:38:20Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [host, tools]
---
# The state dump at tick 0 prints spawn velocities as floats

The orbit-cluster reader found that start.json shows the ship's velocity as a float32 (6016.291015625 against 6016.291064), while later dumps print doubles. Cosmetic, because the engine holds the exact value from tick 1, but a tick-0 dump is then not an exact initial state.


## Notes

**2026-10-27T12:00:13Z**

2026-10-27: the builder stopped at a fork: the exact dump needs exact velocity reads in the engine (a frame owner's velocity from the frame tree, a pending body's from its creation), which moves the Blue Danube film's cut lengths (spinward 410 -> 498 frames, deck 750 -> 733, stack 365 -> 346) and pushes DemoLaunchTests' hand-off acceleration error to 0.01043 against 0.01. The orchestrator settled it: keep the engine-wide exact reads, by the user's rule that a frame's exact tree row is the store of its owner's velocity and the float row is filled from it, and one read path per quantity. The launch test's cause is to be found (a derived tolerance if the float read hid noise, a fork if it hid a real jump), and the film script is regenerated. Workflow wf_92bc727a-e63.

**2026-10-27T13:19:43Z**

2026-10-27: merged; full Checked on the merged main: 9,089 total, 0 failed. Coverage of the changed lines 44/44 (100%), branches 20/22. The merge kept main's Spawned mark (ter-2cmo) beside the exact read in AddNumericBodies. Follow-ups filed by the fixer: ter-s0c2 (the hand-off rounds a hull's velocity to float once) and ter-kijn (an attached part's velocity read still comes from its root's float row).
