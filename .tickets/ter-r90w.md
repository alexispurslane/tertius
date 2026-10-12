---
id: ter-r90w
status: open
deps: []
links: [ter-fa7j]
created: 2026-10-12T13:00:57Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-s02r
tags: [physics, characters]
---
# A person in earth is pinned 128 m behind any body ahead of it along its orbit

Found in the investigation of the crate jitter. Jolt's CharacterVirtual sweeps the character's shape along its whole displacement of the step against other bodies at their start-of-step positions, ignoring their velocities (Jolt 5.6, CharacterVirtual.cpp:569-660, ValidateMovement). UpdateCharacter runs before UpdateSystems moves the rigid bodies. In earth everything moves about 128 m per tick, so a person within 128 m behind a body along the orbital velocity, and within about 1.2 m sideways, is stopped at the body's old place: in a probe its distance to the crate jumped to 128.40 m and stayed there. Jolt also moves the character's inner body with SetPositionAndRotation, so other bodies see it as standing still.

The investigator's proposed fix also helps ter-fa7j: give each Jolt system a reference velocity per step (for example the velocity of its character or of its awake bodies), subtract it from every moving body's velocity before the character update and the system update, and add it back afterward, with its displacement. In an inertial frame such as earth the dynamics do not change, and the sweep and float velocities see nearby bodies as almost still. Cheaper interim options: update the character with velocities relative to the bodies near it, or drop sweep contacts with dynamic bodies that move with the character.

## Acceptance Criteria

A person in earth behind a crate along the orbit, 1 to 100 m away, moves on as if the crate moved with it, and only touches it when they really meet.


## Notes

**2026-10-12T20:08:35Z**

Moved from the vertical slice into the epic ter-s02r, at the user's request: this is a symptom of the numeric job that frames do, which the epic separates from their semantic job.
