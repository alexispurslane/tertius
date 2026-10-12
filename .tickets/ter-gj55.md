---
id: ter-gj55
status: open
deps: [ter-ziae, ter-1j1e, ter-8wmm, ter-g3yp]
links: [ter-j4to]
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [simulation, gameplay, content]
---
# Let a component see which components another entity has, and find entities by component

A component cannot learn anything about another entity beyond its pose, velocity and render row. Rules such as "this burns", "this is metal" or "this is crew" need facts that other entities can read. The user wants such flags to be components (User). A flag is a component type, often with no state, and other entities ask whether an entity has it.

Facts:

- ARCHITECTURE.md, "Component rules": "An entity reads other entities only through the front snapshot of the previous tick. It never reads another entity's components."
- CLAUDE.md names read-only "public facts" on entities as a feature that was left out because nothing needed it yet. This epic needs it.
- Component lists are not double-buffered. Self.AddComponent replaces a list during the parallel phase, and each component changes its own fields in its Handle call. So a read of another entity's live components during a tick races.
- World.GetComponent and World.GetComponents read live lists with no guard against a call during a tick. The linked bug tracks that.
- The content formats design puts components under a blueprint's components key, and it has no tags or flags key. So a flag needs no new key.

## Design

Open questions:

- How the snapshot publishes which component types an entity has. For example, the sync point copies forward a set of type ids, for every component type or only for types marked public.
- Whether a flag can carry read-only data ("public facts"), such as an ignition temperature, and how the sync point publishes it.
- A query that combines position and flags, such as "entities with Flammable within 5 m", and the same filter for ray casts, overlaps and sensors.

## Acceptance Criteria

A component asks Snapshot whether another entity had a component of a given type at the end of the previous tick, with no race. A component finds the entities within a radius that have a given component type. A component that is added or removed shows in the next tick. Tests on the World cover a flag that is added, one that is removed, and reads from many workers at once.


## Notes

**2026-10-12T00:10:15Z**

The entity query uses the shared words (User): With<T>(), Without<T>(), Where, In(frame), Near(center, radius), and the endings foreach, Count(), Any(), TryFirst(out Entity) and CopyTo(list). It runs on the snapshot in entity index order, and it starts from an index (component type, frame or spatial grid) instead of a scan. The inbox gets the same words first, in ter-8wmm.

**2026-10-12T00:18:00Z**

The query has a stored form (see the note of ter-ziae): EntityQuery, run with Snapshot.Find(query, around), returning a ref struct run. snapshot.Entities.With<T>() stays as the shorthand chain, with no filter code of its own.

**2026-10-12T00:23:33Z**

See the note of ter-ziae on the user's query design: a value-type query with With and Without masks, an optional heap object for frames, radii and predicates, and four open questions. The record of component types that this ticket publishes is the per-entity mask that the query tests, possibly interned behind a component set id.

**2026-10-12T00:31:28Z**

Decisions (User): each entity's snapshot row carries a 512-bit component mask (64 bytes, 12.8 MB at 100,000 entities with both buffers), and the user accepts that cost and the cap of 512 component types. Only changed masks are copied forward: add a ComponentsDirty bit to EntityTable's SlotFlags beside TransformDirty and RenderDirty, mark it where ApplyComponentChanges, World.AddComponent and World.RemoveComponent change a list (and at spawn), and let CopyDirtyRows copy the mask of those slots only. Enforce the cap at startup (User): scan the loaded assemblies for concrete types that implement IComponent and fail with a clear error past 512. Points to settle: assemblies that load later, such as mods, need the same check when they load (AppDomain.AssemblyLoad or the mod loader); a startup scan cannot count closed generic component types such as the tests' Boxed<T> and SendOnce<T>, so either refuse generic component types or count each closed type when it is first seen; sorting the types by full name gives each type the same id in every run with the same set of types.

**2026-10-12T00:35:43Z**

Generic component types are refused entirely (User). The startup scan, and the check for assemblies that load later, fail with a clear error on any generic type that implements IComponent, open or closed. Two test components are generic today and must become non-generic first: Boxed<T> in tests/Tertius.Simulation.Tests/BlueprintSupport.cs and SendOnce<T> in tests/Tertius.Host.Tests/HostTesting.cs.

**2026-10-12T01:17:59Z**

The entity query filters by collision group membership too, not only by components (User). The query language exists to build on both flags and collision groups. So the query struct also holds group filters inline (a few bits each, no allocation), the snapshot publishes each entity's groups beside its component mask, and this ticket waits for the groups of ter-g3yp. How group filters join depends on an open point of ter-g3yp: if a body is in exactly one group, 'in any of A' and 'in any of B' join into 'in any of A and B both' by a bitwise AND; if a body can be in several groups, they do not, and a second group filter goes in the heap part.
