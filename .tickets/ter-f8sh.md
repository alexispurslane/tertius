---
id: ter-f8sh
status: closed
deps: [ter-ai0t, ter-ujbt, ter-1zfs]
links: [ter-juuk]
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

**2026-10-11T23:58:32Z**

Decided with the user, from two designs: E cycles the airlock (a Use to every entity within 3 m; the Airlock component closes the open door, then opens the other 3 s later), and a weightless person thrusts and drifts (physics keeps a weightless character's momentum, forces change it, MoveCharacter and Jump do nothing; PersonControl fires 105 N thrusters along the view, Space and C along its up). The threshold moved to FrameTree.ZeroGravityThreshold so that physics and PersonControl agree. After each weightless step the character loses its velocity into each surface that it hit, along the surface normal: Jolt's contact normal leaned by 6 degrees on the hatch's sill and turned the press of the last gravity step into a drift upward. Leaving the ship's bubble is ticket ter-l6oo.
