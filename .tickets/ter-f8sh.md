---
id: ter-f8sh
status: open
deps: [ter-ai0t, ter-ujbt, ter-1zfs]
links: []
created: 2026-10-11T13:52:09Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-dtw2
tags: [host, slice]
---
# Vertical slice: the person goes through an airlock to the outside

Step 3 of docs/plans/host-and-scripted-runs.md.


## Notes

**2026-10-11T22:00:39Z**

From ter-ai0t: outside the cabin's gravity box the person is in zero gravity, below JoltBackendOptions.ZeroGravityThreshold. There a character moves at its desired velocity alone: PersonControl sends MoveCharacter on every tick, so the person stops dead when no key is held, cannot jump (physics drops PendingJump), and moves only across the plane of its last up. This step must decide the zero-gravity controls, for example momentum when no key is held and a way to move along the up axis.
