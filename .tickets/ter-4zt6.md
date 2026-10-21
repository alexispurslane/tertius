---
id: ter-4zt6
status: in_progress
deps: []
links: []
created: 2026-10-21T14:13:35Z
type: epic
priority: 2
assignee: Alexis Purslane
tags: [renderer, engine]
---
# Particle effects in the renderer: additive blending and particle emitters

Design doc: docs/plans/particles.md (to write first). The user chose real particles in the renderer on 2026-10-21, over glow shapes built from demo code: puffs that leave an emitter and stay behind in space, so that they draw a trail of the path, drawn with additive blending, so that they only brighten what is behind them. The first user is the demo's exhaust plumes (the shuttle's main engine, its side and turning jets, and the person's float jet). This is an engine change, so it gets the full engine process: a design with two designs at each real fork, the user's choices, red-green tests, review, coverage and an ARCHITECTURE.md update. The survey of 2026-10-21 found: no particle system, no billboard primitive for scene objects, no additive blend (GraphicsPipelineDesc has only AlphaBlend), no per-instance tint, and an open bloom ticket ter-63wr. The main design fork it named: in which frame the particles live, because particles in the emitter's own axes cannot leave a trail, and particles in a frame that does not follow the emitter must survive the render origin's moves and the camera's changes of frame.


## Notes

**2026-10-21T14:17:10Z**

From the user's objection of 2026-10-21 (see ter-6u2m): emitters belong to the render data of the entity whose component causes them, like RenderData.Light, and the component sets them on its own entity. The design must settle how one entity holds several emitters, one per jet of a hull, written by more than one component of that entity.

**2026-10-21T14:27:09Z**

Superseded on 2026-10-21: the user proposed parts (see the new parts ticket), so each jet is its own attached entity with one emitter. An entity's render data holds one emitter, as it holds one light, and the question of several emitters per entity is gone.

**2026-10-21T14:36:37Z**

The design settled with the user on 2026-10-21. A puff is a loose body without a shape: born in its emitter's frame, it lives there for its whole life and moves as a loose body there would (the frame's gravity, its proper acceleration, its rotation terms through the same two-turn update as bodies), with a drag toward rest in its frame (zero in vacuum), and it fades as it ages. The simulation owns the puffs and steps them every tick, in doubles, because the renderer knows no frames, a time-lapse film draws one tick in 4 to 240, and the window skips or repeats snapshots. The snapshot carries each puff with a current and a previous position, like an instance. Settled by the rules: one emitter per entity in its render row, like its light (parts make each jet its own entity); one emitter struct with a shading union (glow or lit); glow for hot exhaust, lit puffs for cold gas (the sun with its shadows, the lamps); camera-facing sprites in one draw, additive, sorted together with the transparent instances so that the cabin glass dims only the puffs behind it; a soft fade near opaque surfaces and near the eye; births spread through the step; a time-lapse frame shows the puffs alive at its tick; true size far away; a cap on live puffs, oldest first, with a switch; a jet's puffs start in the physics step of its force. The user's choices: slow tracer puffs at tens of m/s that hang behind and mark the path (not real exhaust speed); no collisions with surfaces; bloom (ter-63wr) later, after the plumes.
