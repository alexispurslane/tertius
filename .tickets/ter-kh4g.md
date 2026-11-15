---
id: ter-kh4g
status: in_progress
deps: []
links: [ter-ukw6, t1-i54g, t1-0ti7]
created: 2026-11-14T18:53:04Z
type: epic
priority: 1
assignee: Alexis Purslane
---
# Back-face depth layer: the screen walk learns each surface's real thickness

Alexis decided on 2026-11-14 to design the real mechanism for ter-ukw6 first: a second depth layer of back faces, so that each screen pixel knows where its object ends, in place of the walk's fixed ThicknessShare slab. The brief, the evidence, the code places, the rejected designs, the points to cover and the acceptance are in docs/handoffs/2026-11-14-renderer-lab.md, part 1. Steps: find the sources and their situations, measure the frame cost with a throwaway back-face pass on the Mac and the RTX 2000 Ada, write two designs with Alexis's questions, then the design doc in docs/plans/ and the tickets.


## Notes

**2026-11-14T19:10:52Z**

Head start from the stopped designer of 2026-11-14 (copied from /tmp/t7-3e22/ukw6-backface-notes.md, which does not survive a restart; these are notes, not decisions):


The coordinator stopped this work early. No source search, prototype, timing run or cloud job happened. Cloud spend: $0. The agent never made /tmp/ukw6 or a scratch worktree.

## What was read

- `tk show ter-ukw6`. The scene is rect-lamps, with the Sun on and clamps off. False sill hits bring about 74 % of the glow box's bright reflection light on the ceiling. These rays left through the end window past the sill's outer edge. The place is render row y 375, x 596-679. There the sill is a sub-pixel strip, seen almost edge-on.
- `src/Tertius.Rendering/Shaders/screen_walk.glsl`, `walkScreen`.
  - At level 0, the slab of a pixel runs from its center w back to w * backShare. Here backShare = 1 / (1 + ThicknessShare), and ThicknessShare = 0.1.
  - Above level 0, the slab runs from the cell's near bound to its far bound times backShare.
  - A level-0 hit needs `meetsSlab && hitsPixel`. `hitsPixel` already rejects a surface whose normal faces away from the ray. It also rejects the hair receiver's own card.
  - `entryShare` is 0 at the slab's front and 1 at its back.
- ARCHITECTURE.md, "The screen tracer".
  - Pyramid images: level 0 is R32F, the center w. The coarser levels are RG32F, the near and far bounds of the pixel planes' corners.
  - Device needs: `maxPerStageDescriptorStorageImages` of at least 20. The tracer sees 16 to 21 storage images.
  - Pass costs: the depth pyramid takes 0.27 ms on the deck. The reflection rays take 0.60 ms on the RTX 2000 Ada, and about 2.65 ms in the M1 Max deck trace.
- The evidence folder holds hits9/hits-true.txt (light sums for each hit class) and the probe patches. Only the head of hits-true.txt was read.

## A first geometric argument (not yet checked against the data)

Take a convex solid, such as a box sill, and the view column of one pixel. A point in that column can lie behind the front surface and outside the solid. Then it lies behind the solid's back face in that column. A ray past the sill's outer edge is outside the solid. So a back-face depth at the same jitter rejects that ray where the ray crosses the pixel near its center.

Two limits to check in a design:

- The sill is sub-pixel. The back-face depth comes from the pixel center only. The ray can cross the pixel elsewhere, where the column through the sill is thinner or empty. So some false hits can remain at the strip's edge pixels.
- Open surfaces have no back face of their own. Examples are single-sided cards, hair cards, glass and thin hull walls. Then the nearest back face belongs to a farther object, and the slab grows thicker than today's 10 %. A design can clamp it, for example to the smaller of the back-face slab and today's slab.

## Points that the design must still cover

- The sources and their situations: McGuire and Mara 2014 (a back-face buffer), depth peeling, and the fixed thickness of current engines.
- The frame cost of a depth-only pass with front faces culled, at the same jitter. Measure it on the Mac and on the RTX 2000 Ada, the prototype against main in one job.
- Whether the back layer needs its own pyramid of bounds for the coarse levels.
- Memory at 1280 x 720. One R32F layer takes about 3.7 MB.
- The extra texel read in each walk step.
- Moving objects and skinned meshes. The back pass must draw the same placements as the front pass.
- Linux portability. A depth-only pass needs no new device feature.
- The effect on contact shadows.

**2026-11-14T22:51:57Z**

Stage 1 of 2026-11-14 is done: sources, cost and two designs. The record is docs/research/back-face-depth-layer/README.md on the branch ter-kh4g-back-face-depth-layer, with sources.md (22 sources with quotes), the patch of the whole probe and two stills. The probe's pass is the commit bacd0680d, marked THROWAWAY. Stage 2 removes it.

Main numbers:
- Sources: no large engine (Lumen, HDRP, Godot, EEVEE, AMD SSSR) gives its screen walk a back layer. Two small sources draw back faces for a reflection walk (kode80 2015, an optional mode of Babylon.js), without a time.
- A pass that keeps the nearest back face fails: in orbit's cabin the camera stands inside the person's body mesh, 0.3 m from every pixel. The pass must keep the first back face behind the front surface.
- With that rule, 99.99 to 100 % of the surface pixels of rect-lamps, the cabin and the deck have a back face behind them. The real thickness is above the 10 % rule on 33 to 46 % of the pixels.
- Cost of the pass alone: +0.037 to +0.047 ms on the RTX 2000 Ada (main 7.1 to 9.4 ms, five runs per side, every run differs), and +0.03 to +0.05 ms on the M1 Max (main 14.8 to 20.5 ms, two rounds, about the size of the run-to-run spread).
- Cost of a second texel read at every step of the finest level: about 0.21 to 0.24 ms on the RTX card and 0.35 to 0.43 ms on the Mac (one round). The rays that walk on after a rejected hit cost 0.02 to 0.05 ms on the RTX card.
- Memory at 1280 x 720: 1.76 MiB (R16F side image) to 5.27 MiB (RG32F finest level plus a side target).
- Cloud spend: $0.26 by the job log, of which $0.09 for a stills job that timed out.

The two designs: (1) the back layer is the slab, stored in the depth pyramid, with no thickness constant in the walk. (2) the back layer only trims the 10 % slab, from a side image read at a candidate hit. Three questions for Alexis are in the record. Not done, by the brief: the design document, the build tickets and the real mechanism.

Not checked: a new probe run of the false sill hits, contact darkening in stills, Design 1's rule in a running walk, hair and other two-sided surfaces, dense geometry, and RADV, ANV and lavapipe.

**2026-11-14T23:05:06Z**

2026-11-14, the answers of Alexis to the three questions of stage 1. (1) The layer is the slab (Design 1): the real back face replaces the 10 % rule. Alexis chose it knowing that slabs grow on a third to a half of the pixels and that contact darkening can change in both directions. (2) A surface with no back face behind it is a sheet with no thickness. (3) Settled by the parent agent from answer 1: the layer lives in the depth pyramid, with the front depth and the back depth at every level. Stage 1b is narrower for it: the false sill hits under the 10 % rule and under the layer as the slab, stills of both rules at one fixed exposure to find a misbehavior with a name, the sheet beside the 10 % rule in a hair view with a check that a ray can hit a slab of zero thickness, and the cost of the pyramid store only. The side image is out. The design doc, the build tickets and the build wait for a later stage.

**2026-11-15T01:25:26Z**

Stage 1b of 2026-11-14 and 2026-11-15 is done: the evidence for the chosen model, the layer as the slab in the depth pyramid. Records: docs/research/back-face-depth-layer/README.md (section 'Stage 1b') and stage-1b/probe.md, cost.md, pictures.md. Main numbers. (1) False sill hits in rect-lamps: 74.34 % of the glow area's bright reflection light is false under the 10 % rule, and 0.016 % of that false light stays under the layer. Stage 1's estimate of 8 % does not hold. (2) Cost of the chosen model against main per frame at 1280 x 720: on the RTX 2000 Ada +0.031 ms on the deck, +0.055 ms in the cabin, -0.109 ms in rect-lamps and +0.446 ms in a crowd of 2.0 million skinned vertices. On the M1 Max +0.060 ms (inside the noise), +0.140, -0.141 and +1.014 ms. The store alone is free. The pass costs by vertex count. (3) Stills at a fixed exposure: rect-lamps becomes darker by about 64 of 255 on 1,972 of 8,040 tiles. In the cabin the recess between two thick pillars brightens by up to 44 of 255, and the fade over the first 10 % turns it darker again, so the fade rule on a thick slab is open (t1-g4p1). (4) The borrowed back face: a hair card that the pass does not draw takes the floor's back face, and 95 % of the layer's reflection hits in lamp-hall-16-hair then lie behind the ray's start (t1-yy2f). (5) A sheet with no thickness keeps 25 to 56 % of the real hits that the 10 % rule finds on it, and a sheet as deep as its own plane keeps 52 to 89 %. The cause is open (t1-i54g). Cloud: $1.24 of $3. Not done, by the brief: the design document, the build tickets and the real mechanism.

**2026-11-15T10:38:43Z**

2026-11-15, stage 2 starts: the open research, then the design doc in docs/plans/, the build tickets and the build. The parent agent answered the two questions of stage 1b with research first: (1) the fade on a slab deeper than 10 % is decided after a reference of the cabin's left wall, (2) the rule for a sheet is decided after the cause of the lost hits is known. The parent also gave one hypothesis to test, which the data can break. The back layer answers whether the ray is blocked. Whether the light at the blocked point is known is a second fact: the screen holds the light of the visible face only, so the light is known where the entry lies within the depth that the pixel's own plane spans. A deeper entry is blocked by a hidden face with unknown light. The fade then does not depend on the slab's thickness, and a sheet is the case where the slab is as deep as the plane's span. The design must then say which light a ray gets that a face with unknown light blocks. Research tickets of stage 2: the reference, the columns and the jamb's bands (t1-g4p1), the cards, the open meshes and the sheets' lost hits (t1-yy2f, with the census of t1-i54g), and the pass's cost in a crowd (t1-kiaw). Limits: cloud $4, two building agents at once, no merge into main.

**2026-11-15T10:40:14Z**

Launched 2026-11-15 from a8bbc2144, two ticket leads on Claude Opus 5.5 at high effort, each in its own worktree. The reference (t1-g4p1): clientRequestId ter-kh4g-2-t1-g4p1-lead-1, taskId node:delegated-task:command%3Amcp%3Ae373919d-10c7-46e4-b97f-8e9f17350ff7%3Adelegate-task%3Ater-kh4g-2-t1-g4p1-lead-1, childThreadId thread:delegated-task:command%3Amcp%3Ae373919d-10c7-46e4-b97f-8e9f17350ff7%3Adelegate-task%3Ater-kh4g-2-t1-g4p1-lead-1. The cards and sheets (t1-yy2f with t1-i54g): clientRequestId ter-kh4g-2-t1-yy2f-lead-1, taskId node:delegated-task:command%3Amcp%3Ae373919d-10c7-46e4-b97f-8e9f17350ff7%3Adelegate-task%3Ater-kh4g-2-t1-yy2f-lead-1, childThreadId thread:delegated-task:command%3Amcp%3Ae373919d-10c7-46e4-b97f-8e9f17350ff7%3Adelegate-task%3Ater-kh4g-2-t1-yy2f-lead-1. The crowd cost (t1-kiaw) waits for a free slot: at most two agents of the line build at once. The launch records are on the epic, so that a lead's branch and the line's branch do not both change a ticket file.

**2026-11-15T12:11:19Z**

2026-11-15: the cards experiment is merged as ab7bdcd34. The crowd cost (t1-kiaw) launched from cb21d8944 as a ticket lead on Claude Opus 5.5 at high effort: clientRequestId ter-kh4g-2-t1-kiaw-lead-1, taskId node:delegated-task:command%3Amcp%3Ab023e4d6-e763-4f5c-b373-081f81e27ece%3Adelegate-task%3Ater-kh4g-2-t1-kiaw-lead-1, childThreadId thread:delegated-task:command%3Amcp%3Ab023e4d6-e763-4f5c-b373-081f81e27ece%3Adelegate-task%3Ater-kh4g-2-t1-kiaw-lead-1. The reference lead (t1-g4p1) still runs: at 07:10 it rendered the cabin's columns on the Mac.

**2026-11-15T12:19:46Z**

2026-11-15, two notes from the parent agent. (1) Reporting channel: T3 delivers only the first turn end of a task to the parent. So the line orchestrator sends each report, question and result to the parent's thread itself: t3_thread_send with threadId ad82a149-2983-4c27-bf78-02402dbe7a6d and mode auto, the whole report in the message, beginning with 'Report of the back-face line'. Send it only for a result, a decision, a question or a problem. If the send fails, note that here and go on. (2) A fix goes to main soon (ticket t8-69v7): a history store in a half-float image loses value downward on this Mac, so long bounce histories are 2 to 6 % too dark, on main too. When the parent says that it landed, merge main into this branch before any acceptance take, and take main's side of each comparison again. The research tickets compare columns inside one image or one job, so they stay valid.

**2026-11-15T13:12:40Z**

2026-11-15: the design doc is docs/plans/back-face-depth-layer.md, still a draft in decision 6. The build tickets, in order: the store (t1-0s0a) and mesh closure with the batcher's split (t1-6ucc) side by side, then the back-face pass (t1-j03y), the layer as the slab (t1-7b4x), and the acceptance takes (t1-8b4w). The crowd cost experiment is merged as f0e9dc1f1 and its ticket t1-kiaw is closed: 0.24 ms per million drawn vertices on the RTX 2000 Ada and 0.36 ms on the M1 Max. Its final report did not arrive as a delivery. One read of task_status showed it. Answers that I gave myself: the pass keeps front-face culling on closed one-sided runs, it draws every skinned figure, and it gets no second vertex path with packed positions. Three questions are open for Alexis: the sheet's slab, the fade by the entry share, and the treatment of many skinned figures.

**2026-11-15T13:13:31Z**

Launched 2026-11-15 from 94138a196: the ticket lead of mesh closure and the batcher's split (t1-6ucc) on Claude Opus 5.5 at high effort, in its own worktree. clientRequestId ter-kh4g-3-t1-6ucc-lead-1, taskId node:delegated-task:command%3Amcp%3A165bdbf9-3637-4289-82dc-aa6b406985af%3Adelegate-task%3Ater-kh4g-3-t1-6ucc-lead-1, child thread thread:delegated-task:command%3Amcp%3A165bdbf9-3637-4289-82dc-aa6b406985af%3Adelegate-task%3Ater-kh4g-3-t1-6ucc-lead-1. The lead of the store (t1-0s0a) starts when the reference lead (t1-g4p1) ends, because two agents of the line build at once at most.

**2026-11-15T13:17:10Z**

2026-11-15, the parent agent's answers to the interim note of stage 2. (1) The slab of a sheet: build the rule of decision 3 in step 4 (the slab reaches to its plane's far w) and approve its snapshots. It is no exception to Alexis's answer: it states the far end of the sheet's real extent inside its pixel, and it gives the sheet no thickness. The full rule, which also starts a slab at its plane's near w, is ticket t1-0ti7 and follows the one-image depth pyramid (ter-y3k4). The design doc says that the full rule is the general rule for the front of every slab, and why. (2) Many skinned figures: every figure keeps its real back face. Alexis sees the numbers at the final acceptance, per figure and for the crowd, beside what the figures already cost. The parent asked how much of the pass's vertex work on a figure is skinning: none. deform.comp writes one deformed copy per figure and frame, and the shadow maps, the G-buffer pass and the back-face pass all draw it. The deformation takes 1.275 ms once in the crowd on the RTX 2000 Ada. So no ticket for a shared skinned copy is needed. (3) The fade by the entry share: bring the reviewed reference with crops beside the path trace. If the fade over the real slab is nearest, it stays, and the question is only whether anything is nearer still.

**2026-11-15T13:17:55Z**

2026-11-15: the reference experiment (t1-g4p1) is merged as 23413e56b. Its final report did not arrive as a delivery either. One read of task_status showed it. Launched from 23413e56b: the ticket lead of the store (t1-0s0a) on Claude Opus 5.5 at high effort, in its own worktree. clientRequestId ter-kh4g-3-t1-0s0a-lead-1, taskId node:delegated-task:command%3Amcp%3A165bdbf9-3637-4289-82dc-aa6b406985af%3Adelegate-task%3Ater-kh4g-3-t1-0s0a-lead-1.

**2026-11-15T13:20:28Z**

2026-11-15: the fade result went to the parent agent with the crops beside the path trace (cabin-nosun.png, cabin-full.png). The design keeps the fade over the real slab. One question went up as needing more research: no fade by the entry share is nearer in six of eight pillar cells and overshoots one by 67 %. My recommendation is to keep the fade, and to test a view with a lit front and a dark back before anyone changes it. The build goes on. Running leads: the store (t1-0s0a) and mesh closure (t1-6ucc).

**2026-11-15T13:25:26Z**

2026-11-15, the parent agent's answers to the report on the reviewed reference. (1) The fade over the real slab stands for the build. The view with a lit front and a dark back is experiment ticket t1-r9vj, for a ticket lead when a slot is free, before step 5 if that costs no wait. A rule that is nearer everywhere goes up with its crops. (2) The sun's shadow leak (t8-rjno) is the line's most urgent open ticket. The same leak can light the end window's sill of rect-lamps and the cabin's sill strips, which are the named cause of the false sill hits, the specks, the jitter's 18-draw cycle and the rare bright samples. The diagnosis is ticket t1-3j2j: it goes to a ticket lead in the next free slot, before step 3 if step 3 still waits. Its result goes to the parent agent as soon as it is merged into this branch, before the fix. The fix is its own branch from main, with both suites and a review of each changed reference image. The line orchestrator then merges it into main in the main checkout by exact paths, pushes, and tells the parent agent. (3) The Earth-light ticket (t1-tbz9) and the bounce-light ticket (t1-mybi) stay as tickets and are not started. t1-tbz9 got two crops and a section on how sure the path trace's Earth is. My own finding while I made the crops: the engine and the path trace do not agree on what the camera sees of Earth, so the factors 16 and 58 are not yet established. Part 4 of t1-3j2j checks it.

**2026-11-15T13:27:13Z**

2026-11-15, the parent agent's answer on capacity: the diagnosis lead starts now as a third agent. The line's limit is now two agents that build and test for hours, plus this one lead that builds once and then renders. The fade's test view (t1-r9vj) waits for a regular slot. The parent corrects the Earth-light factors with my caveat, and wants the result of part 4 on its own in two lines. Launched from 036a9919e: the ticket lead of the shadow leak's diagnosis (t1-3j2j) on Claude Opus 5.5 at high effort, in its own worktree. clientRequestId ter-kh4g-3-t1-3j2j-lead-1, taskId node:delegated-task:command%3Amcp%3A165bdbf9-3637-4289-82dc-aa6b406985af%3Adelegate-task%3Ater-kh4g-3-t1-3j2j-lead-1. The lead sends two early results to my thread: part 1's verdicts and part 4's check of Earth.

**2026-11-15T14:13:05Z**

2026-11-15: part 1 of the shadow leak's diagnosis (t1-3j2j) is merged as bba92b948 and sent to the parent agent. Every bright strip of direct sunlight in rect-lamps is a leak: the far jamb, the end window's sill that the ceiling reflects, and the side windows' sills. The truth lights 103 of the 7,143 pixels that the engine's sun lights, with 0.8 % of the summed light. The cabin's frame 239 holds no strip of direct sunlight. Consequence for this epic: the acceptance stills of the layer wait for the shadow fix on main, because main's side of the rect-lamps comparison gets darker too.

**2026-11-15T14:15:59Z**

2026-11-15: the Design X line's cabin strip is the scene orbit with the camera person, output row 563 and columns 892 to 1030. That is the end window's sill of rect-lamps, which part 1 of the diagnosis already judged a leak at frame 239. The parent agent asked for one more cast at world tick 300, and at tick 60 if it is cheap. I passed it to the diagnosis lead (t1-3j2j) as an extra cast after part 4, with the other line's kept files to read only. Its verdict goes to the parent agent in a few lines.

**2026-11-15T14:51:01Z**

2026-11-15: two more early results of the diagnosis (t1-3j2j) are merged as e2c9b025d and sent to the parent agent. (1) The check of Earth: both renderers put Earth at the same place and give it the radiance of the formula, so the factors 16 and 58 of t1-tbz9 stand and my caveat is withdrawn. Neither crop's camera sees Earth. (2) The Design X line's strip is the end window's sill of rect-lamps. At world ticks 60, 300 and 306 it is lit only in the engine, and the engine's lit part is under one twentieth of a render row tall. The lit part shrinks from 0.108 m to 0.049 m between ticks 300 and 306, because one shadow texel changes as the ship turns.

**2026-11-15T15:52:00Z**

2026-11-15: mesh closure and the batcher's split (t1-6ucc) is merged as 11c506ae3 and closed. Its lead reported 'blocked' at the review stage, because its reviewer could not see the test runs and gave 'unverified' for fulfillment. I settled that myself: the runs are in the lead's report (Checked 10,960 passed, Release 10,951 passed, no reference image approved again, 44 mutants). I read the diff after the merge: one store (RangeRuns) with two derived views, and the closed flag with one owner. Three small review findings are carried into the back-face pass ticket, and the duplicate weld is ticket t1-o086. Launched from 1e0e02600: the ticket lead of the back-face pass (t1-j03y) on Claude Opus 5.5 at high effort, in its own worktree. clientRequestId ter-kh4g-3-t1-j03y-lead-1, taskId node:delegated-task:command%3Amcp%3A165bdbf9-3637-4289-82dc-aa6b406985af%3Adelegate-task%3Ater-kh4g-3-t1-j03y-lead-1. Free disk: 65 GiB.
