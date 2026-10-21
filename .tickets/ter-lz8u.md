---
id: ter-lz8u
status: closed
deps: []
links: []
created: 2026-10-20T20:27:09Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# The rendezvous steering can hit Lambert's refusal near 0 degrees before the coast

Found by the review fix of ter-tzf6. RendezvousAutopilot steers by KeplerOrbit.Lambert in every tick of the transfer until the 60 s coast. The remaining transfer angle shrinks during the transfer, and Lambert refuses an angle within LambertRefusedAngle (1 degree) of 0. For the demo plan and the short test plan the angle at the coast is about 4 degrees, so neither is affected. A plan whose remaining angle falls below 1 degree more than 60 s before the arrival would throw in every tick and log an error in every tick. Fix it with a plan guard, or with a coast that starts at the angle where Lambert stops, with a test of such a plan. Demo work, no engine change.


## Notes

**2026-10-21T00:25:59Z**

Merged on 2026-10-20. The coast starts 60 s before the arrival or within TransferGuidance.CoastAngle (2 degrees) of the arrival point, whichever comes first. Coverage of the changed lines: 14 of 14, and 15 of 16 branches (the brake check in the angle coast, an equivalent mutant). Follow-up: ter-3o6h.
