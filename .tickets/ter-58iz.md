---
id: ter-58iz
status: open
deps: [ter-g3yp]
links: []
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, simulation, gameplay]
---
# Let a component read another entity's physics facts: motion, mass, shape and collision groups

Snapshot tells a component only whether another entity has a body (HasBody). The component cannot read the other body's motion type, mass, shape, collision category and mask, or whether it is a character or a sensor. A component that decides whether it can push, lift or stand on another entity needs these facts.

Facts:

- A body's description (BodyDesc) is fixed at spawn. Only a frame hull's motion changes, with its frame's driver. So a read-only view of the description cannot race with the tick.
- EntityTable.Body is internal, and Simulation does not reference Physics.
- The backend keeps sets of touching pairs for each frame system (touching, held and characterTouches in FrameWorld), private to the physics thread.
- The editor design plans a World.GetBody (docs/plans/editor.md, step B6).
- Snapshot also lacks a frame's owner and an entity's attachments. The World exposes both outside a tick.

## Design

Open questions:

- Which facts to publish, and whether the set of entities that an entity touches now is one of them.
- Whether the view is a struct in the snapshot or a call on Snapshot.

## Acceptance Criteria

From Snapshot, a component reads another entity's motion, mass, shape kind, bounding radius, collision groups and the groups that it collides with, and whether it is a character or a sensor. A read for a stale entity gives an explicit false or null. Tests cover each fact.

