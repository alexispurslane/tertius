# Queries and sensors

Epic: `ter-1yed`. Design ticket: `ter-ziae`. The user made the decisions marked (User), in the design talk of 2026-10-12 and on 2026-10-16.

## Goal

A component can learn what it needs about the world and about other entities:

- what lies along a line, and what overlaps a shape,
- what touches its entity,
- which components another entity has, and which collision groups its body is in.

One small query language serves an inbox and the world's entities.

## Why

Today a component reads other entities only through the snapshot of the previous tick: their pose, velocity and render row, and whether they have a body. Physics answers no questions. Nothing marks a body as a trigger, and no event says that a contact ended. No entity can see another entity's components. Aiming, line of sight, use keys, triggers, doors, and rules such as "this burns" or "this is crew" need all of these.

## Terms

| Term | Meaning |
|---|---|
| Collision group | A named group that a body is in. A body can be in several. |
| Collision mask | A filter over collision groups. It has an all-of part, a none-of part and one any-of set. A query has one, and so has a body. |
| Component flags | The bitset of component types that an entity has, which the snapshot publishes |
| Component mask | A filter over component types: the types that an entity must all have, and the types that it must not have |
| Entity query | A filter over the world's entities: a component mask, a collision mask, places and predicates |
| Event query | A filter over an inbox: one payload type, a sender and a predicate |
| Physics query | A ray cast, a shape cast or an overlap test against the physics bodies |
| Sensor body | A body that detects touches but does not push. Jolt calls it a sensor. |
| `Sensor` component | A component on any entity that relays what touches its entity |

## Decisions

| Question | Decision |
|---|---|
| One language for an inbox and for entities | Yes, with shared words and endings (User) |
| Type filters | `Of<T>()` for events only. `With<T>()` and `Without<T>()` for entities. (User) |
| The inbox's old shortcuts | Replaced by the chain, so that each question has one way to ask it (User) |
| How a query is stored | A value type with its masks inline, and an optional heap part for the rest (User) |
| Query joining | None. If it ever returns, it is a merge of both chains, named as one. (User) |
| Collision groups per body | Several (User) |
| Any-of over groups | `InAny(group)` takes one group per call and widens one any-of set (User) |
| A body's own filter | A collision mask with an any-of set only, tested both ways, as Jolt does (User) |
| Where group names live | In content, resolved to bits at load (User) |
| Changing groups after spawn | Yes, by an intent (User) |
| Component flags | 512 bits per entity in the snapshot. The sync point copies only the flags that changed. (User) |
| Component types | At most 512, checked at startup and at each later load. Generic component types are refused. (User) |
| Data on flags | Not now. A flag says only whether an entity has the component. (User) |
| Where a physics query runs | At the sync point, after the physics output lands. The answer arrives as an event in the next tick. (User) |
| When a contact starts | At a real touch. Jolt's earlier report of the pair does not count. (User) |
| Contact event types | Two: `Collision` and `CollisionEnded` (User) |
| The `Sensor` component | It relays `Entered` and `Left` to a topic or to every entity, with an entity query as its filter (User) |
| `Near` centers | The asker, another entity, or a fixed point (User) |
| "Cast as a group" | Not now (User) |

## The query language

### The words

| Operation | Inbox | Entities |
|---|---|---|
| Pick by type | `Of<T>()` | `With<T>()`, `Without<T>()` |
| Filter by value | `Where(predicate)`, `Where(state, predicate)` | The same |
| Filter by place or sender | `From(sender)` | `In(frame)`, `Near(...)` |
| Filter by collision group | None | `InAll(group)`, `InNone(group)`, `InAny(group)` |
| Endings | `foreach`, `Count()`, `Any()`, `TryFirst(out)`, `Remove()` | `foreach`, `Count()`, `Any()`, `TryFirst(out)`, `CopyTo(list)` |

A predicate is a static lambda, so it captures nothing and allocates nothing. `Where(state, predicate)` passes one value in without a capture. `TryFirst` avoids LINQ's `First`, which throws, and `FirstOrDefault`, whose default struct is ambiguous. `Remove()` removes the matches from the inbox and returns their number. Only an inbox has it, because the snapshot is read-only.

### Event queries

An event has exactly one type, so `Of<T>()` is its only type filter (User). An event query has no component mask and no type mask. `EventQuery<T>` holds a sender for `From` and a predicate for `Where`, so it never allocates.

The inbox's shortcuts go away (User): `Contains<T>()` becomes `Of<T>().Any()`, `CountOf<T>()` becomes `Of<T>().Count()`, and both `RemoveAll` methods become `Where(...).Remove()`. `IndexOf<T>()` goes away. The early exit on the inbox's event mask stays, so a query for a type that the inbox does not hold costs no scan. Ticket: `ter-8wmm`.

### Entity queries

An entity query is a `readonly record struct` (User):

```csharp
private static readonly EntityQuery Targets = EntityQuery.With<Crew>().Without<Dead>().InAny(Wall).InAny(Person);
...
foreach (Entity other in self.Snapshot.Find(Targets))
    outbox.Send(other, new Alarm());
```

where

- `EntityQuery` = the stored description of a query. It holds no source, so a field can keep it.
- `With<Crew>()`, `Without<Dead>()` = the component mask: an entity that has a `Crew` component and no `Dead` component.
- `InAny(Wall).InAny(Person)` = the any-of set of the collision mask: a body in `Wall` or in `Person`.
- `Find` = binds the query to the snapshot of the previous tick and returns a run, a `ref struct` that cannot leave `Handle`.

These parts sit inline, with no allocation:

- The component mask: `With` and `Without`, 512 bits each, built like `EventMask`.
- The collision mask: the all-of part, the none-of part and the any-of set, 16 bits each.
- One `Where` with a static lambda. The compiler caches its delegate.

An optional immutable heap part, `QueryExtras`, holds what does not fit inline: several frames, several centers and radii, and several predicates (User). Building it on a gameplay worker during a tick throws, and the message says to build the query once as a static field (User). See "Static query fields" below.

The filters narrow with each call. The one exception is `InAny`: each call adds one group to the same any-of set, which widens that set, as "any of" means (User). A body in no group of a non-empty any-of set fails it. An empty set is no condition. A query whose `With` and `Without` share a type matches nothing, and building it throws.

There is no query joining (User). If it ever returns, it is a merge: the joined query is the chain of both queries' calls, so every mask joins by OR. It is not the intersection of the two queries' results, so its name says merge.

Two forms exist (User):

- A stored query, as above, for a query that runs again and again, or that a physics query or a sensor uses as its filter.
- A shorthand chain that starts at the source, such as `self.Snapshot.Entities.With<Flammable>()`, for a query written once in place. It builds the same description and runs it, with no filter code of its own. It keeps the filters that do not fit inline on the stack, in an inline array of 8 filters.

A run starts from the smallest candidate set that an index gives: the entities with a component type, a frame's members, or the spatial grid for `Near`. Then it tests the other filters on each candidate. `Without` can only filter, because no index lists what an entity lacks. Results come in entity index order. Ticket: `ter-gj55`.

### Near centers

A stored query needs no run arguments (User):

- `Near(radius)` = within the radius of the entity that runs the query.
- `Near(other, radius)` = within the radius of another entity's position in the snapshot.
- `Near(frame, point, radius)` = within the radius of a fixed point in a frame.

## Component flags

Each entity's snapshot row carries its component flags: 512 bits, so 64 bytes per entity (User). At 100,000 entities with both buffers, that is 12.8 MB, which the user accepts. A new `ComponentsDirty` bit in `EntityTable`'s slot flags marks the slots whose components changed: at spawn, in `ApplyComponentChanges` after an entity's run, and in `World.AddComponent` and `World.RemoveComponent`. The sync point copies the flags of those slots only, so most ticks copy none.

At most 512 component types exist (User). A scan at startup counts the concrete types that implement `IComponent`, and it fails with a clear error past 512. The same check runs for each assembly that loads later, such as a mod. The scan sorts the types by full name, so each type keeps its id in every run with the same set of types. Generic component types are refused, open or closed (User). The two generic test components, `Boxed<T>` and `SendOnce<T>`, become non-generic first.

A flag carries no data that other entities read (User). It says only whether an entity has the component. Data travels by messages, a question and an answer.

`World.GetComponent` and `World.GetComponents` read live component lists with no guard against a call during a tick. The bug `ter-j4to` tracks that. Ticket: `ter-gj55`.

## Collision groups and masks

A body can be in several collision groups (User). The groups are the 16 group bits of the body's Jolt object layer (`ObjectLayerPairFilterMask`, with joltc's 32-bit object layers). The engine keeps 4 of those bits for its own roles: static, moving, frame hull and ghost. It adds them to every body's groups and mask by itself, so content sees only its own 12 groups. Every body starts in a default group, `Solid`, with the mask `InAny(Solid)`, which gives today's behavior.

A body has a collision mask, the same type as a query's (User). For a collision, each body's mask must match the other body's groups. Jolt tests only an any-of set in its pair filter:

```cpp
return (sGetGroup(inObject1) & sGetMask(inObject2)) != 0
    && (sGetGroup(inObject2) & sGetMask(inObject1)) != 0;
```

where

- `sGetGroup(inObject)` = the collision groups that a body is in.
- `sGetMask(inObject)` = the any-of set of the body's collision mask.

So a body's collision mask holds only an any-of set. The engine refuses all-of and none-of parts on a body at spawn, because they need a managed callback for each contact pair, which costs speed. Jolt's own `CollisionGroup` and `GroupFilterTable` are a different mechanism, and the code keeps the names apart.

The names of the groups live in content (User): one list per game, which mods can extend up to the cap. Blueprints name groups by string, and loading resolves them to bits. Loading fails past the cap. A component can change its own body's groups and mask by an intent, which physics applies at the next step with `SetObjectLayer` (User). Ghosts, frame hulls and interiors keep their behavior, and tests pin it. Ticket: `ter-g3yp`.

## Physics queries

A component cannot ask Jolt anything during `Handle`, because the physics step of the same tick runs beside gameplay and holds Jolt's lock. So a physics query is a request (User):

```csharp
// Tick N: ask. The request goes out with the entity's messages.
self.Physics.CastRay(from, direction, 50, Targets, RayTag.Aim);
// Tick N+1: the answer is an event in this entity's own inbox.
if (events.Of<RayHit>().Where(static (ref RayHit h) => h.Tag == RayTag.Aim).TryFirst(out RayHit hit))
    outbox.Send(hit.Entity, new Shot());
```

where

- `from`, `direction` = the ray's origin and direction, in `self.Frame`.
- `50` = the ray's length, in meters.
- `Targets` = an entity query, used as the filter.
- `RayTag.Aim` = a value that the component picks, so that it can match each answer to its question. Two components on one entity use different tags.
- `RayHit` = the answer: the entity hit, the point, the normal and the distance in the asker's frame, and the tag.

The sync point runs every request of a tick after the physics output lands, against Jolt at the end of the step. The answers go out as events, so they arrive in tick N+1, with the snapshot that matches them. The requests run in one batch in entity order. They run serially at first. Whether they can spread over cores depends on joltc's process-wide temp allocator (`JoltRuntime.NativeGate`), which the build step measures.

The query's collision mask runs inside Jolt, in an `ObjectLayerFilter` on Jolt's own group bits. Its component mask runs in a `BodyFilter` against the snapshot's component flags.

The kinds of query:

- `CastRay` gives the nearest hit. `CastRayAll` gives every hit, by distance.
- A shape cast moves a sphere, a box or a capsule along a direction, and gives the first hit.
- An overlap test gives every body that overlaps a sphere, a box or a capsule.

Results come in a fixed order: by distance, then by entity index. A query works in the asker's frame and crosses frame bubbles. It tests the bubble, casts again in the other frame's axes, maps the hits back, and merges them by distance, with one hit per entity. Numeric frames stay hidden: a query names only semantic frames, and its answer is in the asker's frame. A ray that starts inside a mesh hull passes through it, because Jolt ignores back faces. "Cast as a group", which also tests each body's own mask, waits until a feature needs it (User). Tickets: `ter-juuk`, `ter-3euw`.

## Contacts

A contact starts at a real touch (User): the first step with an applied impulse or zero separation, and an overlap for a sensor body. Jolt adds a pair earlier, at its speculative contact distance. That can be 2 cm before a touch, or with no touch at all, so it does not start a contact. The deep-yard scene found that start 11 ticks early at 0.1 m/s.

Starts and ends are two event types, `Collision` and `CollisionEnded`, so that each reader picks its type with `Of<T>()` (User). Each start ends with exactly one end, and both entities get both events. Sleep and wake send nothing. A move between numeric frames sends nothing. A destroyed body ends all of its contacts.

The `Impulse` of a `Collision` is an estimate from the approach speed today. The build step checks whether the backend can read the impulse that Jolt's solver applied. Ticket: `ter-wu6v`.

## Sensors

A sensor body detects touches but does not push. It uses Jolt's `IsSensor`, and the backend reads `ContactSettings.IsSensor`, so that a sensor touch never counts as a solid collision. If their collision masks match, a sensor detects kinematic bodies and characters too. The backend's hold of removed pairs covers sensor pairs too, so that sleep and wake look like nothing.

The `Sensor` component works on any entity, with a sensor body or a solid one (User). It needs no search for its own touches, because the sync point already sends each contact to both of its entities. It reads `Collision` and `CollisionEnded` in its own inbox. It relays them as two event types, `Entered` and `Left`, which name the sensor and the other entity (User). Content names its recipients: a topic, or every entity. It takes an entity query as its filter, so that a plate can react to crew only. A touch in the step of tick N reaches the sensor's entity in tick N+1 and the recipients in tick N+2. Ticket: `ter-gd22`.

## Physics facts of other entities

From the snapshot, a component reads these facts of another entity's body:

- its motion, mass, shape kind and bounding radius,
- its collision groups and mask,
- whether it is a character or a sensor.

A body's description is fixed at spawn, with two exceptions: a frame hull's motion, and the groups and mask that an intent can change. The snapshot publishes those parts beside the component flags. A read for a stale entity gives an explicit `false`. Whether the snapshot also publishes the set of entities that an entity touches now stays open until a feature needs it. Ticket: `ter-58iz`.

## Static query fields

C# runs a static field's initializer at the first use of its class. For a component, that first use can be a `Handle` call on a worker, during a tick. So the World runs each component type's static constructor with `RuntimeHelpers.RunClassConstructor` at the first sight of the type. That happens at a spawn outside a tick, or at the sync point for a spawn during a tick. The scheduler sets a thread-static marker around each entity run. Building a query's heap part while that marker is set throws at once.

## Mods

Every type of this plan goes on the mod API list in `content-packages.md`:

- the query types and their runs,
- the physics query requests and their answer events,
- `CollisionEnded`, `Entered` and `Left`,
- the `Sensor` component and the collision group names.

## Order of the work

The tickets hold the order. In short:

1. `ter-8wmm`: the inbox gets the query words.
2. `ter-g3yp`: collision groups and body masks, with the engine's own bits kept.
3. `ter-gj55`: component flags and entity queries.
4. `ter-wu6v`: contact starts at real touches, and `CollisionEnded`.
5. `ter-58iz`: physics facts of other entities.
6. `ter-juuk`, then `ter-3euw`: ray casts, then shape casts and overlaps.
7. `ter-gd22`: sensor bodies and the `Sensor` component.
