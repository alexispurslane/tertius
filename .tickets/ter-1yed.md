---
id: ter-1yed
status: open
deps: [ter-ziae, ter-g3yp, ter-wu6v, ter-juuk, ter-3euw, ter-gd22, ter-58iz, ter-gj55, ter-8wmm]
links: []
created: 2026-10-11T23:45:16Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [physics, simulation, gameplay]
---
# Queries and sensors: what a component can learn about the world and about other entities

A component can learn very little about the world around it. It reads other entities only through the front snapshot of the previous tick: their pose, velocity, render row and whether they have a body. Physics answers no questions. Nothing marks a body as a trigger. No entity can see another entity's components. Games need all of these, for aiming, line of sight, use keys, triggers, doors, and rules such as "this burns" or "this is crew".

This epic adds:

- collision categories and masks that content can name,
- an event when a contact ends,
- ray casts, shape casts and overlap queries from components, also across frame bubbles,
- sensor bodies, and a Sensor component that tells a topic or every entity what touches its entity (User),
- reads of another entity's physics facts,
- flags as components, which other entities can see (User).

Design doc: docs/plans/queries-and-sensors.md. The first child ticket writes it with the user.

Order: the epic starts after the vertical slice (ter-dtw2) and the shared test helpers (ter-1j1e), so that its tests use the World on Jolt from the start. The content formats epic (ter-jwzd) waits for this epic, because the blueprint body section must carry the masks and the sensor flag, and a flag is an entry under a blueprint's components key.

