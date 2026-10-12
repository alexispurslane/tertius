---
id: ter-gd22
status: open
deps: [ter-g3yp, ter-wu6v, ter-gj55]
links: []
created: 2026-10-11T23:45:16Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [physics, simulation, gameplay]
---
# Sensors: trigger bodies, and a Sensor component that tells a topic or every entity what touches its entity

Nothing can notice that a body entered or left a volume without stopping that body. The user wants a sensor to be a component on any entity (User). When something touches the entity, the component sends a message, either to every entity or to a topic. A trigger volume also needs a body that detects touches but does not push, which Jolt calls a sensor body.

Facts:

- JoltPhysicsSharp exposes BodyCreationSettings.IsSensor. Nothing in Tertius sets it.
- Jolt reports sensor touches through the normal contact listener, with ContactSettings.IsSensor set. FrameWorld.OnContactAdded never reads that flag, so a sensor body would be reported as a solid collision today.
- A sensor detects kinematic bodies when their layers collide, and a character's inner body is kinematic. A static sensor loses a body that falls asleep. A kinematic sensor also sees sleeping bodies, and with CollideKinematicVsNonDynamic it also sees static bodies.
- Jolt drops a contact when a body sleeps and adds it again on wake. The backend's hold of removed pairs must cover sensor pairs too, or sleep and wake look like a leave and an enter.

## Design

Open questions:

- A sensor flag on BodyDesc, a sensor motion, or a sensor category?
- The Sensor component's message: the touching entity, the kind (started or ended) and the place. Its recipients: a named topic, every entity (a broadcast), or the entity's own subscribers.
- The component also relays the touches of a solid body, because the user asked for a component on any entity, not only on a sensor body. Decide what it sends for each.
- The cost of a broadcast for each touch, and a filter by mask or by flag.

## Acceptance Criteria

A sensor body detects dynamic, kinematic and character bodies without pushing them, and its entity gets a start and an end for each. The Sensor component on any entity relays its starts and ends to a topic or to every entity. Sleep and wake send nothing. A scripted test shows the person entering and leaving a sensor volume, and a door that opens on the relayed message.


## Notes

**2026-10-12T00:00:10Z**

The user kept the collision masks (ter-g3yp) in the path. A sensor filters by mask and also by component type, through the record of ter-gj55.
