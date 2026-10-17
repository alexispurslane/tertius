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

