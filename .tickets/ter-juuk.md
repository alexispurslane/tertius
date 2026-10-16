---
id: ter-juuk
status: open
deps: [ter-g3yp, ter-gj55]
links: [ter-f8sh]
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, simulation, gameplay]
---
# Ray casts from components, within a frame and across frame bubbles

Design: docs/plans/queries-and-sensors.md, section Physics queries.

A component cannot ask what lies along a line. Aiming, line of sight, a use key that acts on what the person looks at, and placing objects all need a ray cast. The airlock step of the slice (ter-f8sh) works around the gap: its use key sends Use to every entity within reach, which it finds with Snapshot.QueryRadius. That query tests entity origins only.

Facts:

- No ray, shape or overlap query exists in src. IPhysicsBackend has no query method.
- JoltPhysicsSharp exposes NarrowPhaseQuery.CastRay with RVector3 origins, four collector types (closest, any, all, and all sorted) and four filter classes. The float overloads throw in double precision.
- RayCastResult holds the body, the fraction of the ray and the sub-shape. The hit point is the origin plus the direction times the fraction. The normal needs Body.GetWorldSpaceSurfaceNormal under a body lock.
- The binding's filter classes do not own their native handles, so Dispose frees nothing. Wrap them with NativeOwnership.Owned. joltc lacks IgnoreSingleBodyFilter, so a filter that skips the caster's own body is a hand-written BodyFilter subclass.
- Queries never hit a CharacterVirtual. They hit its inner body, which the backend puts on the Moving layer, and whose user data names the entity.
- Ray casts ignore back faces by default, so a ray that starts inside a mesh hull passes through it.
- Each frame has its own Jolt system. A parent system shows a child frame only as its hull and its ghosts. The child's interior and contents exist only in the child's system.
- A ray that crosses a bubble must test the bubble, cast again in the other frame's axes, map the hits back, and merge them by distance. A bubble is an ellipsoid, and FrameBounds has no ray test today. A body that straddles a bubble can be hit in both systems, so the hits need a dedupe by entity.
- Every native query must run under JoltRuntime.NativeGate (project memory, Jolt pitfalls).
- The design ticket decides where and when queries run.

## Acceptance Criteria

A component casts a ray from a point along a direction in its own frame, with a length and an entity query as its filter: a collision mask and a component mask (ter-gj55). It gets the nearest hit: the entity, the point and the normal in its own frame, and the distance. A ray that crosses into a child frame or out to the parent finds the bodies there. The same world gives the same hits in every run. Tests on the World with Jolt cover a hit, a miss, a mask, a ray that starts inside a hull, and a ray across a bubble.


## Notes

**2026-10-12T00:00:10Z**

The user kept the collision masks (ter-g3yp) in the path. A ray filters by mask and also by component type, through the record of ter-gj55, for example only entities with Crew.
