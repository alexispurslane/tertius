---
id: ter-m3jm
status: open
deps: []
links: []
created: 2026-10-12T02:04:49Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, characters]
---
# A character in free fall turns when the World rehomes it out of a destroyed frame

Found in the review of ter-l6oo. When a frame is destroyed, World.Rehome (World.Entities.cs) moves each body into the parent frame as BodiesDestroyed plus a BodyCreate. The backend creates the character again with its up taken from the parent's gravity (upFromGravity: true for every BodyCreate). A hand-off keeps the up of a character that does not stand, but this path does not. Example: the demo ship is destroyed while the person floats outside the cabin. The person is rehomed into earth, where gravity is 8.7 m/s², and it snaps to an up that points away from Earth, although it falls freely. ARCHITECTURE.md says that a character in free fall keeps its up.

Design to decide: the backend keeps the up and the standing state when a batch destroys and creates the same entity, or BodyCreate carries whether to take the up from gravity, or the World passes the standing column's value.

## Acceptance Criteria

A character that does not stand keeps its up and its body rotation when a destroyed frame's bodies move to its parent.

