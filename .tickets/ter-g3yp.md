---
id: ter-g3yp
status: open
deps: [ter-ziae, ter-1j1e]
links: []
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, content]
---
# Collision groups: a body can be in several groups, and content chooses which groups collide

Every body has one of four fixed object layers today: Static, Moving, FrameHull and Ghost (BodyLayer in src/Tertius.Core/Seams/PhysicsSeam.cs). A private pair table in src/Tertius.Physics/Internal/Layers.cs says which layers collide. Content cannot add a category, such as debris that passes through people. No code can ask what a body collides with.

Facts:

- BodyDesc.Layer is checked only with Enum.IsDefined. The backend overrides it for frame hulls, interiors, character inner bodies and ghosts.
- joltc builds with 32 object layer bits. JoltPhysicsSharp 2.23.0 exposes ObjectLayerPairFilterMask, which splits a layer into 16 category bits and 16 mask bits. Tertius uses the table variant today.
- The binding also exposes Jolt's GroupFilterTable. It acts only during simulation, and it filters only inside one group. Queries filter with a BodyFilter instead.
- BodyInterface.SetObjectLayer can change a layer at run time. Nothing in Tertius calls it.
- The content formats design has one collision key in the body section: layer, with the values "static" and "moving".

## Design

Open questions:

- Do masks replace the four layers, or sit beside them? Ghosts must still meet only Moving bodies, and frame hulls must still meet only Static bodies and other hulls.
- Does content name the categories, and where does the table of names live?
- Can a category or a mask change after spawn?
- Does a mask also filter queries and sensors by default?

## Acceptance Criteria

A body's description names its collision groups (several are allowed) and the groups that it collides with. Two bodies collide only when each body collides with at least one of the other body's groups, which is Jolt's own pair test. Ghosts, frame hulls and interiors keep their current behavior. A test on the World with Jolt shows two collision groups that pass through each other and a third that hits both.


## Notes

**2026-10-12T01:17:59Z**

Name: the user calls a category a collision group, and the queries of ter-gj55 filter by collision group membership (User). Jolt's CollisionGroup and GroupFilterTable are a different mechanism, so the code must keep the two names apart. Open point added: is a body in exactly one collision group, or in several? One group per body lets the query's group filters join by a bitwise AND.

**2026-10-12T01:25:35Z**

A body can be in several collision groups (User). Jolt supports this: joltc builds with 32-bit object layers, and ObjectLayerPairFilterMask splits a layer into 16 group bits and 16 mask bits. Two bodies collide when (group1 & mask2) != 0 and (group2 & mask1) != 0, so each body's mask is an any-of test. Limits: 16 group bits in all, and Jolt allows only 16-bit or 32-bit object layers. The engine's own four layers (Static, Moving, FrameHull, Ghost) must be encoded in the same bits, so content gets about 12 groups. Finer filtering past that would need OnContactValidate, a managed callback for each contact pair. Naming: the user reserves 'collision mask' for the query side, which is an all-of test. A body's own set of groups that it collides with is an any-of test, so it needs another name, for example 'collides with'.

**2026-10-16T12:35:49Z**

Decided (User, 2026-10-16): a body has a collision mask, the same type as a query's, so the word keeps one meaning: two bodies collide when each body's mask matches the other body's collision groups. Jolt's mask filter tests (group1 & mask2) != 0 and (group2 & mask1) != 0, which is the any-of part only, so a body's mask may hold only InAny groups; the engine refuses all-of and none-of parts on a body at spawn, because they would need a managed callback for each contact pair. The names of collision groups live in content, one list per game that mods can extend up to the cap; blueprints name groups by string, and loading resolves them to bits and fails past the cap (User). A component can change its own body's groups and mask by an intent, which physics applies at the next step through SetObjectLayer (User). A query option to hit what a given group hits ('cast as Bullet') waits until a feature needs it (User).
