---
id: ter-6r8c
status: open
deps: [ter-k8cg]
links: []
created: 2026-10-17T21:44:05Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# Kepler, Spin and Static motion become components, and every frame follows its owner

Part of the epic on frames that follow their owners. Read the epic and its design doc first. The driver state moves onto components of the frame's entity, which call the rails library and hand the engine a motion sample each tick. A frame follows its owner for every kind of motion, as it follows a Physics hull today. DriveKinematicHulls and the tree's own advance of rails frames go. Scenes, scripts, state dumps and the docs (ARCHITECTURE.md, docs/plans/numeric-frames.md) follow.

