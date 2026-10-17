---
id: ter-ozm8
status: open
deps: [ter-zg77]
links: []
created: 2026-10-17T21:44:05Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [frames, physics, architecture]
---
# Frames follow their owners: Kepler, Spin and Static motion become entity components

Design: docs/plans/rails-components.md (the design ticket of this epic writes it).

User decisions (2026-10-17):
- The full version, for consistency. The state of the Kepler, Spin and Static drivers moves from the frame tree onto components of the frame's entity. Every frame with an owner follows its owner, as a frame with the Physics driver follows its hull today. The reverse path goes away: the tree moving a frame on rails, and physics pushing the frame's hull after it (DriveKinematicHulls).
- The math of the rails moves into a library of static methods, in a namespace under the frame tree code. Components and engine readers both call it. Examples: a Kepler orbit's place, velocity and angular velocity at a time, the orbit's chord over a step (NumericFrameRules.OrbitChord), the orbit's acceleration, a spin's turn over a step, and the prediction of a frame at the end of a step (FrameTree.KeplerMotion).
- The spin gravity epic (ter-s9m0) waits for this epic. This epic comes after the numeric frames epic (ter-uu1x), whose ghost change edits the same orbit frames and anchors.

Points that the design must settle:
1. The motion sample that a rails component hands over each tick: the place at the end of the next step, the chord velocity, the acceleration and the angular velocity. How physics, the ghost pass, hand-offs, the orbit frame and render interpolation read it. The component computes one step ahead, because its tick N output acts in the step of tick N+1.
2. The sync point order. Today step 6b places rails frames before the numeric update in step 6c. With components, the samples come from the gameplay of the tick before.
3. Numeric frames. The orbit frame of a Kepler hull (NumericMode.Orbiting) becomes a numeric frame that follows any fast kinematic anchor. NumericBody.Fixed and NumericBody.Orbits become properties of the body's motion, not of a frame driver.
4. Frames without a body, such as planets and rooms: an entity with a component, and the cost of one component call per frame per tick, against the serial loop of the tree today.
5. Driver changes (World.SetFrameDriver, FrameTree.MoveOnOrbit, LeaveOrbit, DriverChangeTests), and what replaces the MotionDriver enum.
6. Determinism, state dumps and saved state.

