---
id: ter-gqbj
status: open
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

Design: docs/plans/numeric-frames.md. Step 1 of the order of the work. The row frame stays semantic, and a new field names the physics home, which is still the semantic frame for every body. Self.Velocity, Self.SetVelocity, SpawnDesc.LinearVelocity, World.GetVelocity and Snapshot.VelocityOf become Double3. The getter adds the home's frame velocity to the float local velocity. A set velocity, a spawn velocity and intent poses stay in the semantic frame until physics applies them, and physics converts them then.

## Acceptance Criteria

Every suite passes in Debug and Release. A body set to 7,672.123456 m/s reads back the same value to 1e-6 m/s. No gameplay surface changes its frames.

