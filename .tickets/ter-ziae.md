---
id: ter-ziae
status: closed
deps: [ter-dtw2]
links: []
created: 2026-10-11T23:45:16Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [design, physics, simulation]
---
# Design queries, sensors, collision masks and entity flags with the user

Write docs/plans/queries-and-sensors.md for the epic ter-1yed. Design each open decision twice in chat, and let the user choose. Each child ticket of the epic lists its own open questions. The decisions that span them come first:

- Where and when a physics query runs. Gameplay runs beside the physics step, and the step holds the native Jolt lock (JoltRuntime.NativeGate) for its whole length. So a component cannot call Jolt during its Handle call. Three places can answer a query:
  1. The physics thread, at the start of the next step, before it moves anything. Jolt then holds the end of the previous tick, which matches the front snapshot. The answer to a question asked in tick N arrives in tick N+2.
  2. The sync point, after the step. This is safe and serial. Answers arrive in tick N+1, but their cost lands on the serial part of the tick.
  3. A copy of the previous tick's shapes on the gameplay side. Answers come at once. But Simulation has no shapes and cannot reference Physics, so this needs its own intersection code.
- Whether a query is a call that returns at once, or a request message whose answer arrives as an event.
- Determinism. Jolt returns narrow phase hits in a varying order, so every result needs a fixed order, for example by distance and then by entity index.
- Which of these features mods can use. The mod API list in docs/plans/content-packages.md must name them.

## Acceptance Criteria

docs/plans/queries-and-sensors.md exists, the user approved each of its decisions, and each child ticket of ter-1yed names its section.


## Notes

**2026-10-12T00:04:33Z**

The user proposes a small query language over all entities, in the fluent style of LINQ: for example With<Flammable>().Without<Wet>(), built once and then run many times with no allocation. Design it here with ter-gj55. Points to settle: a query runs on the snapshot of the previous tick, not on the live EntityTable, so that workers can run it during a tick without a race. A query that scans every entity costs O(entities) per run, so the builder picks an index first (the record of component types, the spatial grid, or a frame's members) and tests the other filters on each candidate. Run returns a ref struct enumerator, as EventList.Of does, in entity index order. The same filter object can also filter ray casts, overlaps and sensors.

**2026-10-12T00:10:14Z**

The user chose the query words for both an inbox and entities (User). Pick by type: Of<T>() for events, With<T>() and Without<T>() for entities. Filter: Where(predicate) with a static lambda, and Where(state, predicate). Place and sender: From(sender) for events, In(frame) and Near(center, radius) for entities. Endings: foreach, Count(), Any(), TryFirst(out), CopyTo(list) for entities, Remove() for events. A query is a readonly struct value, inline or stored. The inbox's special methods (Contains<T>, CountOf<T>, IndexOf<T>, RemoveAll) go away in favor of the chain (User). ter-8wmm does the inbox part.

**2026-10-12T00:18:00Z**

How a query is stored (User): a query has two parts. The description (EntityQuery, EventQuery<T>) is a plain readonly record struct with no source: component type masks built like EventMask (With, Without), a frame for In, a radius for Near, a sender for From, and a static predicate for Where. A field can hold it. The run (for example FoundEntities, and EventsOf<T> for the inbox) is a ref struct that binds one description to one source: Snapshot.Find(query, around) or events.Find(query). It picks the index when foreach starts. Values that change on each run, such as the center of Near and the state of Where(state, predicate), are arguments of Find, not parts of the description. The user also kept the shorthand chains that start from a source, such as events.Of<Damage>() and snapshot.Entities.With<T>(). They build the same description and run it, with no filter code of their own.

**2026-10-12T00:23:33Z**

The user's query design (User), which replaces the class sketch: EntityQuery and EventQuery are value types (record structs) that hold the With and Without masks directly, as component or event type masks built like EventMask. Masks join with bitwise operations, so a type-only query never allocates. Frames, radii and predicates go in an optional heap object (QueryExtras), which most queries lack. Building that object on a gameplay worker during a tick is banned: it throws, and the message says to build the query once as a static member. Open questions for the design doc: (1) an event has one type, so an event mask means any of these types, while an entity's With mask means all of these types, and a typed foreach over ref T needs one type; (2) ORing masks gives AND, so a real OR of queries goes in QueryExtras if it is ever needed, and a With that overlaps a Without matches nothing; (3) a static field initializer runs at first use, which can be a first Handle call during a tick, so the World runs each component type's class constructor when it first sees the type, outside the gameplay phase, and the ban needs a ThreadStatic marker that the scheduler sets around each entity run; (4) a 512-bit mask per entity in the snapshot costs 64 bytes each, double-buffered, so consider interning the distinct component sets behind a 4-byte id, and the mask width caps the number of component types that queries can name.

**2026-10-12T00:26:52Z**

Events keep only Of<T> (User). An event has exactly one type, so an event query has no With or Without and no type mask: the payload type is the type parameter, and a typed foreach over ref T always works. This settles open question (1) of the query design note. An EventQuery<T> holds only its other filters, From(sender) and Where.

**2026-10-16T12:06:51Z**

Design state on 2026-10-16, consolidated from the design talk of 2026-10-12. Decided (User): one query language for an inbox and for entities (ter-8wmm, ter-gj55); events keep only Of<T>, with From and Where; entity queries are value types with inline component and collision masks, an optional heap part built only outside a tick, InAny one group per call, and no query joining; 512-bit component flags per entity with dirty copies, a startup cap of 512 component types, and no generic component types; several collision groups per body, on Jolt's 16 group bits, with about 12 left for content; the jargon note on ter-1yed. The current state of the entity query is the newest note of ter-gj55. Still open, for the design doc: where and when a physics query runs (the three places above); a call or a request message; the fixed order of results; which features mods can use; the name of a body's own collides-with set; whether the engine's four layers stay beside the groups; the Near center; static initializers and the in-tick ban; public facts; the Sensor component's recipients and message; one or two contact end event types.

**2026-10-16T12:22:45Z**

Decided (User, 2026-10-16): a physics query (ray cast, shape cast, overlap) is a request that the sync point runs after the physics output lands, against Jolt at the end of step N. Its answer arrives as an event in the asker's inbox in tick N+1, with a tag that the component chose, so it can match answers to questions. The tick stays parallel. Rejected: physics before gameplay with synchronous calls (it gives up the overlap of physics and gameplay), answers at the start of the next step (tick N+2), and a gameplay-side copy of the shapes (a second intersection code that drifts).
