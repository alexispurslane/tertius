---
id: ter-j4to
status: open
deps: [ter-7e55]
links: [ter-gj55]
created: 2026-10-11T23:45:16Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [simulation]
---
# World.GetComponent and GetComponents read live component lists with no guard against a call during a tick

World.GetComponent<T>(Entity) and World.GetComponents(Entity) in src/Tertius.Simulation/World.cs read an entity's live component list. Spawn, Destroy, AddComponent and the other World members that change live state call ThrowIfInTick, but these two reads do not.

A component that holds the World, for example one that a scene built with it, can call them during a tick. There they race with Self.AddComponent, which replaces the list in the parallel phase. They also break the rule that an entity never reads another entity's components. Only tests and the host call them today.

Found while writing the tickets of the queries and sensors epic (ter-1yed).

## Acceptance Criteria

Both methods throw during a tick, as the other World members that touch live state do, or the design of ter-gj55 replaces them. A test shows the throw.

