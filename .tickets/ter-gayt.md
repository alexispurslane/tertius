---
id: ter-gayt
status: open
deps: [ter-nmbm, ter-toaw, ter-8hbi, ter-jz2y, ter-sftw, ter-3ug8, ter-jxjx]
links: [ter-dzxf]
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# The screen tracer: reflections and many-bounce light through one trace

Build ticket 4 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 6 and 12, and decisions 4 to 6, 9, 17, 25 to 27, 36, 38 and 39. Acceptance: section 14, item 4. It starts from ticket 9's trace. If the whole tracer exceeds the budget's 1.7 ms at 1280 x 720, it reports to the user before it goes on. The user sees reflections on every surface up to roughness 0.5 and bounce light with many bounces.


## Notes

**2026-11-04T12:10:08Z**

2026-11-04, the trace budget facts at the windowed clock (1296 MHz), from a read-only researcher (GPT 6.1 SOL), file /private/tmp/trace-budget-facts.md. At 1920 x 1080 output with 1280 x 720 rendering, the frame without the screen tracer is an estimate of 9.34 to 10.02 ms: section 11's rows, with the direct light's shadow-aware combine (3.0 to 3.1 ms with its candidates) and FSR at 1.93 to 1.99 ms. Inside 15.0 ms the whole tracer gets 4.98 to 5.66 ms. After 0.6 ms for the plan and the resolve and 0.015 ms for the finest depth level, the two trace dispatches get 4.36 to 5.05 ms. V6's two dispatches at 10 % own rays take an estimated 3.14 to 3.16 ms on the deck and 2.92 to 3.02 ms on the floor (frame 12.9 to 13.8 ms). At 25 % own rays they take 4.07 to 4.15 and 3.77 to 3.80 ms (frame 13.7 to 14.8 ms, worst margin 0.21 ms). So the old 1.1 ms covers about 35 % of the rays, but the frame holds all of them at 10 % own rays. Every number converts headless times at an assumed 800 MHz, within about 15 % either way. At native 1080p rendering, the frame has no room for the trace before FSR (-1.68 to -0.46 ms). The orchestrator's earlier figure of 2.7 ms of room subtracted today's frame (12.3 ms), which still holds the old occlusion and reflection passes that the tracer replaces, and was too low. The budget decision goes to the user together with the diffuse walk choice, after the profiling continuation (8e00fb0c).

**2026-11-04T17:30:08Z**

2026-11-04, the trace budget under 12.0 ms after ter-9nrc, from a read-only researcher (GPT 6.1 SOL), file /private/tmp/trace-budget-12ms.md. It replaces the note above that gave 1.36 to 2.05 ms. Deck, 1920 x 1080 output from 1280 x 720 rendering, at the windowed clock: the measured frame after ter-9nrc (7.03 to 7.32 ms) minus the old occlusion, reflection and SMAA groups, plus FSR (1.93 to 1.99), the output-size passes (0.40), the plan and resolve (0.6, allowance) and the finest level (0.025), gives 7.84 to 8.13 ms before the dispatches. With section 11's reserves for unbuilt work (indirect accumulation, corrected environment reads, the opaque copy: 0.40 to 0.93 ms) it is 8.24 to 9.06 ms. So the two dispatches get 2.94 to 3.76 ms (complete design), or 3.87 to 4.16 ms (replacement only). With the clock rule's 15 % error on every converted term, the room spans 1.74 to 4.87 ms. At the top clock, V6 at 10 % own rays takes 3.93 (cap 64), 3.90 (62) and 3.78 (54) ms on the deck and 3.50, 3.47 and 3.35 ms on the floor. 25 % own rays (5.2 to 5.4 ms) fits in no setting. The 9nrc runs' clocks are whole-run active means, not timed-span clocks, so these are estimates. The old 1.1 ms figure is not an input. Decision to the user: rays at the first build and the diffuse cap.

**2026-11-04T18:20:05Z**

2026-11-04, the user's decision on the budget: full rays at the first build (every 2x2 block's ray plus 10 % own rays per kind), no ray limit now; the build measures the real frame at the top clock and reports it, and a ray limit comes only if the measured frame stays over 12 ms after the hill-climb (ter-dzxf) lands. Diffuse cap 62 steps (at least V1's image in both views at V1's diffuse time), reflection cap 256. The stop rule of this ticket's body (1.7 ms for the whole tracer) is replaced by this rule. The design update ter-jxjx runs first.

**2026-11-04T18:51:06Z**

2026-11-04, the user's answers to the plan's open forks of ticket 4 (docs/plans/temporal-renderer.md, 'Open forks of ticket 4'): fork 1, design A, a busy headless film (a timed film keeps two frames in flight; the span's recorded clock shows whether the GPU reached the top clock). Fork 2, design A, a C# copy of the two-depth walk in ScreenRay, held against a march over the flat slabs on 1,000,000 random rays, with GPU tests pinning the shader (and any later Mac-only shader) to it. Frames after a reset (known limit 21): no bound at the first build; part 7 measures them and reports, and a limit comes with the ray-limit ticket if needed. The build records these answers in the plan's forks section.

**2026-11-04T22:16:37Z**

Part 7, the measured frame (2026-11-04). The deck's frame is 19.2 ms against the 12.0 ms of section 11, "The frame before ticket 5". The reflection rays take 8.3 ms of it, because 59 % of the deck's pixels trace their own reflection ray in every converged frame. No figure stops the ticket (decision 43).

How. Release, 1280 × 720, NoUpscaling, `--in-flight 2` (part 4). 7 interleaved rounds of the branch and a byte copy of its binary as the control, in a rotated order, each run under `tools/gpu-lock.sh` and beside `tools/gpu-clock.sh`, in the same way as `tools/demo-timing.sh`. Views: the deck (`gpu-trace.py trim scripts/demo-exit.json 399 120`), the floor (frame 2552, 120 frames), and the camera cuts (`scripts/demo-exit-deck-cuts.json`). All 42 runs ran at 1295.7 to 1296.0 MHz over their timed spans, so no pair was left out. The 1-minute load at the starts was 4.5 to 15.6. A pass's own time comes from one trace of the deck's 120 frames (`tools/gpu-trace.sh --no-counters`, at 1296.0 MHz, load 9.2), because on MoltenVK a pass's timestamp time can hold its neighbors' work. The plan's recipe asks for three interleaved traces with a base; this ticket has no base with the tracer, so one trace of the branch stands in.

Frame spans, the median over the rounds of each run's median, in ms:

| View | Branch | Control | Frames per run |
|---|---:|---:|---:|
| Deck | 17.150 | 17.154 | 120 |
| Floor | 13.583 | 13.557 | 120 |
| Cuts | 20.503 (mean 21.918, p95 30.606) | 20.540 (mean 21.976, p95 30.665) | 136 |

The frame against 12.0 ms, as section 11 builds it: the measured span, less bloom to capture (0.326 ms on the deck, 0.323 ms on the floor), plus FSR's 1.93 to 1.99 ms and the output passes' 0.40 ms.

| View | Frame | Against 12.0 ms |
|---|---:|---:|
| Deck | 19.15 to 19.21 ms | 7.2 ms over |
| Floor | 15.59 to 15.65 ms | 3.6 ms over |

Each pass of the tracer and of the accumulations, in ms. The timestamp mean is the median over the 7 rounds of the branch. The own time is the trace's mean (152 frames: the first still's 32 warm-up draws and the 120 film frames).

| Pass | Deck, timestamps | Deck, own time | Floor, timestamps | Cuts, timestamps | Section 11 |
|---|---:|---:|---:|---:|---|
| depth pyramid, with its finest level | 0.743 | 0.273 | 0.907 | 0.797 | |
| ray plan | 0.362 | 0.324 | 0.384 | 0.792 | 0.6 for the plan and the resolve |
| reflection rays | 8.296 | 8.274 | 4.846 | 10.440 | 2.94 to 3.76 for the two trace passes |
| diffuse rays | 0.928 | 1.025 | 1.038 | 1.902 | (with the reflection rays) |
| screen trace resolve | 0.280 | 0.240 | 0.279 | 0.396 | (with the plan) |
| accumulate indirect diffuse | 0.576 | 0.505 | 0.625 | 0.745 | 0.20 to 0.40 for the accumulations |
| accumulate indirect specular | 1.359 | 1.158 | 1.456 | 0.813 | (with the diffuse one) |

Against section 11's rows on the deck, by own time: the two trace passes take 9.30 ms against 2.94 to 3.76 ms. The plan and the resolve take 0.56 ms against 0.6 ms. The accumulations take 1.66 ms against 0.20 to 0.40 ms. The control's distance from the branch is at most 0.03 ms in the deck's and floor's spans and 0.14 ms in a tracer pass's timestamp mean, except the cuts' diffuse rays, 0.16 ms.

The own-ray share of each kind, as a share of the 921,600 pixels, from part 5's hook `ReadOwnRayCounts`. A throwaway tool in /private/tmp read the counts in each draw of the same scripts (not a timed run):

| View | Reflection | Diffuse |
|---|---|---|
| Deck, 119 film frames | 59.1 % (59.0 to 59.3) | 1.26 % (1.22 to 1.29) |
| Floor, 119 film frames | 36.8 % (30.5 to 62.8) | 1.23 % (0.36 to 2.84) |
| Deck, frames 1 to 8 after each cut | 70.5 % in each frame | 70.5 % in each frame |
| Cabin, the frame of each cut away | 75.0 % | 75.0 % |

The deck's reflection share stays at 59 % in the still's warm-up draws too, where the camera does not move. On a flat test floor of roughness 0.38, the reflection share falls from 81,220 to 2,133 pixels in 12 frames, so the plan's rule works on its own. Section 6, "The ray budget", names two causes that keep a pixel at its own reflection ray in every frame: a roughness below about 0.19, whose specular cap of 4 stays below FullRateFrames, and an anti-lag that cuts the history below 8 frames. This report did not split the deck's share between them. The plan's estimate assumed 10 % own rays.

The frames after a camera cut. Each of the 15 cut blocks of `scripts/demo-exit-deck-cuts.json` is one frame of the cabin camera and then 8 frames of the deck camera, so the cuts' figures are means over 135 such frames and the first still. Every lit pixel but each block's chosen pixel traces own rays of both kinds in all 8 frames, as section 6 says. The mean frame is 21.9 ms, 4.7 ms above the converged deck, and the 95th percentile 30.6 ms. The reflection rays take 10.4 ms and the diffuse rays 1.9 ms.

What follows by decision 43: a ray limit becomes a ticket of its own if the frame stays above 12.0 ms after the hill-climb (`ter-dzxf`) lands. The reflection rays' share is the largest lever: their own time is 8.3 ms at 59 % own rays on the deck, and 4.8 ms on the floor at 37 %.

**2026-11-04T22:56:41Z**

Part 8, the docs, the screenshots and the last checks.

- ARCHITECTURE.md describes the code: the pass list, the GPU timings, the G-buffer bindings, hair, the map table, the haze, the still rule, the options table (`ScreenTrace`, `WarmUpFrames`, `CapturesInFlight`), the Host options (`--screen-trace`, `--in-flight`), the required device features (15 storage images per compute stage, R8_UINT and RG32F) and the Portable Vulkan rows of the walk, the density code, the resolve and the lists. The rows of the deleted passes are gone. Dated measurements keep their old pass names.
- The plan records both forks as settled on 2026-11-04, design A of each, the shader split of part 5, part 7's figures in section 11, the own records in the memory table, and the known limits 22 to 25.
- Two Portable Vulkan fixes, found while writing the rows. The resolve now caps the diffuse sample at MaxRadiance after its weights, which reach 4: a light at 32,256 times 4 passed the largest half float. A record now holds a hit distance of at most 65,504 m, because a planet's hit passed it. The density code gives a density below 2^-100 its own code, which decodes to exactly 0, because the spec leaves log2 of 0 undefined, and a hair pixel's diffuse ray can have a density of 0 where hairResponse is not 0. Metal and KosmicKrisp both saturate a half float store at 65,504, so `AHitFartherThanTheLargestHalfFloatStoresTheLargestHalfFloatAsItsDistance` passed without the cap too: only another driver can fail it. No test can fail on the density code here, because MoltenVK gives log2 of 0 as minus infinity.
- New tickets: `ter-76e7` (the C# copies of the removed estimate), `ter-dbzx` (the accumulator's isnan test) and `ter-12xp` (single bright pixels on the deck's walls).
- Screenshots of the deck, the cabin and the 64-lamp hall, headless at 1280 x 720 under the defaults. Against the same deck under `--screen-trace off`, the tracer's still shows a fine grain over the walls, which the plan's accumulator model expects of 32 draws, and single white pixels, which `ter-12xp` takes.
- Checked: 9,848 passed, 21 skipped, 0 failed. Release: 9,835 passed, 21 skipped, 0 failed.
