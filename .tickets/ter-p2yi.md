---
id: ter-p2yi
status: open
deps: []
links: []
created: 2026-10-17T02:58:53Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Ghosts never sleep, so a body that rests on a ghost never sleeps either

From ter-ejx9, found by test: the ghost pass sets every ghost's pose in every step, and Jolt resets a body's sleep timer when its pose is set. A body that rests on a ghost shares its island, so it never sleeps. Skip the set when the ghost's pose and velocity did not change, and make sure that the contact ledger's held pairs still end correctly (HeldPairEnded reads IsAwake).


## Notes

**2026-10-17T07:05:17Z**

2026-10-17: the ter-dq92 agent disputes the second half of this ticket. It reads Jolt 5.6 ContactConstraintManager.cpp as linking only two dynamic bodies into an island, and in its benchmark 100 boxes slept on a kinematic mesh ghost that held still. The ter-ejx9 agent had found by test that a body resting on a kinematic ghost whose pose the ghost pass sets in every step never sleeps. Both can hold: the pose set may keep the ghost awake and wake the bodies around it (WakeAround), not the island. Settle it with one test before you fix anything.
