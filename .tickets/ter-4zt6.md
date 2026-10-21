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

**2026-10-21T14:39:35Z**

The user asked on 2026-10-21 that the particle system be efficient and minimal. Puffs are not entities: no handle, no generation, no components, no messages, no render row, no body, no spawn description. 'A loose body without a shape' describes how a puff moves, not how it is stored. Puffs are rows of one compact store that the simulation owns, grouped by frame, born only by emitters at the sync point into a pool allocated up to the cap, and stepped in bulk, one loop per frame over a contiguous span, with that frame's turn and field of the step. A likely minimal row: the two-turn update keeps x_n and x_(n-1), which are also the current and previous positions that the renderer interpolates, so a puff may need two positions, an age and a look index, and no velocity. The design doc settles the layout and measures it.

**2026-10-21T15:17:40Z**

The user's choices on the draft 3da92c5 (docs/plans/particles.md on ter-4zt6-particles), 2026-10-21: (1) pure gravity: puffs feel the full gravity of their frame, with no lift number, so smoke on the pad sinks and fades at the ground; (2) the frame holds the air: each frame holds a medium beside its gravity field (for example none, uniform, or air that thins with height), and the engine gets that store and model, not a drag number on each emitter; (3) lit puffs are lit once per puff, at the center. Also from the user: puffs must be efficient and minimal. The draft's 104-byte Puff copies its emitter's settings and its frame handle into each row: one design agent revises it to puffs grouped by frame with no per-puff copies of what their emitter shares, and splits the order of the work into parts that touch separate files, with the interfaces stated, for parallel builders.

**2026-10-21T15:18:25Z**

Correction, 2026-10-21: the user's 'pure gravity' means no lift number on the emitter. Lift comes from the frame's medium, by Archimedes: at a puff's place the medium gives the drag toward its rest and a buoyancy from its density against the puff's own gas, so the net acceleration from gravity and buoyancy is g (1 - rho_medium(x) / rho_puff). In vacuum a puff feels the full gravity. In air, hot exhaust rises and cold gas heavier than air sinks. The look holds the puff's gas density. The user's words: 'I was hoping that the frame's knowledge about the medium could then impart lift to the puffs based on that, since the puffs are already grouped by frame.'

**2026-10-21T15:58:07Z**

The revised design is docs/plans/particles.md at 1951a75 (branch ter-4zt6-particles): a 56-byte Puff (place, chord, age, look index), ranges per frame, a shared look table, the medium as a [Union] beside the gravity field (UniformMedium, PlanetMedium), lift and drag from it, old puffs stepped at a new step 2b, and nine tasks of which 3, 4 and 5 run in parallel. The user settled its two forks on 2026-10-21: the medium acts only on puffs for now, and a look holds a temperature ratio (the ideal gas law), so lift and drag are the same at every height and stop at once at 50 km, where earth-surface's air ends.
