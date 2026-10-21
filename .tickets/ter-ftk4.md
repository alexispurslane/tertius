---
id: ter-ftk4
status: in_progress
deps: []
links: []
created: 2026-10-21T14:27:09Z
type: feature
priority: 2
assignee: Alexis Purslane
tags: [engine, simulation, physics]
---
# Parts: an attached entity pushes the body of its chain's root

The user proposed this on 2026-10-21 for the demo's plumes: the main engine and each jet become separate entities attached to the hull, each with one emitter, and each pushes the hull. Attachments exist (ARCHITECTURE.md, the paragraph on SpawnDesc.AttachTo): an attached entity follows its parent with an offset in the parent's axes, has no body, cannot be a frame, dies with its parent, and is always in the frame of its chain's root. What is new is the upward flow. The model to design: a part writes only its own intent, as every entity does. At the sync point, the additive intents of each part (force, torque, impulse) flow up its chain to the body of the root. A part's force acts at the part's place, so it gives the body a force and a torque about its center of mass. The exclusive intents (motion mode, end pose, full sample, steer target, character walk, teleport) stay with the root alone, because two writers of one would need someone to decide. A part's push is physics, not a message, like a contact force: the root's components do not interpret it. The sum runs in a fixed order, so it repeats bit for bit. Every reader of a body's force must see the sum, for example the numeric frames' estimate of a hull's acceleration (World.NumericFrames.cs near line 116). Costs to state: commands from the hull's autopilot reach the jets by message, one tick later than today, and the demo's turning jets become pairs of forces, so the attitude autopilot's pure torque gives way to an allocation of jet throttles. Engine change, full engine process, design doc in docs/plans/ first.

