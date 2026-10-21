---
id: ter-6u2m
status: closed
deps: [ter-4zt6, ter-ftk4, ter-diu0]
links: []
created: 2026-10-21T14:13:35Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, video]
---
# Demo plumes: every force on the shuttle and the person shows as exhaust

The user said on 2026-10-21 that the shuttle and the person look like they move on rails, and asked for plumes whenever they thrust. Neither is on rails: the shuttle is a dynamic body that forces push from the ignition to the landing, and the person is a physics character. Nothing on screen shows the forces. Each actuator sends its force of the tick to its own plume entity as a message (for example outbox.Send(plume, new Exhaust { Throttle = throttle }) in MainEngine.Handle), because the force is a local of the actuator that no other entity can read, and a component changes only its own entity. The plume entity sets its own particle emitter from it. Every force shows: the main engine; TranslationThrusters in six hull directions (its per-axis 'limited' force); AttitudeAutopilot's torque as pairs of turning jets whose couple gives the torque; Passenger.Steer's float force (at most 105 N) as a small jet on the person's back. No force, no plume: a belt, a floor, the ramp and free fall show none. The main engine's exhaust is hot and glows. The small jets are cold gas, pale puffs lit by the sun and the lamps. In a time-lapse frame the plume shows the largest force of the ticks in that frame. The plumes come from the renderer's particles (the user's choice, 2026-10-21), so this ticket waits for the particle epic.


## Notes

**2026-10-21T14:17:10Z**

The user objected on 2026-10-21 to the design in which each actuator sends its force to a specific plume entity: that hard-codes a visual entity into the code that applies the force. My chat also called MainEngine 'the engine', which read as the game engine's AddForce. The model instead: the exhaust is part of the look of the entity that thrusts, as a lamp's light is part of the lamp's render row. The component that applies the force sets the emitter on its own entity (Self.Render), in the same tick, with no message and no second entity. So the particle epic ter-4zt6 must let an entity's render data hold its emitters, and it decides how one entity holds several (the main engine, the side jets and the turning jets of one hull).

**2026-10-21T14:36:37Z**

Changed on 2026-10-21: no 'largest force of the frame' rule for time-lapse frames. A frame shows the puffs alive at its tick, because only the script knows the film interval. Each jet is its own attached entity (ter-ftk4) with one emitter, which its own component sets from its own throttle.

**2026-10-21T15:12:59Z**

Split on 2026-10-21: moving the jets onto parts is now its own ticket, ter-diu0. This ticket adds the emitters to those parts once the particles land.

**2026-10-21T20:42:26Z**

Merged into main on 2026-10-21. One Opus builder, the orchestrator's look at the frames, and one Sonnet review-and-fix agent at medium effort (the user's choice on 2026-10-21, with 3 percent of the week's limit left): no defect in the production code, 7 new tests for gaps, each with a hand mutation. Coverage of the changed lines: 56 of 58 lines and 22 of 24 branches (the two uncovered lines are guards in Plume.cs). Every suite passes under coverage except allocation tests that the instrumentation breaks; PuffTableTests and ScriptUnionTests pass without coverage in Debug and Release. Measured: the plumes cost 3.9 s of a 150 s master, 0.16 ms a frame, with at most 1,516 live puffs.
