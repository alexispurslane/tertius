---
id: ter-1w06
status: open
deps: []
links: [ter-kugq, ter-tftg]
created: 2026-10-17T13:44:29Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, character]
---
# A weightless character that presses into a dynamic crate never moves it, while its velocity read grows

Found by the pushed-contact precision measurement (ter-kugq, branch worktree-wf_c4566912-569-1, test tests/Tertius.Physics.Tests/Precision/PushedContactMeasurement.cs, scene character-pushes-crate).

Scene: zero gravity, numeric frames off, one static root frame. A character (capsule half height 0.6 m, radius 0.3 m, 70 kg) starts 0.02 m behind a 1 m, 20 kg dynamic box along +x. A Pilot adds 105 N along +x on every tick, for 3600 ticks at 60 Hz.

Measured at V = 0 (no common velocity), every sample time:
- Neither body changes position: the center of mass never leaves the origin, and the x-distance between the centers changes by 1.1e-16 m.
- The velocity reads grow as if the character were free: |v_character - v_crate| reaches 89.98 m/s at 60 s, which is (105/70) x 59.98 s, and the crate reads 0.
- No contact ends.
The same frozen state holds in direction y at every common speed up to 300 km/s, and in every distance-control run (start points up to 9e6 m).

When the common velocity has a part along the push (direction (1,1,1)/sqrt(3), any V from 1 m/s up), the contact breaks and the character passes the crate: x-distance between the centers about -2600 m at 60 s, crate tilt up to 177 degrees, and at V = 1 m/s 12 contacts begin and 12 end. From V = 2000 m/s up, the slip across x at 5 s is exactly one tick of the cross velocity, V x sqrt(2/3) / 60.

Probable area: UpdateCharacter and WithoutMotionIntoContacts in src/Tertius.Physics/JoltPhysicsBackend.Step.cs. A character that does not stand keeps its velocity against a dynamic body, so it can push it, but here Jolt's CharacterVirtual never moved the crate. Also see ter-tftg (a walking character slows when it touches a light resting crate). Done means: a weightless character that presses into a free crate moves it, the pair's momentum follows the thrust, and the velocity reads agree with the positions.

