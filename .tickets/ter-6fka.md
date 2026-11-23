---
id: ter-6fka
status: open
deps: [ter-8pco, ter-9t54, ter-yglw, t2b-xeo7, ter-pdw8]
links: [ter-4iic, t2b-d2m6, t1-mybi, ter-13rm, ter-8pco, t0-gumm]
created: 2026-11-22T21:13:03Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# Off-screen bounce light that the eye's position does not change: what a diffuse miss reads and what a young history starts from

The structural step that the lighting system review recommends (docs/research/lighting-system-review-2026-11-22/README.md, sections 10 to 13). Today a diffuse ray that misses the screen reads the eye's environment map (missLightOf), and a young history starts from a closed form and buys its own rays. Both then depend on where the eye is. This ticket replaces their source with an estimate that the eye's position does not change, kept per semantic frame, written by rasterized views: capture points per semantic frame (design P) or senders from the lights' shadow views (design S, the user's proposal of 2026-11-22).

## Design

One model (review, section 11): one store of first-bounce and far light, written by rasterized views whose positions do not depend on the eye, read by a diffuse miss along a direction through one reader, missLightOf. One supplier per piece of light, split by the light's origin. The screen rays stay the near field, and the eye's map stays for reflections. Settled 2026-11-22: (1) No store that only the screen feeds: the light would depend on where the camera has been. This replaces the premise of ter-13rm. (2) No build before the three experiments and the film of t2b-xeo7. (3) The orchestrator chooses between P and S from their results (user, 2026-11-22: 'use your best judgement and try to hill climb toward a complete/good lighting system'). The orchestrator's prior is P, because it tests visibility by its stored distance, carries many bounces and lets FullRateFrames and the own rays of young histories go, while S leaks through walls behind the camera. (4) No new trust rule of the temporal stack while the ray census is open. Design X's merge and its restart at 8 draws are not in question.

## Acceptance Criteria

Decided after the experiments. At least: the film of t2b-xeo7 shows no visible change of the room's light when the eye moves around the block, no new leak through a wall in any survey scene, and no cost peak at a start or after a light change that moves the render scale.


## Notes

**2026-11-22T21:20:12Z**

2026-11-22, orchestrator: the review's claim that tuning cannot reach the bar for the ray noise is refuted for the scene that it cites, and holds elsewhere. Combined check's table (t3/combined-renderer-check, docs/research/t0-gbgj-combined-check/tables.txt, draw 132 at render scale 2/3, daub against the long reference with both clamps off): rect-lamps main 0.0119, Design X 0.0089, no clamp 0.0079 (ceiling 0.0114 / 0.0090 / 0.0083, against 0.0567 on 2026-11-14). deck-cuts 0.0076 / 0.0053 / 0.0034. exit-cabin 0.0289 / 0.0173 / 0.0131. docking-cabin 0.0263 / 0.0169 / 0.0137. bay-sunbeam 0.0315 / 0.0229 / 0.0149. cabin-light-cabin 0.0428 / 0.0348 / 0.0285. Reading: two scenes are at the bar of about 0.010. In four scenes the still with no trust rule at all stays 1.3 to 2.9 times above it, so that part is ray noise and no rule removes it. Design X closes most of the gap between main and that floor. The fall of rect-lamps from 0.0567 came before the back-face layer (the Design X line's guess: the room's shadow fix), so the review's cause for it is not supported.

**2026-11-22T21:31:35Z**

2026-11-22, orchestrator: experiment E2 (ter-9t54) is done. The senders' light is right to within 14 to 23 % in the lit-block room. The leak through a wall is 4.8 % and 12.6 % of the lit room's comparable light, above the review's limit. So design S as sketched fails its own condition, and a design that takes the lamps' views as writers needs a visibility test that works behind the camera. Capture points have one through their stored distance.

**2026-11-22T22:09:05Z**

2026-11-22, orchestrator: the film of t2b-xeo7 is on main (docs/research/back-face-depth-layer/stage-2/lit-back-film/). A floor point behind the block that no lamp reaches reads 0.078 at rest, 0.507 while the eye sees the lit face (6.5 times), and 0.081 after the walk back, dark again within about 40 draws. A turn of the camera with the eye at rest moves it by at most 8 %. So the eye-bound class is a visible change over time by the user's bar, not accepted lost light. The review's first condition for the step holds. Open: the ray census (ter-8pco) and the face cost (ter-yglw).

**2026-11-23T02:55:55Z**

2026-11-22, orchestrator. Three facts that shape this step's design, each with its record.

1. The ray census (ter-8pco, docs/research/ray-census/README.md): far hits (2 m or more) and misses carry 76 to 100 % of the diffuse samples' variance in every region of five stills. The review's condition for 'no far-field store helps' (under about 10 %) fails everywhere. No cap bound a sample. The accumulator keeps up to 12 % less light than it receives.

2. The class behind three speck faults (ter-fe63, the lamp hall, the jamb leak's opening): a ray's end reads one pixel of one draw. hitLightOf is a plain imageLoad of the hit light, and leavingLightOf fills it from this draw's jittered direct light plus the last draw's history. A sunlit sill thinner than a pixel enters at 162 in 1 draw of 25 against a ceiling of 0.22. The owner is what the rays read, not which rays are traced and not a trust rule on the samples. The experiment ter-pdw8 measures a time-resolved read and a prefiltered read against today's.

3. Who reads an own ray's sample (the Design X line's code read, t0-gumm, line commit 127633a4d): every pixel takes exactly one sample per draw, its own ray's or its block's, with the same weight 1 / length. An own ray gives its pixel no extra sample. It makes the 4 pixels of a block independent, and only the wavelet filter's pool (up to 63 pixels along each axis) sees that. So a ray reaches the picture only through the pool: a young history on an open surface is served by the filter, and a ray buys most where the pool is small (narrow surfaces, corners, jambs) or where the block's ray does not fit. A ray budget per draw must spend by pool size and fit, not by history length. The fair test in bounce-lit scenes is ordered at the Design X line and is not yet measured.

Consequence for the design, my reading until ter-pdw8 and the fair test report: the first change of this step is what a ray's end reads (hits and misses alike read a filtered light). The capture points and the senders then serve the misses and the young histories on top of that, and are chosen after the record of ter-pdw8.

**2026-11-23T04:26:36Z**

2026-11-22, orchestrator. Two more facts for the ray budget, from the Design X line's fair test and gate take (t0-gumm, docs/research/t0-gumm-catch-up-spread on the line).

1. In the bounce-lit bay at a start, today's catch-up rays (one own ray per young pixel per draw for 8 draws) give 24 % less boil in draws 1 to 8 and less than half the grain in draws 3 to 8 than no catch-up rays, and the gain lasts past draw 8. One ray per restarted pixel spread over 32 draws equals none. So an early ray buys more than a late one, and removing the young-history ray rule is refuted. This corrects my reading in the note above for bounce-lit scenes: the filter's pool does not serve a young history there.

2. After the sun comes on in the bay, catch-up rays buy 20 % less grain for about 12 draws. After the sun goes off they add boil. The line's observation: a ray buys where the samples' variance rises. A budget that spends by variance would cover the start, the cut, a light coming on and the narrow surfaces with one rule. Not a design yet.

Also from the card's logs: after a light change or a cut, the render scale stays under its rest for about 50 draws with today's rule, of which about 8 are the dear draws and the rest is the controller's return at 5 % per draw. So most of the rule's price on the card is the controller's (t1-zeyk, ter-5kz6).

**2026-11-23T04:47:56Z**

2026-11-22, orchestrator. The first results of ter-pdw8 (the back-face line's record follows in docs/research; evidence under the line's evidence worktree, rayend/).

1. A pixel is not a surface. The lamp hall's row 314 shows a sunlit sill's face in 1 jitter phase of 25 and the wall in the others. Today a ray reads the one draw's surface at its full value (162 against a ceiling of 0.22) and the picture gets specks. A mean per pixel over time reads both surfaces at once and puts the sill's light on the wall's pixel, where the floor's rays read it: a measured leak (all 92 bright samples of the floor at the wall's foot hit row 314). So any resolve per pixel leaks, the upscaler's output included. A read that is right is keyed by the surface, not by the pixel.

2. Closed by measurement: the time-resolved read per pixel (the leak above), and the prefiltered read by the ray's footprint (far diffuse hits read cells 32 to 256 px wide; in bay-sunbeam it loses 29 to 55 % of the bounce light and shows a halo).

3. The general mottle needs a far-field store or better sampling, and a resolved read is a remedy of the tail only: in bay-sunbeam the time-resolved read lowers the far hits' variance by a factor of 0.98 to 0.99, which is nothing. The variance is in which far pixel a ray reaches.

4. The guide and the hit must read the same light: 0.906 of the speck samples are guided rays, and a filtered guide with today's hit read makes the ceiling 3.6 times worse.

5. The clamp's loss stays: the accumulator keeps 0.91 to 0.94 of the received light in bay-sunbeam in every variant, so ter-18hc does not close with this experiment.

Next in ter-pdw8: the surface share. The direct part of the hit light is scaled by the share of recent draws in which the pixel showed this draw's surface, in two forms: with a new small state per pixel, and from the history's length that the pixel already holds (no new state). The lost bounce of surfaces thinner than a pixel is accepted in advance.

**2026-11-23T05:43:52Z**

2026-11-23, orchestrator. Two results of ter-pdw8 on the lamp hall (probe build on 3549d3283, before Design X).

1. The surface share passes on the first still. Form S2': where leavingLightOf writes the hit light, the direct part is scaled by 1 where the reprojection's surface test holds, else by 1 / min(jitter phases, draws since the reset + 1). No new state. At 1072 x 603 (the size that bursts): specks per capture 0 (today 83), largest diffuse sample 9.9 (today 127), level within 0.6 % of today in five regions, no flecks at the wall's foot. In the still 0.012 % of the surface pixels have a share below 1, on jambs and sills only. The form with a new count per pixel failed, and the form that took the share from the history's length ran 1.1 to 1.3 % dark, because other rules shorten that length. Still to run: bay-sunbeam, two cabins and a walk.

2. The anti-lag after the clamp shortens the diffuse history at 8.1 % of a still's pixels (16 % of the lamp hall's middle floor). With that one line off for the diffuse signal, 0.027 % remain. So the clamp's move has two readers that both treat a rare bright sample as a change of the lighting: the clamp loses its light (ter-18hc), and the anti-lag cuts the length. Bug ticket t1-g1jd. Its first step is the same count on main after Design X, by writer, beside the region test's false fires.

**2026-11-23T06:25:27Z**

2026-11-23, orchestrator: the ray end experiment (ter-pdw8) is closed, and I accept the surface share S2' for an engine build. It is the first change of this step and the remedy of the speck class 'a ray's end reads one pixel of one draw' (ter-fe63). I opened three of the record's pictures at 1:1 (lamp hall ceiling at 1072 x 603, the share map of walk draw 228, exit-cabin's doorway) and read sections 1, 8 and 9 of docs/research/ray-end-reads/README.md: no specks under S2', red only on jambs, no fringe at the doorway, every region within 0.6 % of V0. Model: the light that this draw computed for a pixel's surface counts in full where the reprojection's surface test holds a history at that pixel, and with 1 / min(P, draws since the reset + 1) where it does not (P = the jitter's phase count at the current render size). No new image and no new state: the share is derived where leavingLightOf writes the hit light, so the hit and the guide read one value. My decisions inside the build, each reversible: (1) the share scales the whole part that this draw computed, the direct light and the glow, because a thin emitter is in the same class. If a measure speaks against the glow, the lead reports it. (2) P comes from the jitter's owner as a frame value. The probe's 8 * (1920 / width)^2 in the shader is a second copy of that rule and does not go into the engine. (3) Accepted limit: a lit surface thinner than a pixel sends about 1 / P of its true bounce, and a newly seen surface loses about one draw of its direct bounce. That is lost light, not a change over time. (4) Order: the build takes the back-face line's next free slot, after the sun's shadow fix (t1-gout, commit A) merges. The face cost experiment (ter-yglw) does not stop. Acceptance on main after Design X: the specks gone at 1072 x 603, 1040 x 585, 0.96 and under the dynamic render scale, in both window shapes. No leak at 1:1. Region means within 1 %. Side-by-side videos of the lamp hall's ceiling, the walk and the exit-cabin motion film, with frames looked at 1:1. A moving sunlit object keeps its bounce. A cut onto the lamp hall shows no flash of specks. The frame's time equal within the noise after a discarded warm-up run. Then ter-fe63, t2b-d2m6 and the jamb opening's specks get one take each on the merged code.

**2026-11-23T09:35:55Z**

2026-11-23, 04:35 New York time, the Design X line's orchestrator, by the orchestrator's word: the next change of the screen tracer's 2 x 2 block starts from the patch docs/research/t0-rs55-wide-fix/wide-fix.patch (it comes to main with the record of t0-rs55). That patch gives the block one named place, trace_block.glsl (traceBlockOf, traceBlockPixel, TraceBlockPixels), which the ray plan, the resolve and chosenPixelOf read in place of 'pixel / 2' and '2 * block'. It did not merge alone, because the wide read of short histories that it came with was not merged (t0-rs55).

**2026-11-23T09:55:04Z**

2026-11-23, orchestrator: the face cost experiment (ter-yglw, docs/research/environment-face-cost/README.md) is closed. Design P does not fit as the review sketched it, with today's face pipeline per capture point: one face costs 0.52 to 0.72 ms on the Mac, and 216 faces within 60 draws cost 1.27 to 2.60 ms per draw. My answer to the line's question on the bound: the sum per draw decides, and I hold the store's steady refresh to about 0.7 ms per draw on the Mac, which is what the frame pays for its one environment face today. The review's two numbers disagreed, and 1.5 ms per draw is too much against a goal of 12 ms. The experiment's main result is where the cost lies: 98 % of a face's cost does not shrink with its size, and its three sources are parts that a field point does not need. (1) The Earth's sphere of 65,536 triangles in up to three depth ranges: a field point of an interior frame draws only its own frame's objects, and a texel that sees no such object reads the map of the frame's parent along its direction, as each object already takes the map of its nearest frame. (2) The prefilter with its irradiance convolution of about 1.6 million reads: a field point serves diffuse reads only, so it needs one small irradiance result and no roughness chain. (3) 7 to 10 dispatches per face: the faces of one draw go through one pass. So design P is not refuted. Its cost floor is unmeasured. The split of today's face cost (t1-6tn2) measures it, with one variant added by me: the lean field face (the frame's own objects only, sizes 16 and 32, six faces per pass, the light pass kept, no prefilter). I choose the store's form after that number. Design S stays behind P: its leak of 4.8 and 12.6 % needs a visibility test behind the camera, and per-frame field points cannot leak between frames by construction. The same split also names a saving for today's frame.

**2026-11-23T14:02:39Z**

2026-11-23, orchestrator: the lean field face is measured (t1-6tn2, interim, docs/research/environment-face-cost/README.md section 10 on the lead's branch). On the Mac a lean face at 16 costs 0.309 ms by the span (all that a face adds to the draw's GPU time), and an empty face that draws nothing costs 0.28 to 0.31 ms. So the floor is neither the content nor the size: it is the count of renderings that a face begins. Per face the pass begins 3 depth-range renderings and 1 light rendering with 5 barrier flushes, and each is a Metal encoder on MoltenVK. The room adds 0.02 ms to an empty face. On the card every lean variant costs 0.16 to 0.22 ms per draw for 216 faces in 60 draws, so the card does not bind. My decision on the store's form, reversible: capture points stay, and the unit of work becomes the batch, not the face. All faces that one draw refreshes go through one rendering into one image, with a viewport and a scissor per face, and one light pass over that image. The schedule then gives each draw one batch, for example the six faces of one point, so 36 points take 36 draws. The cost per draw is then about one empty rendering's structure plus 0.02 ms of content per face, and that is the number to measure next: a probe of the batch at 6 and at 12 lean faces of 16, on the Mac by the span, against the bound of 0.7 ms per draw. The queued row that skips a face's empty depth ranges still runs first, because it is exact and cheap, but the choice does not wait for it. Plain viewports and scissors in one render pass are core Vulkan, so the form holds on the desktop Linux drivers and lavapipe. The split also names two savings for today's frame, which pays 0.696 ms per draw on the Mac for its one face: the prefilter is 55 % of that face (the irradiance convolution 0.159 ms, the rest 0.230 ms), and it runs for every face, and the face pass begins its depth ranges even when they are empty. Each gets its own ticket from the back-face line when the split's record is merged. The Earth is not a lever on the Mac (0.016 ms).
