---
id: ter-61ds
status: open
deps: []
links: [ter-1w06]
created: 2026-10-17T14:08:37Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, character]
---
# The character's supporting volume reaches 0.3 m above the capsule's center, not the lower hemisphere that its comment names

NewCharacter in src/Tertius.Physics/JoltPhysicsBackend.Bodies.cs sets SupportingVolume = new Plane(Vector3.UnitY, -capsule.Radius), with the comment 'Contacts below the center of the lower hemisphere support the character'. Jolt's Plane::SignedDistance is p.Dot(normal) + constant, and UpdateSupportingContact skips a contact whose signed distance in character space is above 0. The capsule is centered on the character's position (ShapeCache.CreateCapsule, no shape offset), so the plane is y = +0.3 m and every contact below 0.3 m above the center can support, including the side of the cylinder. Jolt's samples use this plane with a capsule offset so that the origin is at the feet. Found in ter-1w06: a weightless character beside a crate is OnSteepGround, not NotSupported. Check which volume the user wants and what it changes (a character wedged between two walls, a ledge at hip height).

