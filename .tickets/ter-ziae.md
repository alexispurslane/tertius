---
id: ter-ziae
status: open
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
