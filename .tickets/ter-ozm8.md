---
id: ter-ozm8
status: closed
deps: [ter-zg77]
links: []
created: 2026-10-17T21:44:05Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [frames, physics, architecture]
---
# Frames follow their owners: Kepler, Spin and Static motion become entity components

Design: docs/plans/rails-components.md. The design doc is final (written under ter-skca, 2026-10-17). Work tickets, in order: ter-k8cg (the library), ter-6r8c (the samples and the components), ter-ru5u (a body's motion changes after spawn, and the MotionDriver enum goes), ter-zg77 (a following frame follows every fast kinematic hull).

Decisions of the design doc (the user decided Q1 to Q4 on 2026-10-17, and the orchestrator took the doc's recommendation for Q5 and Q6):
- State of rails motion: on components of the frame's entity, KeplerMotion and SpinMotion. The tree keeps no driver, orbit or spin. Every frame with an owner follows its owner.
- What a rails component holds: an exact description as a function of time. No integration state.
- How the engine gets the motion: one motion sample per tick, for the end of the next step, in one intent, MoveOnRails. The engine never calls into a component at the sync point.
- What a sample holds: the pose, the chord velocity, the instant velocity, the acceleration, the angular velocity and the angular acceleration, in doubles, and (Q4) the proper acceleration of the frame.
- How exact a body on rails is: physics snaps it exactly onto its target after the Jolt update, and its frame stands exactly on the sample.
- A frame without a body takes the same intent on its entity's transform.
- A static frame needs no component. Gameplay moves its owner.
- MotionDriver and BodyDesc.FrameHullMotion go. The motion of the owner's body says how a frame follows.
- A change of motion is a swap of components plus a change of the body's motion.
- The rails math is a public library of static methods in Tertius.Frames.Rails.
- DriveKinematicHulls and step 6b (AdvanceDrivenFrames) go.
- This epic comes before spin gravity (ter-s9m0).
- A fast or far body on rails lives in a following frame, for every kinematic hull.
- The ghost of a frame on rails takes the sample's chord. A hand-off takes the sample's instant velocity.
- The tree keeps the instant velocity for a frame on rails, and the hull's chord for a frame with a dynamic hull.
- A transform write to a bodyless owner on rails moves the frame for one tick.
- A component reads the time of its sample with Self.TimeAt(tick).
- Q1, how rails motion starts: the new frame holds still at its spawn pose for its first step, and the first run of the component puts it on its rails.
- Q2, how a body changes its motion after spawn: the intent self.Body.SetMotion(BodyMotion).
- Q3, where the checks of rails motion live: in the components.
- Q4, the proper acceleration of a frame on rails: a field ProperAcceleration of the sample.
- Q5, where the motion sample lives: a table of its own, indexed by the frame, with three buffers.
- Q6, what a state dump records about a frame: FrameRecord(Id, Parent, Follows).

User decisions (2026-10-17):
- The full version, for consistency. The state of the Kepler, Spin and Static drivers moves from the frame tree onto components of the frame's entity. Every frame with an owner follows its owner, as a frame with the Physics driver follows its hull today. The reverse path goes away: the tree moving a frame on rails, and physics pushing the frame's hull after it (DriveKinematicHulls).
- The math of the rails moves into a library of static methods, in a namespace under the frame tree code. Components and engine readers both call it. Examples: a Kepler orbit's place, velocity and angular velocity at a time, the orbit's chord over a step (NumericFrameRules.OrbitChord), the orbit's acceleration, a spin's turn over a step, and the prediction of a frame at the end of a step (FrameTree.KeplerMotion).
- Changed on 2026-10-18 (User): no static rails class. Every rails function lives in its struct: instance methods of KeplerOrbit and FrameSpin for functions of an orbit or a spin, static methods of KeplerOrbit (Chord, Acceleration, MuFrom) for two-body math without orbit elements.
- The spin gravity epic (ter-s9m0) waits for this epic. This epic comes after the numeric frames epic (ter-uu1x), whose ghost change edits the same orbit frames and anchors.

Points that the design must settle:
1. The motion sample that a rails component hands over each tick: the place at the end of the next step, the chord velocity, the acceleration and the angular velocity. How physics, the ghost pass, hand-offs, the orbit frame and render interpolation read it. The component computes one step ahead, because its tick N output acts in the step of tick N+1.
2. The sync point order. Today step 6b places rails frames before the numeric update in step 6c. With components, the samples come from the gameplay of the tick before.
3. Numeric frames. The orbit frame of a Kepler hull (NumericMode.Orbiting) becomes a numeric frame that follows any fast kinematic anchor. NumericBody.Fixed and NumericBody.Orbits become properties of the body's motion, not of a frame driver.
4. Frames without a body, such as planets and rooms: an entity with a component, and the cost of one component call per frame per tick, against the serial loop of the tree today.
5. Driver changes (World.SetFrameDriver, FrameTree.MoveOnOrbit, LeaveOrbit, DriverChangeTests), and what replaces the MotionDriver enum.
6. Determinism, state dumps and saved state.


## Notes

**2026-10-18T01:55:10Z**

HANDOFF for the next session (2026-10-18). Start here and from tk ready.

State of main (after e9ffc94): the numeric frames epic ter-uu1x is closed.
- ter-3xs6: free groups fall from 1,000 m/s (leave 400), groups with a body that thrusts from 50 (leave 20), resting frames from 2^23 m (leave 2^22). Every character thrusts; any other dynamic body after 0.5 s of thrust ticks within 20 s. Tests use the real defaults (user decision): Core tests mark fast hand-built bodies as characters, World tests use tests/Tertius.Testing/Components/Thruster.cs with a 30-tick warm-up, bodies above 1,000 m/s, or scenes past 8,389 km.
- ter-4psc: ghost places are inputs of the merge test (docs/plans/numeric-frames.md 'Ghosts as infinitely heavy'). Heir sends every frame of a dying frame to its semantic parent. The numeric update always hands physics its last batch, so physics reconciles at every sync point.
- This epic's design is final: docs/plans/rails-components.md. Decisions of 2026-10-17 (User): the full version (state on components, every frame follows its owner), rails math as static methods in Tertius.Frames.Rails, a motion sample per tick through one intent, the snap to the target, a new frame off its rails for one tick, SetMotion as an intent, components check their own rules, ProperAcceleration in the sample. Internal: samples in a table of their own with three buffers, dumps record FrameRecord(Id, Parent, Follows).

Order: ter-k8cg (library, no behavior change), ter-6r8c (components and samples), ter-ru5u (SetMotion, MotionDriver goes), ter-zg77 (following frames for any fast kinematic hull). Then the spin gravity epic ter-s9m0 (ter-1bnq, ter-vja8, ter-1b21, ter-n44b).

User rules from 2026-10-17 (also in memory): start with the pure, orthogonal model and add exceptions only when it misbehaves; no stopgaps for planned work; workflows of about 4 or 5 agents grouped by shared context (the cost of a long agent grows with the square of its turns, the duplication only linearly).

Process that worked: Sonnet readers map the code; an Opus builder in the main checkout lands the core change and a failing list; Opus fallout agents in worktrees by file group; the orchestrator merges, runs every suite in Debug and Release, runs coverage on changed lines (tools/README.md), and one Opus reviewer reads the whole diff; fix agents confirm each finding with a failing test. Run the full Release suite too: one Release-only failure appeared only there. Never cd into a worktree from the session shell (use git -C and absolute paths).

Open follow-ups: ter-91p8 (Jolt's linear cast holds a resting body back beside a ghost in a fast home), ter-me1y (item 1 partly, item 3). About 41 old worktrees from earlier sessions sit in .claude/worktrees; they are not this session's.

**2026-10-18T14:14:58Z**

When this epic closes: start the spin gravity epic ter-s9m0 and the demo steps ter-2n31, ter-na4f and ter-m13s (epic ter-rb26) as two parallel workflows (user, 2026-10-18). See the note on ter-rb26.
