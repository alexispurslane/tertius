---
id: ter-i9hy
status: open
deps: []
links: []
created: 2026-10-12T02:18:46Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, characters]
---
# A character's supporting volume and its ground state after a spawn or a teleport are off

Found in the fixes of ter-l6oo, not caused by them.

1. NewCharacter (JoltPhysicsBackend.Bodies.cs) sets SupportingVolume = new Plane(Vector3.UnitY, -capsule.Radius), and its comment says that only contacts below the center of the lower hemisphere support the character. Jolt treats a contact as supporting when the normal times the contact's local position, plus the plane constant, is below 0. The capsule is centered on the character's origin, so this plane accepts contacts up to 0.3 m above the center. The comment needs a plane constant equal to the half height, 0.6 m. Jolt's samples use -radius because their capsule's origin is at the feet.
2. A newly spawned character starts in Jolt's ground state InAir with no contact refresh, so it can stand only from its second step.
3. A teleport moves a character but does not refresh its contacts, so its next update uses the ground state of its old place. A hand-off and a growth already refresh them.

## Acceptance Criteria

Only contacts on the lower hemisphere support a character, and a character stands in the first step after a spawn or a teleport onto walkable ground, each locked in by a test.

