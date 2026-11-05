---
id: ter-dzxf
status: closed
deps: [ter-sftw]
links: [ter-gayt]
created: 2026-11-04T01:51:17Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# Hill-climb the screen tracer's walk kernel on a fixed harness

The calibration spike (ter-3ug8, report docs/research/temporal-renderer/gpu-calibration.md on branch ter-3ug8-gpu-calibration until it merges) measured the lean walk at 2.3 to 2.7 times what the machine predicts: V5c took 2.67 to 2.81 ms on the deck at 1280 x 720 and 800 MHz, against a prediction of 1.05 ms. A rolled loop of dependent multiply-adds reaches only 0.41 of the FP32 peak, in native Metal and through MoltenVK alike. The walk serves both reflections and many-bounce light (ter-gayt), so it holds the largest known slack in our own code. The user chose on 2026-11-03 to climb it with a GPT 6.1 SOL agent.

Scope:
1. A harness that runs the walk kernel alone on fixed inputs: capture the walk's inputs once for the deck and floor views of lean-trace-spike.md (depth pyramid, normals, ray origins and directions), and record the current walk's hits as the reference.
2. A correctness gate, fixed before the climb starts: a variant must give the same hits as the reference, or stay within a tolerance that the ticket states.
3. An objective that the clock does not distort: GPU time per ray with dispatches batched so that the GPU reaches its top clock, gpu-clock (tools/gpu-calibration) beside each measurement, variants compared only at the same clock, under the GPU lock.
4. The climb: one change per step (for example an unrolled step loop, fewer live registers, subgroup-coherent stepping, the depth pyramid's layout), kept only if it passes the gate and is faster than the spread of the runs. A log of every attempt with its result.
5. A report: the best variant, its time against the machine's prediction, the log, and the change that ter-gayt takes over.

It starts after ter-sftw, because the profiling spike measures where the walk's time goes and tests the two-depth walk, and its answer picks the kernel that the climb starts from.


## Notes

**2026-11-04T03:25:10Z**

2026-11-03 (ter-3ug8's review fix): ter-scm7, "Unroll the lean walk's step loop two to four times, and measure it at a recorded clock", is folded into this ticket, whose step 4 already names an unrolled step loop. Its data, now from the variants of the native FMA probe (probes fma-variants, 7 rounds, docs/research/temporal-renderer/gpu-calibration.md, "The variants of the FMA probe"): with 4 float4 chains, 1, 2, 4 and 8 steps per pass of the loop reach 0.41, 0.77, 0.93 and 0.97 of the FP32 peak. A rolled loop with 2 to 16 float4 chains stays at 0.37 to 0.45, so more chains do not help. The first run's figures of 0.58, about 0.80 and 0.84 came from scratch kernels without logs and are withdrawn. The unrolled loop must not cost time on other Vulkan drivers. The lean walk V5c takes 2.4 to 3.3 times an estimate of its work at the rolled loop's rate (40 operations per step, ter-sftw's estimate). Time each variant at the clock over its timed span (ter-qk78).


**2026-11-04T12:20:46Z**

2026-11-04: with the user's 12 ms target (ter-89xq's note), the two trace dispatches get 1.36 to 2.05 ms if the rest of the frame stays as estimated, against V6's 2.92 to 3.16 ms at 10 % own rays at the windowed clock. So the climb's goal is a cut of about 35 to 55 % in the dispatches, at the correctness gate. Candidates named so far: the unrolled step loop (the calibration probe went from 41 % to 77 to 93 % of the FP32 peak), the 0.85 ms of our own code in V1's walk whose cause is unknown, GLSL forms that MoltenVK translates better (the words form recovered the record reads), persistent lanes, and whatever the profiling continuation leaves open of the level 0 layout and the diffuse cap with V6.

**2026-11-04T17:28:07Z**

2026-11-04: started as a workflow, run 19acc6a3-4a97-4598-94eb-a12c17619eca (version 869b0b07), builder GPT 6.1 SOL at xhigh, branch ter-dzxf-walk-hill-climb from the profiling spike's 3c9fda2a3. Settled: start from V6 for both kinds at the diffuse cap of 64; the harness runs V6's real walk on the spike's dumps; the gate is the same hit texel and kind on every ray; objective at the top clock with gpu-clock; every kept change portable with its reason; the target comes from the budget recalculation (running).

**2026-11-04T17:30:08Z**

2026-11-04: the budget recalculation (ter-gayt's note of today) gives the two dispatches 2.94 to 3.76 ms under 12.0 ms on the deck. V6 at 10 % own rays takes 3.90 to 3.93 ms there, so the climb's goal is a cut of about 4 to 25 %, not 35 to 55 %.

**2026-11-04T18:24:14Z**

2026-11-04: the user widened the mandate ("spare no expense": unrolling, hard-coded and pre-calculated values, a Mac-only Metal path chosen by platform, papers, anything). Run 19acc6a3 was stopped only to widen the brief; its harness (tools/walk-hill-climb, uncommitted) and logs (/private/tmp/ter-dzxf.vv9W25) stay. Continuation run 24378142-987f-40a2-89ed-2bfd0c891caa (version 869b0b07, GPT 6.1 SOL at xhigh) on the same worktree, diffuse cap 62 by the user's decision. Settled: the gate stays for the main line; approximations are a second track reported as options with tile bias and FLIP; the SPIR-V path stays the portable reference; a Mac-only path must pass the same gate with the platform choice in one place, and its adoption is the user's fork. A survey researcher (GPT 6.1 SOL, xhigh) writes /private/tmp/dzxf-survey.md for the climber.

**2026-11-04, continuation: harness checkpoint**

The bound checkout is ter-dzxf-walk-hill-climb at 3c9fda2a35bb3f3154751a46efd09e2448acd205. Its ticket file was absent at that base. This copy preserves the current notes from the read-only main Git object. No other checkout changed. The earlier pilot passes against the trace's outcomes on all 3,038,431 active rays of the four V6 dumps. The continuation fixes an overstrict gate: identical hits can use different step counts. Three red tests establish that rule and the cap-62 reference derivation, then all 18 Python tests pass. Raw red and green logs stay under /private/tmp/ter-dzxf.vv9W25. The real extracted V6 baseline takes 3.442826 ms on the first fresh deck-10 run at 1290.3 MHz. This is below the profiling trace's 3.920–3.928 ms range, so the harness-to-trace gap is real and must stay explicit in the report. Further baseline rounds and experiments follow this checkpoint. Coverage and mutation testing remain with the orchestrator.

### 2026-11-04: measured continuation

The report is docs/research/temporal-renderer/walk-hill-climb.md; tools/walk-hill-climb/results holds all 224 completed runs, 100 comparison groups and 55 generated forms, including rejected runs and explicit decisions. The real V6 source and fixed hit-identity gate were preserved before climbing. Final portable and native candidates pass all 3,038,431 active rays on the four V6 dumps, both diffuse caps 62 and 64, in three alternating rounds at clocks within 3% of 1296 MHz. The extracted baseline is 10.8–12.3% faster than the saved whole trace, outside spread; this gap is reported rather than treated as a match or hidden in a speedup claim.

At cap 62, the fastest ordinary GLSL takes 2.533515 ms on deck-10 against 3.421760 ms for the extracted baseline (25.96% less), with nonoverlapping kept ranges. Across all four views its gain is 25.6–27.5%. It combines one RG32F pyramid, Boolean bounds/loop predicates and a sign-only facing normal. The one-image format adds level-zero bandwidth and about 12.48 MiB of allocation here, so it is not an unconditional Linux default. The conservative portable handoff to ter-gayt is specifically bounds-predicate+loop-predicate+facing with the original two-image layout, reflection cap 256 and diffuse cap 62: 2.673525 ms against 3.429224 ms on the independent deck-10 confirmation, and 21.8–23.6% less across all views. The report gives both caps, all ranges/clocks, portability reasons, the calibrated model, every failed attempt, and the exact handoff. No engine code changes.

Explicit loop expansion, including fresh cap-62 checks, loses time. A final review catches an unused split-cap+cap62 probe selecting 256 diffuse steps: the every-ray test first fails on 18,699 outcomes, then passes all 606,080 deck-10 rays after the repair. The repaired probe remains slower and is dropped. Offline length sorting loses; an oracle with linear slots saves only about 0.19 ms before all sorting/scatter costs, so no scheduler is adopted. Cap 54 remains a separate approximation option with the existing top-clock whole-trace times, signed/absolute tile bias and 24 FLIP comparisons against V0 and V1; it does not replace the user's cap 62.

Fork: whether to adopt the Mac-only native source path through MoltenVK's unsupported private shader-module format and deprecated ICD workgroup setter. It passes the same all-ray gate and gains another 0.032–0.062 ms over fastest GLSL, but adds 224 shader/helper/test lines plus host changes, a second shader and fragile ABI upkeep. Platform selection has one owner and non-Mac fallback is tested. Offline Metal compilation is unavailable because the Metal Toolchain is missing; runtime compilation works. Recommendation: take portable changes first, leave native adoption to the user. No engine integration proceeds through this fork. done=false.

Checks, all foreground in the Nix shell: 23 Python tests pass; all 55 current GLSL forms compile and pass spirv-val, with measured portable binary hashes matching. Full Checked solution: 10,160 passed, 18 skipped, zero failures. Full Release solution: 10,146 passed, 18 skipped, one failure in the unchanged GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt (0.078917 ms against 0.05 ms). A focused Release run under the main GPU-lock helper also fails that test (0.075667 ms; 12 passed, one skipped). This is the existing open ter-abun issue; it is reported, not fixed or called green. No snapshot fails. Coverage and mutation testing remain with the orchestrator. Every owned process finishes before the stage result. Retained scratch: about 2.6 GiB under /private/tmp/ter-dzxf.vv9W25 and 1.8 MiB under /private/tmp/ter-dzxf-reading.7YZF56; about 19 GiB free. The report and records are committed on the ticket branch; no merge, ticket closure or PR publication.

**2026-11-04T20:39:50Z**

2026-11-04: closed, report only. The report docs/research/temporal-renderer/walk-hill-climb.md and its records in docs/research/temporal-renderer/walk-hill-climb/ went to main in the close commit. The harness and the native experiment stay on branch ter-dzxf-walk-hill-climb. The user chose portable only (no Metal path). The walk changes go into the engine in ter-rgg9 after ter-gayt, and the one-image pyramid is measured in ter-y3k4.


## Port note from ter-gayt, 2026-11-04

The engine port keeps V6's slab tests but replaces its 0.005 px position nudge with `childAt` and `solved` cell tracking. Commit `17ccadd9c` records 978 skipped hits among 1,000,000 random rays with the nudge. Preserve that correctness change when porting the winning walk. The winning changes belong to ter-rgg9 after ter-gayt, not to this fix stage. The ter-gayt fix report records a comparison of mean cell counts, not a new GPU timing claim.

That comparison used 1,000,000 random rays at seed 20261104, a 128 × 96 room and cap 4,096: the port averaged 10.470271 cells and nudged V6 10.454608, about 0.15 % fewer. The port matched the flat-slab march on every ray. This is not the spike's deck input or a measurement of GPU time. Evidence: `/private/tmp/gayt-fix/walk-steps.log` and `ScreenWalkTests.RandomRays`.
