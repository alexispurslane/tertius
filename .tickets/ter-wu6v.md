---
id: ter-wu6v
status: open
deps: [ter-ziae, ter-1j1e, ter-84co]
links: []
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, simulation]
---
# Report when a contact ends, not only when it starts

The only contact event is Collision, which means that physics reported a contact that started (ARCHITECTURE.md, the engine events). A component cannot learn that a contact ended. So it cannot tell that something left a pressure plate or stopped touching a door.

Facts:

- FrameWorld (src/Tertius.Physics/Internal/FrameWorld.cs) already sees removals. DrainContacts counts the touching pairs. It holds a pair whose count reaches zero, because Jolt drops a pair when a body sleeps and adds it again when the body wakes. A held pair that ends is dropped without a report.
- A character's held pair ends only when the character's own contact list, the bodies within 0.1 m, lacks the other body.
- PhysicsStepOutput.Contacts holds only ContactStarted.
- ARCHITECTURE.md, "Contacts", documents the hold.
- A character's contact starts only when Jolt finds a contact for its inner body (ter-84co), so that bug comes first.

## Design

Open questions:

- One event type with a kind (started or ended), or two event types?
- What an end means when a body changes frame, sleeps, or is destroyed.
- Whether each entity can read the set of entities that it touches now. The backend already keeps that set for each frame system.

## Acceptance Criteria

Each contact that starts ends later with exactly one end event to each of its two entities. This holds when a body sleeps, wakes, leaves its frame or is destroyed. Sleep and wake alone send no end and no new start. Tests on the World with Jolt cover each case, with a character among the bodies.


## Notes

**2026-10-16T12:28:54Z**

Decided (User, 2026-10-16): a contact start is reported at a real touch, the first step with an applied impulse or zero separation, and at an overlap for a sensor, not when Jolt adds the pair at its speculative contact distance (up to 2 cm early, found by the deep-yard scene). Starts and ends are two event types, Collision and CollisionEnded, so that each reader picks its type with Of<T>(). Each start ends with exactly one end to both entities; sleep, wake and moves between numeric frames send nothing; a destroyed body ends all of its contacts; sensors use the same events. Impulse is an estimate from the approach speed today; check whether the backend can read Jolt's solved impulses.
