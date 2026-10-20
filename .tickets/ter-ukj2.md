---
id: ter-ukj2
status: open
deps: [ter-1b21, ter-fsst]
links: []
created: 2026-10-20T08:17:30Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo]
---
# Move the demo's Passenger onto FrameTree.FeltGravity once characters feel the rotation terms

From ter-fsst (2026-10-20). FrameTree.FeltGravity is the public read of the felt gravity of the last finished step, with the centrifugal and Euler terms. The demo's Clip uses it. Passenger (src/Tertius.Demo/Passenger.cs) still reads the field minus the proper acceleration, because physics gives walking characters no rotation terms until ter-1b21: UpdateCharacter applies only LinearGravity. Passenger's belt pushes against the gravity that it reads, so with FeltGravity it pushed against terms that physics never applied: in the climb the person sat up to 2.37 cm off the seat (bound 1 cm; 3 mm with the old read), from an Euler term of about 1.9 m/s^2 at the seat, and the unbuckle moved from 2.0 s to 5.17 s after the cut, past the end of demo-launch.json's run. Once ter-1b21 has merged, move Passenger onto FeltGravity, run DemoLaunchTests (ThePassengerStaysAtItsSeatThroughTheClimb, InOrbitThePassengerFloatsToThePenAndHoldsIt, InOrbitTheHandTakesThePenWhereAndAsItFloats) with their bounds unchanged, and regenerate scripts/blue-danube.json if the film changes.


## Notes

**2026-10-20T08:22:16Z**

2026-10-20: folded into ter-1b21, whose builder 2 does this move right after its first part.
