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
