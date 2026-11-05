---
id: ter-gayt
status: closed
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

**2026-11-04T19:08:48Z**

2026-11-04: started as a workflow, run 5fb9756d-2153-4e58-ae8b-648f521b25a6 (version 869b0b07, Opus high builder, Opus xhigh reviewer), branch ter-gayt-screen-tracer from 86c3c497f. One builder works through the plan's eight parts in order; no measured value stops the work; the hill-climb's change comes after this ticket.

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


## Single fix stage, 2026-11-04

This continuation started from the preserved checkout on `ter-gayt-screen-tracer`, HEAD `ee8070bb17d08c6ee4f8da82fd8bb55ffa3c5d60`, with original review base `86c3c497f`. The checkout path, branch, clean status and HEAD matched before any write. The original eight implementation parts and the completed review were recovered through T3; none was repeated. This stage ran no agent or workflow, did not merge or close a ticket, and did not run coverage or mutation testing. The parent owns integration and hardening. The prior evidence in `/private/tmp/gayt-part7`, `/private/tmp/gayt-part8`, `/private/tmp/gayt-check`, the branch captures and `/private/tmp/tertius-sol-handoff-20261104/preserved.json` is unchanged.

### Finding resolutions

The numbers below follow the completed review's finding order. A result marked open is not acceptance.

| Finding | Resolution and evidence |
|---|---|
| 1. Rough-reflection hit-distance rejection, high | Confirmed in isolation, not changed. `AccumulatorRenderTests.IndependentRoughReflectionDistancesDoNotRestartAnUnchangedLightsHistory` feeds the real accumulator an unchanged camera, surface and light at roughness 0.38. Distances alternate between 2 m and 8 m over 12 frames; all surface-test bits pass (255), but history length remains 1 instead of reaching 8. The failing run is `/private/tmp/gayt-fix/history-red.log`. This verifies the reset mechanism, not its quantitative contribution to the deck's measured 59 % own-reflection share. The regression is explicitly skipped while the user chooses a replacement for section 8 rule 3. The rule, ray caps and lack of a ray budget are unchanged. |
| 2. Missing reflection screenshots, high | Partly resolved. The three still-capture steps of `scripts/reflections.json` were rendered with the tracer on and off. `/private/tmp/gayt-fix/reflections-stills.json` omits only the 200-frame film to keep captures small. All six PNGs and the fresh demo deck PNG were opened. Front and low views show the red box, bright bar and blue sphere reflected with the tracer; the front off view lacks these object reflections. The rough surfaces have strong speckling. The deck's roughness-0.38 paint still does not show an unambiguous reflected lamp or corridor, so section 14 item 4 remains unverified at that roughness. This is not a reason to silently change finding 1's rule. |
| 3. Superseded docs and missing loop-read instructions | Resolved. Added supersession notes to `screen-space-illumination.md`, `screen-space-reflections.md` and `ambient-occlusion.md`, retaining decisions 13, 39 and 41 and replacing decision 30's stale “Exactly one bounce” with temporal decision 17. Replaced screen-space-artifacts decision 2's stale “no temporal accumulation, no TAA, no temporal upscaling.” Added the final-dump procedure and regex requirements to `tools/README.md`. Corrected ARCHITECTURE.md's stale `tools/loop-reads.py` to `tools/sol-walk/loop-reads.py`. |
| 4. Billboard, hair receiver and density tests | Resolved, with a real billboard defect fixed. `AnImpostorsGlowReachesAMirrorOnlyWhenItsEntityHoldsNoLight` failed for a lamp with reflected value 3.9941406 against the bound below 0.01, before the fix. The CPU billboard path dropped the already-existing light mark. It now carries `PreparedInstance.HoldsLight` through `ImpostorDraw` and `GpuInstance.FromAxes`; both lamp and non-lamp cases pass. The original red output is in the turn transcript; the shared focused log was later overwritten, so there is no separate preserved billboard-red file. `ScreenTraceRecordTests` also tests passing through the receiver's own hair card while hitting a distinct card, compares block-record densities with the recorded directions' C# densities for both ray kinds, and tests exact zero and below-2^-100 density codes. Those latter checks cover existing behavior and required no production rule change. |
| 5. V6's changed cell logic and missing comparison | Resolved as a cell-count comparison, not a new GPU timing. Section 6 and ter-dzxf now record the replacement of the 0.005 px nudge with `childAt` and `solved`, and the original commit's 978 skipped hits per million rays. `ScreenWalkTests.RandomRays` compares both walks on 1,000,000 rays, seed 20261104, cap 4,096 and a 128 × 96 room. Mean counts: port 10.470271 cells, nudged V6 10.454608 cells, about 0.15 % fewer. The port agrees with the flat-slab march for every ray. `/private/tmp/gayt-fix/walk-steps.log` is preserved. The result is recorded beside V6's 3.90 ms; it does not rescale that timing or separate the causes of the deck's 8.27 ms. Hill-climb winning changes still belong to ter-rgg9 after this ticket. |
| 6. Two writers of composite light bindings | Resolved without changing rendering behavior. `CompositePass` now owns the light descriptor set and writes all four bindings in one `Resize` operation. `RenderTargets` and `ScreenTraceImages` no longer write competing stand-ins and histories. After image creation or resize, the renderer calls that one owner; callers no longer need to know which writer must overwrite which binding. Focused GPU and final-suite checks exercise the resulting set. |
| 7. Tracer enablement inferred from the pyramid | Resolved. The shading constant comes from `options.ScreenTrace is ScreenTrace`, not pyramid existence, and is stored once after environment creation. The same option already selects target creation and tracer construction. No new renderer setting was added. |
| 8. Repeated history reads and exposure bounds | Resolved without changing the weighting or rejection rules. `signals.glsl` owns `HISTORY_READER` for weighted tap reads and `historyRatio` for exposure conversion. The accumulator, hit reads and ray plan use them. The reader is a macro because GLSL storage-image function parameters do not preserve the source format qualifier; two rejected function forms failed shader compilation, while the macro compiles and passes the focused GPU checks. |
| 9. Mode-selected tracer passes | Resolved without changing pass order or dispatches. `ScreenTraceKernel` owns the shared pipeline, bindings, push constants and dispatch. `ScreenTracePlanPass`, `ScreenTraceRaysPass` and `ScreenTraceResolvePass` each own their operation's resource declarations and calls. The `ScreenTraceStep` mode enum is gone. |
| 10. Stale comments and architecture | Resolved. Replaced “An impostor billboard writes 0.” with its actual light-mark behavior. Removed the stale `reflection_own_rays.glsl` reference. Corrected FrameGraph's false “A reflection shows the last frame's scattered skin, through the hit's history.” The C# walk comment now states that the hair exclusion is GPU-only and names its GPU test. ARCHITECTURE.md also records the one composite-set writer, explicit pass types, history helpers, cell-tracking change and the six passes between direct and composite (not the stale “Seven passes”). |
| 11. C# copies of deleted shaders | Resolved here, not deferred. Deleted `AmbientTerms.Occluded`, `GgxLobe.SpecularOcclusion`, the estimate parameters of `MaterialSample.ReflectedHemisphere` and their obsolete tests. Kept the `AmbientTerms` value and live closed-form hemisphere calculations used by `BrdfRenderTests`, `BrdfShaderTests` and the remaining `MaterialSampleTests`. The parent must reconcile the prior deferral ticket ter-76e7; this stage does not close it. |
| 12. Missing 64-lamp hall timing | Resolved by the documentation alternative the review allowed, not by inventing a timing. Section 11 now says that part 7 omitted the hall because its enumerated runs named deck, floor and cuts even though the broader measurement list included the hall. No hall timing exists and its estimated 2.94–3.65 ms room remains unverified. |
| 13. Misleading `--in-flight` count | Resolved, red first. An explicit count now sets `RendererOptions.FramesInFlight` to 1 or 2 and disables or enables captures in flight respectively. Absence preserves both defaults. Tests include overriding an initial count of 3. `/private/tmp/gayt-fix/cli-red.log` records the old behavior leaving 2 frame slots after a request for 1; the Host focused run passes all 99 tests. The CLI name and accepted values are unchanged. |

### Unresolved user-visible fork

The targeted GPU test confirms that independent rough-ray distances can restart otherwise-valid history. It does not prove that every noisy paint pixel has this cause. No change to section 8 rule 3 was made.

| Design | What the user gets | Cost |
|---|---|---|
| Filtered distance | Compare recent reflection-distance averages, retaining a distance check on rough paint. | Adds stored filtering state and rules. |
| Near-mirror check | Apply single-ray distance rejection only on nearly smooth surfaces. | Needs a roughness threshold and can retain stale rough reflections. |

Both designs retain the existing surface reprojection tests, a distance check for mirrors, the portable words-form walk, one pyramid owner, reflection cap 256, diffuse cap 62 and no ray budget. The parent must ask the user to choose or combine these designs before a behavior change. The ticket is not ready for integration acceptance.

### Review gaps and limits

- The review ran no builds or tests. This stage's actual results are recorded below; the review's earlier unavailability is not a final-suite failure.
- Fresh final MoltenVK dumps are in `/private/tmp/gayt-fix/msl`. The block dump `shader-cs-e1e81b260adc01e7.metal` matches the final block SPIR-V byte for byte (SHA-256 `677f75b00e12f2b5abae335fae42623c415567f86a489801df7fdf756d7533c2`). The own dump `shader-cs-65b7d45f2c3f3389.metal` matches the final own SPIR-V (SHA-256 `87e88c8701b53d00cee9000cd6ab054af4d3f426f31187c82675004c4f128e94`). Both kinds use each kernel through specialization constant 5. `loop-reads.py` was run with all actual device-pointer names, not just array accesses: 3 total accesses and 0 in a loop for block; 7 and 0 for own. `/private/tmp/gayt-fix/loop-reads.log` records them. The tool does not count the structured outer own-ray stride loop; its list entry is read once per ray before the walk loop. No per-ray device-buffer read remains in the walk loop.
- The GPU-to-C# pin now checks C# misses as well as hits, including the miss's zero distance. A spurious shader hit no longer escapes that test.
- New GPU probes execute exact-zero and very small densities. The 2.4 % round-trip tolerance follows half-float log rounding near -100 (at most 1/32 stop). An earlier 1.2 % test tolerance failed at ratio 0.9794922; this was an incorrect test bound, not a production encoding change.
- The original diffuse overflow cap and distant half-float distance cap still have no demonstrated red run on this Mac. Neither lavapipe nor a Linux driver was run. Mac stores can mask the missing-distance-cap defect by saturation. The parent owns portability checks and mutation evidence; this stage makes no new portability claim.
- The old eight parts' red-first history remains unverified: their commits include tests and implementation together, and the completed review found no failing logs. This stage's CLI and billboard regressions were red before their fixes; the rough-history regression remains deliberately skipped after its preserved failing run. Do not infer historical TDD evidence from current green suites.
- The full current `ScreenTraceRenderTests.cs` was read against `tests/STYLE.md` to finish the review's missing style inspection, without rerunning a review stage. The tests name domain behavior, use the real GPU through deterministic `Gpu.Headless` setup, keep scene premises in the tests and check results rather than encoding constants alone. The new billboard regression is one behavior with both light-mark cases. No additional style-only code change was needed; the normal build also runs the style analyzer.
- Coverage, mutation testing, hall GPU timing and attribution of the deck's 59 % own-reflection share were not run. They are not implied by the cell-count comparison or captures. No measured timing stops this ticket under decision 43.

### Captures inspected

The stills use the real Release renderer at 1280 × 720 with its default 32 warm-up draws. The capture runs used `tools/gpu-lock.sh` and `tools/gpu-clock.sh`; their logs retain clock evidence, but these three-frame runs are not new performance comparisons. The original timing evidence is unchanged.

| Screenshot | Caption |
|---|---|
| `/private/tmp/gayt-fix/reflections-on/front.png` | Tracer on: the red box, bright bar and blue sphere have visible floor reflections; rough areas remain speckled. |
| `/private/tmp/gayt-fix/reflections-off/front.png` | Tracer off: the front view has no matching object reflections and is much smoother. |
| `/private/tmp/gayt-fix/reflections-on/low.png` | Low view with the tracer: colored reflections stretch across the strips, with strong grain on rough surfaces. |
| `/private/tmp/gayt-fix/deck/deck.png` | Demo paint at roughness 0.38: indirect light and grain are visible, but a reflected lamp or corridor is not unambiguous. |

All on/off front, low and edge images, plus the deck image, were opened before these captions were written. No snapshot reference was changed or re-approved.

### Check results

| Check | Result | Evidence |
|---|---|---|
| Focused Rendering tests | 39 passed, 1 explicitly skipped, 0 failed | `/private/tmp/gayt-fix/final-focused.log` |
| Focused Host option and capture tests | 99 passed, 0 failed, before and after the hook-driven simplification | `/private/tmp/gayt-fix/host-focused.log`, `/private/tmp/gayt-fix/host-refactor.log` |
| Million-ray walk comparison | 1 passed, 0 failed | `/private/tmp/gayt-fix/walk-steps.log` |
| Full Checked solution, final files | 9,844 passed, 22 skipped, 0 failed | `/private/tmp/gayt-fix/checked-post-hook.log` |
| Full Release solution, final files | 9,831 passed, 22 skipped, 0 failed | `/private/tmp/gayt-fix/release-post-hook.log` |
| Benchmark Release build, final files | Passed, 0 warnings and 0 errors | `/private/tmp/gayt-fix/benchmark-post-hook.log` |
| CSharpier on changed C# files | Passed | `/private/tmp/gayt-fix/format.log` |
| Staged whitespace check | Passed | `git diff --cached --check` |
| Coverage and mutation testing | Not run; parent-owned | This fix stage's execution boundary |

Every build, test, formatting run and shader inspection command used the Nix dev shell. Both final configurations have code-tree identity `83146a66c7d0018ca00ec14e06c0b8c3c3afb73c`, matching the staged code. The final ray SPIR-V files were compared again after the last comment correction and still match the kept fresh dumps byte for byte. The 22 skipped tests include the one newly preserved rough-history regression; the other 21 skips predate this stage. Intermediate shader-format and reference build errors were fixed before the passing focused and full runs. The first suite refresh followed the stale composite-set comment correction. The last refresh followed the behavior-preserving, coupled CLI update described below. All earlier logs remain preserved.


### Commit-hook follow-up

The first fix commit is `a42d23595c2e7794033029d82b8084daa32d35fb`; it is preserved, not amended. Its hook recognized the exact full Checked run and skipped both the build and repeated tests. The hook also ran the normal code metrics: erosion 0.1912, verbosity 0.0280, pooled TCC 0.8865 and pooled depth 2.5607. There were 195 included cohesion types, 769 not applicable and 40 incomplete, with 239 unresolved state-access locations and 3,629 binding errors. These ratios describe the counter's complete population, not proof that every type was analyzed.

The hook found CC 11 in the CLI override application. That was a real opportunity to make the update clearer: two separately conditional assignments controlled one coupled change. The follow-up keeps the same tested behavior with one conditional update, rather than adding a helper solely to move branches out of the measured method:

```csharp
            if (FramesInFlight is { } count)
            {
                options.FramesInFlight = count;
                options.CapturesInFlight = count > 1;
            }
```

where

- `FramesInFlight` = the optional CLI frame-count override, 1 or 2 frames, or null when absent.
- `count` = the requested count after the CLI validates it.
- `options` = the headless renderer's settings receiving the overrides.
- `options.FramesInFlight` = the number of frame slots the renderer uses.
- `options.CapturesInFlight` = whether captures can return while submitted frames are still running; one frame disables this and two enable it.

The focused Host option/capture checks still pass all 99 tests. The full solution suites are refreshed after this behavior-preserving change; their final logs are recorded in the check table. No shader or temporal rule changed in this follow-up.

The four length reports were inspected against their callers and roles. They remain explicit exceptions, not silently dismissed measurements:

| Method | Reported length | Reason to retain its shape |
|---|---:|---|
| `VulkanRenderer` constructor | 64 lines | This is the composition root: it makes the resources and registers passes in their lifetime and frame order. Extracting an arbitrary batch would hide that order without removing a caller decision. The new light-set ownership is already below this root in `CompositePass`. |
| `VulkanRenderer.AddScreenTracePasses` | 74 lines | This one owner creates the two signals and their images and registers the six ordered tracer/accumulation passes. Its local `Kernel` factory already compresses the shared dependency and specialization setup for four kernel creations. Moving it to a renderer-wide helper would expose shaders, images and settings as repeated arguments just to reduce a line count. |
| `BrdfShaderTests.AGrazingViewReadsTheSchlickPartAtTheTexelOfItsViewCosine` | 64 lines | The test keeps the black material, uniform environment, chosen view cosine and C# comparison in one visible setup, as tests/STYLE.md prefers. Its live hemisphere call lost only the deleted estimate parameters. A setup wrapper would hide the premise, not the removed mechanism. |
| `ScreenTraceRenderTests.TheReflectionSampleOfAMirrorFloorHoldsTheGlowOfThePixelThatTheCSharpWalkHits` | 64 lines | One equivalence check now includes both CPU hits and CPU misses. Keeping the miss assertion beside the hit assertion makes its formerly missing case explicit; extracting only a few assertions to meet 60 lines would hide the core comparison. |

The small pass types separate genuinely different frame operations; the shared kernel hides Vulkan pipeline and binding details. The descriptor-set change removes two competing writers, rather than adding a wrapper around them. These are source-and-caller reasons for the design, independent of the pooled metric movement. No branching or artificial state access was added to raise a metric.

## Approved A continuation, 2026-11-04

The user approved A after the engine-source comparison: history estimates reflected light, not the identity of one random hit. This settles finding 1 and supersedes the historical fork above. It does not choose distance stabilization or a new near-mirror cutoff.

This fix stage verified the preserved checkout path, branch, clean status and HEAD `e31dffe7dc32bd796b752f3c7ab5d9015a139fe9` before any write. T3 also confirmed that this thread is attached to that checkout. The original review base remains `86c3c497f2c3ac2d42a3efd46c8082ff83e9987b`. All eight original parts, fixes `a42d23595` and `e31dffe7d`, prior measurements and `/private/tmp/gayt-fix/history-red.log` remain preserved. The parent inspected the stopped workflow `95692eb7-33aa-4609-9830-abbb8525f3b2`; this continuation started no agent or workflow and repeated no completed workflow stage.

### The history contract

The surface-motion place is the receiver's previous image position. It keeps the plane, geometric-normal and roughness gates, without a single-ray hit-distance veto. The reflection-motion place is the reflected virtual point's previous image position. It keeps its existing roughness eligibility and hit-distance gate. Rejecting it cannot reject a valid surface-motion place.

The existing fast-history comparison chooses among valid places. Its current-light mean now includes only finite samples on the same surface, through the existing `sameSurface` predicate. The fast and slow histories, roughness-dependent caps, spatial repair and spatial clamp stay shared and unchanged. A numerically nearer luminance is not treated as proof that old light is current: the response tests below exercise the actual accumulated output. They found no need for another lighting threshold to meet the existing ten-frame response bound.

The change adds no history image, distance filter, parallel denoiser or library. It changes no numerical threshold, ray count, ray cap, budget, FSR rule, traversal or hill-climb. The reflected-place helper no longer takes a boolean that could apply its distance test to the surface place. The owner and stores of temporal state do not change. These are concrete scope and ownership properties, not claims from metric movement.

Section 8 rules 3 and 4 of `docs/plans/temporal-renderer.md`, `ARCHITECTURE.md` and the `SignalParameters.ReflectedPlaceBelow` documentation now describe this contract.

### Red and green evidence

The explicit skip on `IndependentRoughReflectionDistancesDoNotRestartAnUnchangedLightsHistory` is removed. `/private/tmp/gayt-history-a/history-a-red.log` reran it before implementation and again found length 1 after 12 unchanged-light frames. `/private/tmp/gayt-history-a/history-a-complete-red.log` records five intended failures before any shader edit. The first test-helper build errors and an incorrect boundary-scene setup are retained in separate logs; neither is counted as valid red evidence.

| Behavior | Before A | After A |
| --- | --- | --- |
| Unchanged light at roughness 0.38, independent 2 m and 8 m hits over 12 frames | Length 1 | Exactly 12 frames retained |
| The same unchanged light on a smooth receiver | Length 1 | Existing cap of 4 retained |
| Reflected-place distance rejection with a valid surface place | Length 1 | Existing cap of 4 retained |
| Light on a different neighboring surface cannot choose the receiver's history | Receiver light 3.2441406 instead of 1 | Receiver light within 0.1 of 1 |
| One bright sample with an unrelated distance change | Length 1 | At least 8 history frames retained, output below 3 for a sample of 20 |

The old both-candidate distance-invalidation test is replaced by the new surface-retention behavior test. The reflected-place fallback test still accepts 20 m and 39 m against stored 20 m, and rejects 41 m when the surface place leaves the image. It also checks fallback at roughness 0.38 and its existing ineligible roughness case. The receiver-normal and receiver-roughness tests keep the same entity and mesh, so a missing motion history cannot hide a broken gate. The uncovered-receiver test checks rejection of the nearer occluder's old depth through actual light and history length.

Four light-step cases cover rising and falling light at roughness 0.05 and 0.38. Two real-tracer cases move a glowing panel or stop its emission. Each reaches at least 90% replacement of the old mirror light within ten renderer frames, using the existing response-time bound. This is not an instant noise-free guarantee. The core batch passes 34 cases, and the final focused suites include those cases.

### Matched captures

Exact scripts, commands, settings, frame counts, logs and image captions are in `/private/tmp/gayt-history-a/manifest.md`. The controlled pairs are in `/private/tmp/gayt-history-a/{before,after}/fixed`. The earlier automatic-exposure captures are retained separately and are not used as the matched comparison.

Both fixed-exposure baselines finished before any production code or shader changed. Both use the real Demo scenes and Host runner at 1280 × 720, synchronous captures, default tracer settings and the same deterministic draw-index sequence. A temporary program uses the Host's existing renderer factory to set fixed exposure, because the public Demo CLI has no fixed-exposure flag. Its source remains beside the manifest, outside the repository. The same scripts and factory produce the final pairs. Exposure is -6.8 stops for reflections and 1.57 stops for the deck. Each selected still follows 65 renderer draws since reset, including 32 hidden warm-up draws. The moving follow-camera sequence covers 40 consecutive frames, about 0.67 s of game time.

| Caption | Matching old-rule image | Final image |
| --- | --- | --- |
| Front: the box, bar and sphere remain reflected; substantial rough-surface speckling remains. | `/private/tmp/gayt-history-a/before/fixed/reflections/front-000032.png` | `/private/tmp/gayt-history-a/after/fixed/reflections/front-000032.png` |
| Low: the colored reflections remain visible, but coarse grain is still strong. | `/private/tmp/gayt-history-a/before/fixed/reflections/low-000032.png` | `/private/tmp/gayt-history-a/after/fixed/reflections/low-000032.png` |
| Deck: the actual roughness-0.38 paint remains grainy; an identifiable reflected lamp or corridor is not established. | `/private/tmp/gayt-history-a/before/fixed/deck/deck-000032.png` | `/private/tmp/gayt-history-a/after/fixed/deck/deck-000032.png` |
| Moving camera: the last of 40 frames retains pane grain; no obvious new long trail is visible in this short sequence. | `/private/tmp/gayt-history-a/before/fixed/deck/moving-000039.png` | `/private/tmp/gayt-history-a/after/fixed/deck/moving-000039.png` |

All eight selected fixed-exposure images were opened at full size. Eight contact sheets were opened to inspect every saved frame of the controlled sequences. No snapshot reference was changed or re-approved.

### Remaining limits and ownership

The captures do not establish the roughness-0.38 identifiable-reflection criterion of section 14 item 4. They do not show that all grain has the fixed defect as its cause, nor prove the absence of trails in every scene. The parent still owns visual acceptance, changed-line coverage, mutation testing and integration. The earlier portability gaps, original eight parts' missing red-first evidence, unmeasured hall, unresolved attribution of the deck's 59% own-reflection share and prior frame-budget measurements remain as recorded above. No new GPU timing comparison or Linux-driver run is claimed. This stage does not merge, push, close the ticket, publish a PR or remove a worktree.

### Final continuation checks

All final build, test and formatting commands ran through the Nix dev shell, one heavy command at a time. The final focused and full solution runs include the fixture correction that keeps the receiver's entity and mesh during normal and roughness changes. All final commands exited successfully.

| Check | Passed | Failed | Skipped | Evidence below `/private/tmp/gayt-history-a` |
| --- | ---: | ---: | ---: | --- |
| Focused Rendering, Checked | 276 | 0 | 0 | `focused-checked-final.log` |
| Focused Rendering, Release | 276 | 0 | 0 | `focused-release-final.log` |
| Full solution, Checked | 9,862 | 0 | 21 | `full-checked-final.log`, 9,883 total |
| Full solution, Release | 9,849 | 0 | 21 | `full-release-final.log`, 9,870 total |
| Release benchmark compilation | Pass | 0 errors | Not applicable | `benchmark-build.log`, 0 warnings |
| CSharpier, three changed C# files | Pass | 0 | Not applicable | `format-check-final.log` |
| Staged whitespace | Pass | 0 | Not applicable | `staged-whitespace.log` |

The 21 remaining skips predate this continuation. No snapshot failed, changed or was re-approved. Coverage and mutation testing were not run; the parent owns those checks after visual acceptance. Completion of this fix stage is not a claim that the ticket is merged or that the earlier acceptance gaps are closed.

### Real-deck history diagnosis, November 4, 2026

Diagnostic only, from clean `aaa5e776c4b33b985ca1a7cf0b9caea008b30a60`. Artifacts, commands, masks, external probe patches, and full measurements: `/private/tmp/gayt-history-diagnosis/manifest.md` and `report.md`. No renderer-policy or tracked instrumentation changes.

The real deck at 1280 by 720 and fixed exposure 1.57 reproduces the approved-A image byte-for-byte. Three interior roughness-0.38 paint masks contain 54,400 pixels. Observed draws 33–97 and 161–192 give 97 observations per pixel. A frozen continuation advances only its capture clock. Observed, unobserved-control, and external-probe screenshots match byte-for-byte at draws 65, 97, and 192.

| Measurement | Floor-left | Floor-center | Wall |
| --- | ---: | ---: | ---: |
| Mean reflected slow length, frames | 16.414 | 16.486 | 16.460 |
| At reflected maximum of 17.078 stored frames | 79.448% | 80.021% | 79.649% |
| Reflection-motion candidate hit-distance rejection | 48.945% | 53.984% | 32.781% |
| Actual reflected color changed by spatial limiter | 5.962% | 6.892% | 6.741% |
| Diffuse share of linear-image variance, static draws 66–97 | 81.606% | 88.780% | 99.555% |
| Display temporal standard deviation, draws 66–97 / 161–192, 0–255 code values | 1.318 / 1.289 | 1.098 / 1.082 | 1.573 / 1.541 |

Surface-motion history remains valid at every measured pixel. Neither receiving-surface rejection, leaving the image, missing finite samples, nor failure of both candidates causes any history loss in these masks. The reflected-distance veto affects only its candidate. Fast effective length is 4 for both signals. Mean diffuse slow length is 31.66–31.83, with an unchanged maximum of 32. Short-history spatial repair runs in 0% of the observed transitions. An approximate replay that dims previous slow and fast light by 1% predicts final-history dimming of about 0.93% reflected and 0.96% diffuse. Stored length is therefore not the only evidence that old light survives.

Supported explanation: noisy indirect samples, nearly all shared across 2 by 2 pixels, enter bounded temporal averages. The averages reduce but retain variance, and the steady-state short-history repair is inactive. Most measured linear-image variance is diffuse light. The limiter has a smaller measurable effect, not an established sole cause. A next narrow experiment is a display-space decomposition with one indirect signal held at its measured mean while replaying the other through unchanged post passes. No fix is selected or authorized.

The real-deck recognizable-reflection criterion, moving-camera trail acceptance, changed-line coverage, mutation testing, and Linux-driver validation remain pending. Existing rare-emitter bias remains with `ter-18hc`; no new out-of-scope defect or duplicate ticket was established. The ticket stays open. No merge, push, commit, new implementation workflow, or whole-suite rerun occurred.

## Parent scope reconciliation, 2026-11-05

The branch implementation/fix notes and the pre-existing diagnostic note above remain intact. The following parent decisions are carried forward from the latest root-note snapshot against its preserved common base. This worker owns local hardening only; the parent owns integration and ticket closure.


**2026-11-04T23:36:53Z**

2026-11-04 Sol handoff: prior workflow 5fb9756d-2153-4e58-ae8b-648f521b25a6 completed build and review; all members terminal and cleanup complete. Preserve clean branch ter-gayt-screen-tracer at ee8070bb17d08c6ee4f8da82fd8bb55ffa3c5d60, prior measurements and review. Continue ONLY fix in workflow 95692eb7-33aa-4609-9830-abbb8525f3b2, version 54c27845-ff8e-40aa-8627-5dd6304cee16, Codex gpt-6.1-sol reasoningEffort=xhigh. No rerun of build or review, no reset or new checkout. Diagnose sameHit history-reset hypothesis before changing the rule; any new rule remains a user fork. Migration inventory and tested continuation source: /private/tmp/tertius-sol-handoff-20261104/.

**2026-11-05T01:07:48Z**

User approved design A in the takeover thread: separate surface-motion and reflection-motion history validation. A raw reflection-ray distance mismatch must not veto otherwise valid surface-motion history; retain distance validation for the reflection-motion candidate. Reuse existing surface checks, local-light selection and fast/slow accumulation, strengthening lighting validation only where motion tests show a correctness gap. No distance-stabilization filter (design B), no new ray budget, no FSR workaround. Continue from clean e31dffe7dc32bd796b752f3c7ab5d9015a139fe9 on ter-gayt-screen-tracer. Request red-green GPU regressions and matched real-renderer before/after captures before visual acceptance. Preserve completed build/review/fix work and original measurements. Parent retains coverage/mutations and merge decisions; ticket remains open. For future user-visible design choices, use the ask-user-question tool when available.

**2026-11-05T01:10:35Z**

Approved-A fix continuation launched: workflow 4f18d006-7d67-4219-9599-efb101107e65, version 54c27845-ff8e-40aa-8627-5dd6304cee16, provider codex / gpt-6.1-sol / reasoningEffort=xhigh. Attaches preserved ter-gayt-screen-tracer at e31dffe7dc32bd796b752f3c7ab5d9015a139fe9; no repeated build/review and no reset. First request with deliveryThreadId=bd9cdc24-a81f-41f6-8461-9275aa80c44f was rejected FORBIDDEN (Delivery destination is not authorized), without a returned run. Explicit retry without automatic delivery admitted this run. Current phase fix; member mcp:launch:a6568589077da7f00d197b2c5e26a143c0472f8fef1d0df99ddb68928f28faaa. Results remain in workflow status; matched captures/logs requested under /private/tmp/gayt-history-a. No automatic completion delivery to this takeover thread is configured.

**2026-11-05T12:13:08Z**

2026-11-05: user explicitly requests History Rule A integration after visual acceptance and validation, without waiting for continuing noise/filter research. Validation workflow e9b5b21a-8e33-4575-a49a-30e816436d4b (version 077df081-6e60-4160-b9a4-b89ea970421a) attaches aaa5e776c on the preserved tracer branch. Because A rests on the unmerged tracer, validation covers the actual branch scope. The ongoing diagnostic thread has been steered to keep further work external and not edit tracked files during validation/integration. Remaining grain and identifiable-paint-reflection acceptance must remain explicit follow-up work rather than being claimed fixed. Parent owns visual acceptance and merge.

**2026-11-05T12:22:53Z**

2026-11-05 parent visual inspection for the approved A change: inspected matched fixed-exposure front and actual-deck images, plus the after low-angle and moving-camera images. The front retains recognizable box/bar/sphere reflections and the deck retains its structure; no new gross still-image regression is apparent. This accepts A as the narrow receiving-surface history correction, not as a noise cure. Grain remains strong, especially in the moving view's panes. The existing 0.67-second sequence cannot establish universal absence of trails. The original real-deck rough-paint identifiable-reflection quality criterion remains unfulfilled and is now tracked explicitly in ter-icn3. This is a scope transfer under the user's instruction to integrate A independently of ongoing noise ideas, not a claim that the criterion passed. Merge still waits for validation/hardening and integration checks.

## Parent-commissioned hardening, 2026-11-05

Local hardening is complete for the delivered tracer and approved History Rule A. This does not close the ticket or authorize this worker to merge it. The parent owns integration, its combined checks, and ticket closure. No denoising, light-budget, renderer-policy, algorithm, or limit change is included.

### Scope, preservation, and tested identity

- Attached the existing `/Users/alexispurslane/.t3/worktrees/tertius/ter-gayt-screen-tracer`, branch `ter-gayt-screen-tracer`, at expected HEAD `aaa5e776c4b33b985ca1a7cf0b9caea008b30a60`. Checked the path, branch, HEAD, and initial working diff before writing. The only tracked edit at entry was the diagnostic note in this ticket; that entire entry ticket is preserved as an exact prefix.
- The whole-tracer scope remains original review base `86c3c497f2c3ac2d42a3efd46c8082ff83e9987b`, not only A's final eight-file fix. Coverage and mutation comparisons are pinned to immutable initial-main `f703caf174af14d5350e410ce2b26acfcc42e5fe`, whose merge base with this branch is the original review base. Later independent parent merges do not narrow that scope. Parent main is now `64ab730a0e0300942212d9b2d96e74f0bfcd3ec5`; it was not imported. Parent will reconcile the new Entity API and run affected combined checks.
- Prior implementation/review/fix work in workflow `4f18d006-7d67-4219-9599-efb101107e65`, commits, measurements, probes, and captures were reused, not repeated. This hardening belongs to workflow `e9b5b21a-8e33-4575-a49a-30e816436d4b`. No child agents or workflows were started.
- Semantically reconciled the latest parent `.tickets/ter-gayt.md` notes against `/private/tmp/tertius-integration-20261105/root-notes/ter-gayt.base.md`, preserving both sides. `.tickets/ter-icn3.md` is the exact supplied parent snapshot. Snapshot hashes and prefix-preservation evidence are external.
- The exact final code/test tree tested is `4cce74b01137f78d7775194652680ad6a039c966`, as calculated by `tools/code-tree.sh`, before and after final validation. Starting Git HEAD was `aaa5e776c4b33b985ca1a7cf0b9caea008b30a60`. The hardening commit containing this note has that exact tested code tree; only ticket/evidence prose changes follow the full suites. Its exact resulting commit is recorded in the external `final-state.json`, avoiding a self-referencing commit hash in this note. The final branch is not claimed to have been tested against the parent's later combined main.
- Before the final code freeze, ran `tools/cleanup.sh` through Nix on all 76 ticket-touched C# files, after mutant restoration and before official Stryker. Six production files have only equivalent syntax or formatting changes: `DepthPyramidImages.cs`, `ScreenTraceImages.cs`, `CompositePass.cs`, `OwnRayDispatch.cs`, `ScreenTraceSetting.cs`, and `ScreenRay.cs`. Exact changes are in `cleanup-production.diff`. This implements the parent's explicit early-hook-cleanup request, not a production redesign. No substantive production change was made.

All evidence below is under `/private/tmp/tertius-hardening-20261105/ter-gayt` unless an absolute path says otherwise. Final code freeze was `2026-11-05T08:17:07-05:00`; final validation checks completed at `2026-11-05T09:17:16-05:00`. All build, test, coverage, mutation, and cleanup commands ran through Nix, serially in this worker's heavy slot. No Checked build/test or commit occurred during official Stryker.

### Finite behavior ledger and added tests

`evidence-ledger.md` accounts for all 103 changed production paths: 14 added, 43 deleted, and 46 modified. It names C# orchestration, GLSL behavior, non-executable edits, removed paths, existing tests, and deliberate faults separately. It is a finite fault-based ledger, not exhaustive statement-level shader coverage.

| Behavior | Evidence and decisions pinned |
| --- | --- |
| History Rule A | A01-A09 all killed by existing real-shader accumulator regressions: independent rough/smooth surface history; reflected distance validation; surface fallback; receiving normal, roughness, and depth; mirror reflected-location fallback; changed-light response; same-surface current-light comparison. |
| Two-depth walk | CPU million-ray and exact binary-boundary reference tests reused. New actual-shader probes reject slab backs and backfaces and distinguish entering/leaving a child at an exact tie. W01-W07 all killed. |
| Pyramid ownership | Existing depth/padding/resize tests reused. New 1x1, 2x1, and 2x2 real-renderer cases exercise the no-tail allocation path. P01-P04 all killed. |
| Sampling, sharing, and resolve | Existing record, density, own-ray, and convergence tests reused. New converged-block tests reject normal/depth discontinuities. A probe inspects actual `recordOf` before half conversion; controlled records feed the actual embedded production resolve shader to test its radiance cap. R01-R14 all killed. |
| Accumulation and repair | A new isolated-neighbor test establishes short-history spatial repair. Existing finite-sample, clamping, step-light, and parity tests reused. T01-T03 and L03 killed. No estimator or cap was retuned. |
| Lifecycle and capture | A new isolated two-frame populated-surface/sky test verifies own-ray header clearing without overflowing a mutated list. L01-L04, H01-H03 killed; existing resize/history reset/capture completion tests reused. |
| Descriptor, barrier, switch, and deletion | D01 killed; D02 equivalent for the specific redundant declaration, as explained in its row. Existing frame-graph and resource-use tests reused. Replacement tracer/composite and CLI off behavior are tested. Removed sector/cone/radiance-pyramid entry points have no surviving production references; an orphan unreferenced deleted helper has no behavior to execute or mutate. Builds check the remaining callers. |

New test files are `ScreenTraceHardeningTests.cs`, `OwnRayResetRenderTests.cs`, and four shader probes. `ScreenTraceProbe.cs` now declares its real record/history/sample resource uses. The test project embeds the engine's actual `screen_trace_resolve.comp`; it does not test a copied resolve implementation. Test fixtures and observables are bounded and deterministic. No extended soak or MTBF claim is made.

### Final suites and checks

| Final run | Passed | Skipped | Failed |
| --- | ---: | ---: | ---: |
| Full solution Checked | 9876 | 21 existing | 0 |
| Full solution Release | 9863 | 21 existing | 0 |
| Coverage Checked Rendering | 2283 | 8 | 0 |
| Coverage Release Rendering | 2282 | 8 | 0 |
| Coverage Checked Host | 1964 | 5 | 0 |
| Coverage Release Host | 1964 | 5 | 0 |

Final full suites ran once after the last code/test change: `nix develop --command dotnet test --solution Tertius.slnx -c Checked`, then Release, through `run-final-validation.sh`. Checked took 4m27.880s; Release took 4m16.577s. Logs are `final-validation/solution-Checked.log` and `solution-Release.log`. The later documentation-only changes and commit do not invalidate the exact code-tree identity.

The explicit Release benchmark build passed with zero warnings and errors; benchmark timings were not run or claimed. CSharpier checked all 76 ticket-touched C# files successfully. Owned-file and full-scope production/test whitespace checks passed. The blanket original-base whitespace check exits 2 solely for extra EOF blank lines in six unrelated ticket files already present at entry: `ter-12xp`, `ter-186q`, `ter-18hc`, `ter-76e7`, `ter-dbzx`, and `ter-x8bz`. The identical entry-HEAD check is preserved as `whitespace-inherited-at-entry.log`; those files were not changed. This is a reported inherited warning, not a clean blanket-check claim. The initial validation script stopped at this check after both suites, benchmark, and formatting passed; its closeout verified the unchanged tree and clean owned/code scope without repeating the suites.

### Changed-line C# coverage

Read `tools/coverage/run.sh` and `changed-lines.py`; used targeted Rendering/Host coverage in both configurations rather than unrelated-module coverage. Final comparison command: `nix develop --command python3 -I tools/coverage/changed-lines.py --base f703caf174af14d5350e410ce2b26acfcc42e5fe /private/tmp/tertius-hardening-20261105/ter-gayt/coverage-final/Checked /private/tmp/tertius-hardening-20261105/ter-gayt/coverage-final/Release`.

Initial changed C# coverage was 810/814 lines and 194/206 branches. Final changed C# coverage is 814/816 lines and 190/202 branches. Mechanical cleanup changed syntax and mapped branch populations, so those denominators are not a fixed-population before/after score. Counts below are covered/total changed mapped lines and branches, merged across the final Checked/Release reports. These measure CPU execution only, never shader execution. Deleted files, declarations, and changes with no instrumented sequence point have no invented runtime percentage; their ledger rows state the evidence kind.

| Affected file under `src/` | Changed lines | Changed branches |
| --- | ---: | ---: |
| Tertius.Host/ScriptCommand.Options.cs | 5/5 | 4/4 |
| Tertius.Host/ScriptCommand.cs | 9/9 | 8/8 |
| Tertius.Host/Scripting/Playback.cs | 12/12 | 6/6 |
| Tertius.Rendering/Frame/DepthPyramidImages.cs | 86/86 | 20/20 |
| Tertius.Rendering/Frame/DescriptorLayouts.cs | 70/70 | 10/10 |
| Tertius.Rendering/Frame/FrameHistory.cs | 3/3 | 2/2 |
| Tertius.Rendering/Frame/RenderTargets.cs | 17/18 | 18/20 |
| Tertius.Rendering/Frame/ScreenTraceImages.cs | 103/103 | 10/10 |
| Tertius.Rendering/Frame/SignalParameters.cs | 20/20 | 0/0 |
| Tertius.Rendering/Frame/SignalScratch.cs | 25/25 | 4/4 |
| Tertius.Rendering/Frame/TemporalSignal.cs | 22/22 | 0/0 |
| Tertius.Rendering/Passes/CompositePass.cs | 23/23 | 14/14 |
| Tertius.Rendering/Passes/DepthPyramidPass.cs | 37/37 | 0/0 |
| Tertius.Rendering/Passes/ImpostorPass.cs | 2/2 | 0/0 |
| Tertius.Rendering/Passes/OwnRayDispatch.cs | 5/5 | 0/0 |
| Tertius.Rendering/Passes/ScreenTracePass.cs | 132/132 | 19/20 |
| Tertius.Rendering/RendererOptions.cs | 2/2 | 0/0 |
| Tertius.Rendering/Scene/GpuTypes.cs | 13/13 | 0/0 |
| Tertius.Rendering/Scene/ImpostorManager.cs | 1/1 | 0/0 |
| Tertius.Rendering/ScreenTraceSetting.cs | 14/14 | 16/16 |
| Tertius.Rendering/ShaderConstants.cs | 2/2 | 0/0 |
| Tertius.Rendering/Shading/DepthPyramid.cs | 16/16 | 6/10 |
| Tertius.Rendering/Shading/MaterialSample.cs | 1/1 | 0/0 |
| Tertius.Rendering/Shading/ScreenRay.cs | 39/39 | 19/20 |
| Tertius.Rendering/Vulkan/ResourceState.cs | 1/1 | 0/0 |
| Tertius.Rendering/Vulkan/VulkanDevice.cs | 1/1 | 0/0 |
| Tertius.Rendering/VulkanRenderer.cs | 153/154 | 34/38 |

Remaining uncovered behavior was inspected per location, not hidden by the pooled count:

- `RenderTargets.cs:444` rejects unavailable sampled/storage format support; current hardware supports the formats, so this device-dependent rejection was source-assessed, not executed. The partial guards at 215/443 are oversized-allocation and device-format guards. Public option tests reject unsafe sizes before allocation. Judgment: do not allocate an unsafe image or substitute a fake Vulkan device for a coverage number.
- `VulkanRenderer.cs:1106` and partial 1097/1104/1203 are disabled-tracer diagnostic-read guards, not missing production rendering behavior. Existing switch tests exercise legal disabled rendering. Judgment: no private diagnostic-only throw assertion solely for coverage.
- `VulkanRenderer.cs:1985` and `ScreenTracePass.cs:157` guard a missing owned pyramid while the tracer is enabled. Normal construction/resize creates both together; failed construction cannot render. Judgment: do not corrupt private ownership state to cover an unreachable consistency throw. Legal lifecycle/resize behavior is tested.
- `DepthPyramid.cs:426` has unvisited combinations of out-of-image geometric-normal bounds checks; `ScreenRay.cs:195` has one unvisited slab/facing short-circuit combination. Judgment: the CPU million-ray/tie reference tests and W01-W05 cover meaningful slab/facing/boundary decisions; no assertion is added merely for a short-circuit combination. The no-tail allocation gap is now fully covered and P04 killed.

The initial implicit Release Host coverage build failed without useful diagnostics. An explicit diagnostic build and no-build retry passed; the cause remains unknown and the failed attempt is preserved, not counted as evidence. Final Host coverage was rebuilt/rerun and passed in both configurations. `coverage-review.md` and the full Cobertura reports preserve details.

### Official Core mutation result

Ran the prescribed changed-line entry point with the immutable comparison: `nix develop --command python3 -I tools/stryker/changed.py f703caf174af14d5350e410ce2b26acfcc42e5fe /private/tmp/tertius-hardening-20261105/ter-gayt/stryker-final`. It used only the official locking wrapper, concurrency 1 at nice 20, and four xunit threads per host. No Checked output was built/tested or committed during it.

`stryker-final/results.json` reports `official: true`, no problems, zero Dubious tests, and zero changed-scope executable mutants. Its sole unmutated changed line is `src/Tertius.Core/Seams/RenderSeam.cs:492`, `void FinishCaptures();`: an interface declaration with no executable body. Explicit no-mutant judgment: no implementation exists on that line to mutate; actual completion behavior is killed by listed L04/H03 in Rendering/Host. Other Core changes are documentation; no Simulation/Physics production behavior changed. A mutation score percentage for zero executable mutants would be meaningless.

Core wall time was 2163s (2170s overall). Initial CPU run: 4616 tests in 27s; per-test capture: 2075s. Schemata compilation needed nine tries, eight removing unrelated generated mutants that failed compilation; there is no changed-scope build-failed row. The wrapper observed up to four assembly test-server processes, while official concurrency remained one; it did not run four independent Stryker campaigns. Peak/mean one-minute load was 25.2/6.9. The wrapper accepted validity; no NOT VALID or infrastructure timeout is counted as a kill.

### Listed mutations and triage

The deliberate original 46-row fault list is finite. It includes A and the whole branch's key depth, walk, sampling/share/resolve, accumulation/repair, lifecycle/reset, capture, descriptor/barrier, and switch/deletion decisions. Every listed command used `nix develop --command python3 -I tools/mutate.py <list.json> <out>`. The runner alone applied, built, tested, restored, and touched mutations. No mutant was manually edited/build-tested/restored. Only the 14 affected invalid/surviving/timeout rows were rerun after test-only additions; unchanged behavior reuses valid initial results. The mechanical cleanup preserves the relevant decisions and assertions.

Initial result: 31 killed, 12 survived, one timeout, two invalid build failures. Final result: 45 killed, one equivalent; zero unresolved non-equivalent survivors, timeouts, build failures, or no-coverage rows. The non-equivalent tested fault denominator is 45, all killed; no exhaustive shader mutation score is claimed. The following exact table includes evidence paths and all dispositions.

| ID | Represented fault | Initial result | Final result | Evidence / disposition |
| --- | --- | --- | --- | --- |
| A01 | Reapply raw hit-distance rejection to surface history at rough and smooth receivers | killed | killed | AccumulatorRenderTests.IndependentSmoothReflectionDistancesDoNotRestartAnUnchangedLightsHistory; AccumulatorRenderTests.OneBrightSampleDoesNotRestartAnOtherwiseUnchangedRoughReflectionsHistory; AccumulatorRenderTests.ARejectedReflectedLocationDoesNotDiscardUnchangedLightAtTheSurfaceLocation(hitDistance: 4.5, length: 4); listed-initial/A01.test.log |
| A02 | Drop the reflected candidate hit-distance gate | killed | killed | AccumulatorRenderTests.AMirrorPixelWhoseSurfaceLeftTheLastImageKeepsItsHistoryThroughTheReflectedPoint(specular: True, roughness: 0.0500000007, hitDistance: 41, length: 1); listed-initial/A02.test.log |
| A03 | Let reflected failure destroy the surface fallback | killed | killed | AccumulatorRenderTests.IndependentSmoothReflectionDistancesDoNotRestartAnUnchangedLightsHistory; AccumulatorRenderTests.OneBrightSampleDoesNotRestartAnOtherwiseUnchangedRoughReflectionsHistory; AccumulatorRenderTests.ARejectedReflectedLocationDoesNotDiscardUnchangedLightAtTheSurfaceLocation(hitDistance: 4.5, length: 4); listed-initial/A03.test.log |
| A04 | Accept a wrong receiver normal | killed | killed | AccumulatorRenderTests.AReceiverWhoseNormalOrRoughnessChangedDoesNotKeepOldReflectedLight(turn: 0.600000024, roughness: 0.379999995); listed-initial/A04.test.log |
| A05 | Accept a wrong receiver roughness | killed | killed | AccumulatorRenderTests.AReceiverWhoseNormalOrRoughnessChangedDoesNotKeepOldReflectedLight(turn: 0, roughness: 0.0500000007); AccumulatorRenderTests.AReceiverWhoseNormalOrRoughnessChangedDoesNotKeepOldReflectedLight(turn: 0, roughness: 0.800000012); listed-initial/A05.test.log |
| A06 | Accept a newly uncovered receiver at another depth | killed | killed | AccumulatorRenderTests.AnUncoveredReceiverDoesNotInheritTheOccludersReflectedLight; listed-initial/A06.test.log |
| A07 | Lose mirror reflected-location fallback | killed | killed | AccumulatorRenderTests.AMirrorPixelWhoseSurfaceLeftTheLastImageKeepsItsHistoryThroughTheReflectedPoint(specular: True, roughness: 0.0500000007, hitDistance: 39, length: 4); AccumulatorRenderTests.AMirrorPixelWhoseSurfaceLeftTheLastImageKeepsItsHistoryThroughTheReflectedPoint(specular: True, roughness: 0.0500000007, hitDistance: 20, length: 4); AccumulatorRenderTests.AMirrorPixelWhoseSurfaceLeftTheLastImageKeepsItsHistoryThroughTheReflectedPoint(specular: True, roughness: 0.379999995, hitDistance: 20, length: 4); listed-initial/A07.test.log |
| A08 | Allow changed light to persist in slow history | killed | killed | AccumulatorRenderTests.AfterAStepOfTheSampleTheHistoryReachesNinetyPercentOfTheStepWithinTenFrames; AccumulatorRenderTests.AStepOfTheSampleShortensTheHistoryByTheClampsMoveOverHalfTheMeansLight; AccumulatorRenderTests.AChangedReflectedLightReplacesNinetyPercentOfTheOldLightWithinTenFrames(roughness: 0.379999995, before: 1, after: 0); listed-initial/A08.test.log |
| A09 | Include different-surface neighbors in current-light comparison | killed | killed | AccumulatorRenderTests.LightOnANeighboringSurfaceDoesNotChooseTheReceiversReflectionHistory; listed-initial/A09.test.log |
| W01 | Treat a ray behind a flat slab as a possible hit | build-failed | killed | ScreenWalkTests+RandomRays.OnRaysOfBinaryFractionsWhoseEntriesAndCrossingsTieWithCellEdgesTheWalkFindsTheHitOfTheMarch; ScreenWalkTests+RandomRays.AtACapOf4096TheTwoDepthWalkFindsTheHitOfAMarchOverTheFlatSlabsOnAMillionRandomRays; listed-targeted-3/W01.test.log |
| W02 | CPU child selection loses the leaving-cell rule at a solved tie | build-failed | killed | ScreenWalkTests+RandomRays.OnRaysOfBinaryFractionsWhoseEntriesAndCrossingsTieWithCellEdgesTheWalkFindsTheHitOfTheMarch; listed-targeted-3/W02.test.log |
| W03 | Shader ignores the far bound in the two-depth walk | survived | killed | ScreenTraceHardeningTests.TheShaderWalkRejectsSlabBacksAndBackfacesAndDistinguishesTheTwoSidesOfATie(channel: 0, expected: 2); listed-targeted-3/W03.test.log |
| W04 | Shader child selection loses the leaving-cell rule at a solved tie | survived | killed | ScreenTraceHardeningTests.TheShaderWalkRejectsSlabBacksAndBackfacesAndDistinguishesTheTwoSidesOfATie(channel: 2, expected: 0); listed-targeted-3/W04.test.log |
| W05 | Shader accepts the back of a surface as a hit | survived | killed | ScreenTraceHardeningTests.TheShaderWalkRejectsSlabBacksAndBackfacesAndDistinguishesTheTwoSidesOfATie(channel: 1, expected: 2); listed-targeted-3/W05.test.log |
| W06 | A hair receiver hits its own card rather than the distinct card | killed | killed | ScreenTraceRecordTests.AHairReceiversWalkPassesItsOwnCardAndHitsAnotherCard; listed-initial/W06.test.log |
| W07 | Ignore selected walk caps and stop every ray after one cell | killed | killed | ScreenTraceRenderTests.AHitFartherThanTheLargestHalfFloatStoresTheLargestHalfFloatAsItsDistance; ScreenTraceRenderTests.AMirrorFloorShowsAGlowingPanelAtThePlaceOfItsMirrorImage; ScreenTraceRenderTests.AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight; listed-initial/W07.test.log |
| P01 | Finest level stores inverse radial distance rather than inverse view depth | killed | killed | PyramidRenderTests.EachTexelOfTheFinestLevelHoldsOneOverTheViewDepthOfItsPixelsCenterAndSkyHoldsZero; listed-initial/P01.test.log |
| P02 | Coarse bounds discard the farthest child surface | killed | killed | PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 9); PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 10); PyramidRenderTests.EachDepthPyramidCellHoldsTheBoundsOfThePixelsUnderIt(level: 5); listed-initial/P02.test.log |
| P03 | CPU finest level uses plane-corner bounds rather than flat center depth | killed | killed | DepthPyramidTests.TheFinestLevelHoldsTheInverseDepthOfEachPixelsCenter; listed-initial/P03.test.log |
| P04 | Reject a valid tiny screen whose depth pyramid has no tail dispatch | survived | killed | PyramidRenderTests.ATinyScreenBuildsItsFinestDepthWithoutATail(width: 1, height: 1); PyramidRenderTests.ATinyScreenBuildsItsFinestDepthWithoutATail(width: 2, height: 1); PyramidRenderTests.ATinyScreenBuildsItsFinestDepthWithoutATail(width: 2, height: 2); listed-targeted-3/P04.test.log |
| R01 | Zero density decodes as positive | killed | killed | ScreenTraceRecordTests.DensityCodesPreserveTheirDensityAndDecodeAZeroDensityAsExactlyZero; listed-initial/R01.test.log |
| R02 | Recorded density belongs to the unquantized direction | killed | killed | ScreenTraceRenderTests.TheReflectionSampleOfAMirrorFloorHoldsTheGlowOfThePixelThatTheCSharpWalkHits; listed-initial/R02.test.log |
| R03 | Short histories share block rays before eight valid frames | killed | killed | ScreenTraceRenderTests.AfterItsHistoryFillsAPixelInsideASurfaceSharesItsBlocksDiffuseRay; ScreenTraceRenderTests.AMirrorFloorShowsAGlowingPanelAtThePlaceOfItsMirrorImage; ScreenTraceRenderTests.TheReflectionSampleOfAMirrorFloorHoldsTheGlowOfThePixelThatTheCSharpWalkHits; listed-initial/R03.test.log |
| R04 | Converged coplanar pixels never share block rays | killed | killed | ScreenTraceRenderTests.AfterItsHistoryFillsAPixelInsideASurfaceSharesItsBlocksDiffuseRay; listed-initial/R04.test.log |
| R05 | Resolve uses a block record where the pixel traced an own ray | killed | killed | ScreenTraceRenderTests.AMirrorFloorShowsAGlowingPanelAtThePlaceOfItsMirrorImage; ScreenTraceRenderTests.TheReflectionSampleOfAMirrorFloorHoldsTheGlowOfThePixelThatTheCSharpWalkHits; ScreenTraceRenderTests.TheCompositeShowsTheTracersReflectionAndWithoutTheTracerTheClosedForm(tracer: True); listed-initial/R05.test.log |
| R06 | Diffuse resolve loses half the incident-light estimate | killed | killed | ScreenTraceRenderTests.UnderAUniformEnvironmentWithNoBlockerBothIndirectSignalsConvergeToItsLight; ScreenTraceRenderTests.AGlowingPanelBesideAFloorPixelTakesItsShareOfThePixelsDiffuseLight; listed-initial/R06.test.log |
| R07 | Rough surfaces lose the specular estimate from diffuse rays | survived | killed | ScreenTraceRenderTests.ASurfaceAboveTheReflectionLimitReceivesSpecularLightFromItsDiffuseRays; listed-targeted-3/R07.test.log |
| R08 | Double count emission when the hit entity already holds a light | killed | killed | ScreenTraceRenderTests.AMirrorFloorShowsNoGlowOfAPanelWhoseEntityHoldsALight; ScreenTraceRenderTests.AnImpostorsGlowReachesAMirrorOnlyWhenItsEntityHoldsNoLight(holdsLight: True); listed-initial/R08.test.log |
| R09 | Drop previous indirect light and later bounces | killed | killed | ScreenTraceRenderTests.InAClosedBoxTheDiffuseLightRisesWithNoOvershootTowardTheSumOfTheBounces; listed-initial/R09.test.log |
| R10 | Remove diffuse half-float overflow cap; Mac saturation may mask fault | survived | killed | ScreenTraceHardeningTests.TheDiffuseResolveCapsAWeightedRayBeforeHalfFloatStorage; listed-targeted-3/R10.test.log |
| R11 | Remove finite distance cap; Mac saturation may mask fault | survived | killed | ScreenTraceHardeningTests.ARecordedHitCapsItsDistanceBeforeHalfFloatStorage; listed-targeted-3/R11.test.log |
| R12 | Bypass the replacement tracer and always use the closed form | killed | killed | ScreenTraceRenderTests.TheCompositeShowsTheTracersReflectionAndWithoutTheTracerTheClosedForm(tracer: True); listed-initial/R12.test.log |
| R13 | Share block rays across a change of geometric normal | survived | killed | ScreenTraceHardeningTests.AConvergedPixelKeepsItsOwnRayAcrossAnIncompatibleSurface(normalBreak: True); listed-targeted-3/R13.test.log |
| R14 | Share block rays across a receiver-plane depth break | survived | killed | ScreenTraceHardeningTests.AConvergedPixelKeepsItsOwnRayAcrossAnIncompatibleSurface(normalBreak: False); listed-targeted-3/R14.test.log |
| T01 | Disable same-surface short-history repair | survived | killed | AccumulatorRenderTests.AShortHistoryRepairsAnIsolatedSampleFromItsSameSurfaceNeighbors; listed-targeted-3/T01.test.log |
| T02 | Treat NaN as valid sample light | killed | killed | AccumulatorRenderTests.WhenTheExposureMovesBetweenTwoFramesTheHistoryTakesTheNewExposureInThatFrame; AccumulatorRenderTests.APixelWithoutASampleAndWithoutAHistoryTakesTheEnvironmentsClosedForm(specular: False); AccumulatorRenderTests.APixelWithoutASampleAndWithoutAHistoryTakesTheEnvironmentsClosedForm(specular: True); listed-initial/T02.test.log |
| T03 | Changed light does not shorten slow history by its lag | killed | killed | AccumulatorRenderTests.AStepOfTheSampleShortensTheHistoryByTheClampsMoveOverHalfTheMeansLight; listed-initial/T03.test.log |
| L01 | Headless tracer stills skip history warm-up | killed | killed | StillRuleTests.AStillUnderTheTracerTakesTheWarmUpCountOfTheOptions(automaticExposure: True); StillRuleTests.AStillUnderTheTracerTakesTheWarmUpCountOfTheOptions(automaticExposure: False); ScreenTraceRenderTests.AStillUnderTheTracerDrawsItsWarmUpDrawsAndThenTheShownDraw(warmUpFrames: 3); listed-initial/L01.test.log |
| L02 | Keep stale own-ray dispatch counts from the previous frame | timeout | killed | OwnRayResetRenderTests.ASkyFrameClearsTheOwnRayCountsOfThePreviousSurfaceFrame; listed-lifecycle-final/L02.test.log |
| L03 | Read current rather than previous fast history | killed | killed | AccumulatorRenderTests.AfterAStepOfTheSampleTheHistoryReachesNinetyPercentOfTheStepWithinTenFrames; AccumulatorRenderTests.TheSpecularSignalsCapFollowsTheRoughness(roughness: 0.400000006, cap: 18); AccumulatorRenderTests.TheSpecularSignalsCapFollowsTheRoughness(roughness: 0.800000012, cap: 32); listed-initial/L03.test.log |
| L04 | FinishCaptures drops remaining captures in flight | killed | killed | CapturesInFlightTests.FinishCapturesHandsOverEachCaptureInFlightOnceInTheOrderDrawn; CapturesInFlightTests.AtEachHandOverTheRenderersReadingsAreThoseThatACaptureThatWaitsSees; CapturesInFlightTests.ACaptureInFlightReachesItsSinkAtFinishCapturesWithThePixelsOfACaptureThatWaits; listed-initial/L04.test.log |
| D01 | Omit a storage-image descriptor | killed | killed | DescriptorLayoutsTests.TheLayoutsNeedOfAStageTenStorageBuffersFifteenStorageImagesAndSixteenSampledImages; listed-initial/D01.test.log |
| D02 | Drop indirect-command visibility declaration | survived | equivalent (survived) | Equivalent in the current call graph: ScreenTraceRaysPass declares BOTH lists ComputeIndirectRead before Trace; BarrierBatch.Buffer retains the earlier read stage/access union. The mutant removes only Trace's redundant declaration, not indirect visibility. |
| H01 | In-flight override loses selected frame count | killed | killed | ScriptCommandOptionsTests.AnExplicitInFlightCountReplacesAThreeFrameRenderer(count: 2, captures: True); listed-initial/H01.test.log |
| H02 | Screen-trace off leaves tracer on | survived | killed | ScriptCommandCaptureTests.TheScreenTraceOptionTurnsTheHeadlessRenderersTracerOffAndOnKeepsTheRenderersDefault(screenTrace: ["--screen-trace", "off"], off: True); listed-targeted-3/H02.test.log |
| H03 | Playback does not drain remaining captures before output | killed | killed | ScriptCommandCaptureTests.AFilmWithTwoFramesInFlightWritesTheImagesAndTheReadingsOfAFilmThatWaitsForEachFrame; ScriptCommandCaptureTests.TheInFlightOptionSetsTheRenderersFrameCountAndWhetherCapturesWait(inFlight: ["--in-flight", "2"], kept: True, frames: 2); listed-initial/H03.test.log |

Final: 46 faults, 45 killed, 1 equivalent. No unresolved survivor, timeout, build-failed or no-coverage result is counted as successful evidence.

#### Invalid and missing-evidence attempts retained

- Initial W01/W02 replacements failed compilation under IDE0060 because they removed parameter uses. These were not kills. Corrected viable replacements preserve the parameter uses; the wildcard class filter includes the nested million-ray and exact-binary-tie CPU tests. Both corrected faults are killed.
- Initial H02 selected the wrong Host class. The corrected filter selects ScriptCommandCaptureTests and is killed by the existing CLI off behavior test. No new production or CLI behavior was needed.
- Initial L02 produced an IndexOutOfRange failure after stale own-ray counts overflowed a list, then its broad test host timed out. That timeout is missing evidence, not a clean kill. The bounded populated-surface then sky test is the final evidence; its runner log supplies the exact result. The first targeted rerun still included many-frame sharing tests in the same class and timed out again. Moving the two-frame lifecycle test to its own class eliminated the overflow/hang and produced a clean assertion failure in 6.9 seconds.
- listed-targeted stopped at a baseline with invalid fixture premises: the normal break coincided with a 2x2 block boundary, and quantized roughness 0.5 exceeded the reflection limit. No mutants ran. The joined plane now meets the actual front wall, and the wall uses roughness 0.3.
- listed-targeted-2 stopped at a proposed roughness-1 white-furnace assertion: the existing normal-mapped floor returned mean specular 0.4494953 for input 0.5, outside a proposed 1% unit-mean tolerance. This is not accepted energy-accuracy evidence. The final additional test checks the narrower hand-over decision: an above-limit surface retains a nonzero specular estimate (within half to one-and-a-half times input). The original roughness-0.3 1% convergence test is unchanged. No estimator or limit was retuned.
- Parent-required tools/cleanup.sh ran on all 76 ticket-touched C# files after mutation restoration, before the final freeze. Production differences are get-only auto-property substitutions, primary-constructor syntax, an equivalent negated pattern and formatting. The existing mutation decisions/assertions remain equivalent; their valid evidence is reused rather than rerunning all faults for whitespace. cleanup-production.diff preserves the exact six-file production delta. No production policy changed.

Detailed replacements, classes, timings and failure logs remain in mutants-initial.json, mutants.json, listed-initial/results.json and listed-targeted-3/results.json. The 46 rows are finite fault-based evidence, not a shader coverage percentage or proof of all possible faults.

### Visual acceptance, portability, and limits

- The parent accepts A as the narrow receiving-surface history correction after inspecting matched fixed-exposure front/deck stills and the after low/moving views. This is not a claim that all grain is fixed. `/private/tmp/gayt-history-a/manifest.md` and `/private/tmp/gayt-history-diagnosis/report.md` preserve the visual evidence. No new captures or policy changes were requested by the final steering, so the existing bounded evidence was reused.
- The actual roughness-0.38 deck's recognizable-lamp/reflection/noise criterion is still unmet. The approximately 0.67-second motion sequence does not establish long-motion trail absence. `.tickets/ter-icn3.md`, dependent on this ticket, retains those acceptance items, changed-light response, and the independent denoising investigation. Merging A must not erase them. Denoising and light-budget work do not gate A under the user's decision.
- A proposed new roughness-1 furnace fixture produced mean specular 0.4494953 for uniform input 0.5 on the existing normal-mapped floor, not the proposed 1% accuracy. It is not accepted energy-accuracy evidence. The final narrower regression only proves that above-reflection-limit surfaces retain a specular estimate from diffuse rays, bounded between half and one-and-a-half times the input. The existing roughness-0.3 1% convergence test is unchanged. No estimator or limit was retuned, and no new production defect was established from that fixture.
- `portability-assessment.md` checks Vulkan indirect-command stage/access unions, transfer clearing, image declarations, descriptor counts/limits, explicit storage formats/features, and finite half-float bounds against pinned official Khronos Vulkan-Docs v1.3.296 source chapters. No source-level Vulkan correctness or portability blocker was found by this bounded assessment. This cannot exclude driver-specific failures. Linux runtime remains explicitly deferred under `ter-cgif`, dependent on `ter-s7c6` and `ter-kzj3`, as the user decided. No Linux, lavapipe, RADV, ANV, or NVIDIA runtime check ran here.
- CPU coverage is not shader coverage. The finite GLSL fault probes are not exhaustive shader coverage. No screenshot supplies GPU timing. Prior measured timings, rare-emitter limitations (`ter-18hc`), earlier red/green evidence, and detailed notes remain intact. The original eight-part historical TDD logs were not independently reconstructed; current green is not proof of historical red. No extended soak, quantified reliability, universal long-motion acceptance, or all-noise-fixed claim is made.

### Evidence locations and handoff

The bounded external evidence root is `/private/tmp/tertius-hardening-20261105/ter-gayt`. It includes `evidence-ledger.md`, `coverage-review.md`, `coverage-final/`, `final-mutant-outcomes.md/json`, `listed-initial/`, `listed-targeted-3/`, `listed-lifecycle-final/`, `stryker-final/`, `portability-assessment.md`, `cleanup-production.diff`, `final-validation/`, and `final-state.json`. Invalid baselines and timeouts remain preserved separately and are not passed off as kills. Disk remained about 13 GB free after the final suites. Processes are completed before handoff; no watchers or renderer runs are left behind. Parent receives one ordinary queued completion event, not forbidden workflow-service delivery.

**2026-11-05T15:10:14Z**

2026-11-05 parent integration validation: exact delivered 28334ae49851837bb564c04c5025174063a2902d is normally merged with main 64ab730a, retaining main Entity identity and single-renderer/frame-span timing tests, one frame slot and explicit NoScreenTrace isolation. Both CLI gpu-run and in-flight options and all root ticket-note paragraphs are preserved. Seven conflicts were reconciled; no tracer/history policy or denoising change was introduced. Combined canonical code/test/build-input tree 92b75137580e0893295b3ac77dd51e8027a9b4b7 passed Checked 9,945 and Release 9,932 tests, zero failures and 20 declared skips each. All 16 full-suite records match that tree. Both complete solution builds include benchmarks and pass with zero warnings/errors. Focused Rendering passes 175 with one existing driver skip. Scoped cleanup, shell syntax and full code whitespace checks pass. Matched root-engine native 1280x720 deck and reflection-front captures are byte-identical to the previously inspected Rule A references: deck b0897b3d541435f9c3518536579ba7e6b3ba520f2f7fa5a92f0079ac69892d3b, front ea2709b7ea448fae118293c0527f4593c7da465cb77bc27076b06c69fc3a2210. Both were visually inspected; grain and speckles remain, not a merge regression or accepted noise cure. Final validator coverage/mutation evidence is retained without rerunning the unchanged policies. Evidence: /private/tmp/tertius-integration-20261105/merge-gayt/. Earlier failed cleanup/default-build attempts are preserved as unsuccessful attempts; successful builds used -m:1 -nr:false. ter-icn3 retains actual-deck quality, identifiable reflections and long-motion acceptance. Linux runtime remains user-deferred under ter-cgif. Normal merge/closure commits identify their own integration state; this note does not authorize denoising acceptance.

**2026-11-05T15:12:00Z**

2026-11-05 integrated by normal root-main merge 13117f6a491b78bc625a2231d6fdd027e41c84a2, whose second parent is exact validated 28334ae49851837bb564c04c5025174063a2902d. Closed only after that merge. This closure adds ticket prose only and preserves tested combined tree 92b75137580e0893295b3ac77dd51e8027a9b4b7; prior final suites and captures are reused. Parent release: the preserved tracer checkout no longer has a validation/integration tracked-write freeze, but no diagnostic/denoising restart or new production work is authorized by this notice. Integration hook findings were inspected: Playback.FinishCaptures exposes completion on the playback boundary, not the renderer; descriptor-layout tuple/statement groups remain internal to their existing owner; the two unused roughness constants do not participate in any retained-light state. Length and ScreenRay CC12 warnings concern already-reviewed mechanisms, not new merge logic. No new ownership/correctness defect was established, and speculative structural cleanup was not placed ahead of this independently accepted merge. Broader visual quality remains open under ter-icn3; Linux runtime remains deferred under ter-cgif.
