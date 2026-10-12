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

**2026-10-12T01:25:35Z**

Jargon and group filters (User, supersedes the earlier note on joining group filters): a body can be in several collision groups, published as a bitset. A query's collision mask holds the groups that a body must all be in and the groups that it must not be in, inline, with no heap part. Two queries join by ORing their component masks and their collision masks, because every filter narrows. A query that wants walls or people asks for a group that both are in, such as Solid.

**2026-10-12T01:30:31Z**

Proposal, not yet decided: any-of group filters that still join additively. Any-of masks alone do not join by a bitwise operation, because (g & A) != 0 and (g & B) != 0 is not one any-of test. So the collision part of a query is in conjunctive normal form: an all-of mask, a none-of mask, and a short inline list of any-of clauses, each 16 bits, for example room for 4. Two queries join by ORing the all-of masks and the none-of masks and appending the clause lists, then normalizing: a clause of one bit moves into the all-of mask, a clause that holds a required group is dropped, a clause that holds another clause is dropped, excluded bits leave every clause, and an empty clause means the query matches nothing. Each clause is Jolt's own test, (groups & clause) != 0, on the same 16 group bits that the body's Jolt object layer holds, so a ray cast's ObjectLayerFilter evaluates the query on ObjectLayerPairFilterMask.GetGroup(layer) with no second copy of the groups. Optional: a query 'as group X' also requires that the body's collides-with set holds X, which is Jolt's other half of the pair test ('hit what a bullet hits'). The same form for component masks would cost 64 bytes for each clause, so component any-of clauses would go in the heap part.

**2026-10-12T01:41:29Z**

The user's simpler variant of the any-of proposal: the collision mask is three inline masks, each joined by OR. AllOf is tested with (g & AllOf) == AllOf, NoneOf with (g & NoneOf) == 0, and AnyOf with (g & AnyOf) != 0, where an empty AnyOf means no condition. It is exact with one rule: a query holds at most one AnyOf mask, so a join of two queries that both have one throws at build time. Without the rule, the join widens: AnyOf {Wall, Person} joined with AnyOf {Person, Crate} would accept a body in Wall alone, which fails the second query.

**2026-10-12T01:44:38Z**

No query joining (User): there is no operator & or other way to build one query from two whole queries. That operator was only in my sketch. A chain still adds one filter at a time, so a second any-of call in one chain is the same case. Two ways to close it at compile time instead of with a throw: the any-of group filter exists only as the first call of a chain (a static start such as EntityQuery.InAnyGroup(...), and on the shorthand's starting view), so no chain can hold two; or no any-of filter at all, with umbrella groups such as Solid. Not yet decided.

**2026-10-12T01:46:01Z**

Decided (User), closing the open any-of point: InAny(group) takes one collision group per call, and each call ORs that group into the query's one any-of set, tested with (g & AnyOf) != 0. An empty set means no condition. So .InAny(Wall).InAny(Person) means 'in Wall or in Person'. A query has exactly one any-of condition, and the one-group signature leaves no way to write two, so nothing throws. The other filters narrow with each call. An InAny call after the first widens the any-of condition, which is what any-of means.

**2026-10-12T01:51:13Z**

If query joining ever returns, define it as a filter merge (User): the joined query is the chain of both queries' calls, so every mask joins by OR, the all-of and none-of conditions narrow and the any-of choices widen. It is not the intersection of the two queries' results, so name it for a merge, not for 'and'.
