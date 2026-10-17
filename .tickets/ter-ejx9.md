---
id: ter-ejx9
status: closed
deps: []
links: []
created: 2026-10-16T13:36:33Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, contacts]
---
# A contact across a bubble hand-off ends and starts again when the two bodies do not move together

From ter-wu6v: a contact goes on across a hand-off only if the other entity's body moves into the same system, because contacts with ghosts are not reported. So a crate that enters a frame ahead of a crate that it touches gets Started, Ended, Started. A body that moves into a falling numeric frame away from an anchor that it rests on ends that contact too. Decide whether ghost contacts should carry such a pair.


## Notes

**2026-10-17T01:25:16Z**

Decided with the user (2026-10-17), from two designs: ghost contacts are contacts of the two entities, and a contact can start through a ghost. A ledger per entity pair counts the systems that hold the pair as started: Started when the count goes from 0 to 1, Ended when it goes back to 0, so each entity pair is reported once also when two systems hold mirror ghost pairs. A ghost stands for its real body, so the impulse uses both real masses. This fixes the crate that enters a frame ahead of the crate it touches, the anchor that a body leaves for a falling numeric frame, and a fast crate in a falling numeric frame that hits a static wall's ghost and hears no Collision. Rejected: ghosts that only carry a started contact, which leaves the wall silent.

**2026-10-17T05:13:32Z**

Merged with the contact sweep. The review's confirmed defects are fixed: a contact handed into a numeric frame waits one step for the numeric update, a ghost of a static body makes only the contacts that the static body makes, a stale anchor ghost is dropped in the same step, static mesh ghosts shift with their frame, and a carry between real bodies checks that they can meet. A contact that really ended at a hand-off into a numeric frame can end up to two steps late. Follow-ups: ter-3mwi, ter-9sri, ter-dq92, ter-7ul9, ter-p2yi.
