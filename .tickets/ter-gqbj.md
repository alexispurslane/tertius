---
id: ter-gqbj
status: closed
deps: []
links: []
created: 2026-10-12T22:20:38Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s02r
tags: [physics, frames]
---
# Split the entity row into a semantic frame and a physics home, and compute the gameplay velocity as a Double3

Design: docs/plans/numeric-frames.md. Step 1 of the order of the work. The row frame stays semantic, and a new field names the physics home, which is still the semantic frame for every body. Every read of a position or velocity names its frame (User): PoseIn, VelocityIn and AngularVelocityIn on Self and World, TryGetPoseIn, TryGetVelocityIn and TryGetAngularVelocityIn on Snapshot. The reads without a frame go away, and Self.Transform becomes a setter relative to self.Frame. A velocity read is a Double3: the home's frame velocity plus the float local velocity, converted through the tree for another frame. Writes stay relative to self.Frame, because an entity writes only itself (User). Self.SetVelocity and SpawnDesc.LinearVelocity take a Double3. A set velocity, a spawn velocity and intent poses stay in self.Frame until physics applies them, and physics converts them then. Directions stay in self.Frame's axes (User).

## Acceptance Criteria

Every suite passes in Debug and Release. A Double3 velocity set or spawned in self.Frame reads back through VelocityIn. (The 7,672.123456 m/s precision test belongs to ter-jng0, because it needs a numeric frame.) No gameplay surface changes its frames.

