# First displayed frames after camera cuts

The latest [pristine Linux measurement](pristine-linux-2026-11-28/README.md) completed and passed the reviewed analyzer. The candidate's first frame was correct in all eight real-cut observations; the baseline's first frame was incorrect in all eight. Each cut used one draw and one presentation. Reversed rounds measured an ordinary GPU median increase of 0.220288 to 0.228160 ms in native mode and 0.089504 to 0.092320 ms in FSR mode. Complete raw evidence returned with zero collector skips. Canonical counted cost was $0.1217736. Original policy bytes and both endpoint defaults are verified restored. The parent retains overall acceptance, PR readiness, required CI and metrics, and merge.

The earlier [bounded pristine Linux attempt](bounded-pristine-linux-2026-11-28/README.md) failed before measurement because its encoded command exceeded Linux argument limits. Its canonical counted cost was $0.0124664. The original 840/900-second policy and both endpoint defaults were verified restored. No measurement archive returned from that attempt. Its paid approval was consumed.

The [corrected Design A Linux run](design-a-linux-2026-11-28/README.md) ended after validating diagnostics and pristine rebuilds. Its capacity check stopped both matched timing rounds. The synthetic FSR destination references also disagree between trees. Linux acceptance remains HOLD. The corrected attempt counted $0.09488624, separately from the earlier $0.05953728 attempt. PR #32 stays draft and In Progress for the parent's decision. F1 is resolved; no further review or paid retry is authorized.

The capture and hardening campaign below records the earlier cf84 source. The user-selected 32-bit scene-radiance implementation has separate [Design A milestone evidence](design-a-2026-11-28/README.md). The earlier 201 captures and old glossy exposure-conversion mutants do not establish acceptance of that new path.

The outside cut at simulation tick 1417 and the return cut at tick 1462 use the destination exposure in their first displayed frame. Each cut shades the destination once and presents once. The controls reach each destination independently and repeat eight captures without advancing the simulation.

## Real game captures

The public `ScriptRunner` and `HeadlessPlayback` run the real `demo-surface` scene with its cooked content, Jolt physics and Vulkan renderer on an Apple M1 Max. Each mode records 49 consecutive film frames, plus nine captures of each control: 201 captures across nine runs. Every run enables Vulkan validation. All nine runs passed with zero validation messages.

The output is 1280 × 720 pixels. Native and window-clock rendering shade 1280 × 720 pixels. FSR shades an observed active rectangle of 848 × 477 pixels. The window-clock case uses an offscreen presentation target; it does not claim desktop-window event-loop coverage. These Mac captures establish correctness and appearance, not Linux performance.

| Renderer clock | Cut tick | First exposure (stops) | Settled exposure (stops) | Absolute difference (stops) | Scene draws / presentations |
|---|---:|---:|---:|---:|---:|
| native | 1417 | -7.6409545 | -7.6409683 | 0.0000138 | 1 / 1 |
| native | 1462 | 2.8734600 | 2.8734600 | 0.0000000 | 1 / 1 |
| fsr | 1417 | -7.6406430 | -7.6406800 | 0.0000370 | 1 / 1 |
| fsr | 1462 | 2.8750825 | 2.8750825 | 0.0000000 | 1 / 1 |
| window | 1417 | -7.6409545 | -7.6423400 | 0.0013855 | 1 / 1 |
| window | 1462 | 2.8734608 | 2.8734420 | 0.0000188 | 1 / 1 |

A stop is a base-two exposure unit: adding one stop doubles the exposure multiplier. The last two control captures differ by at most 0.0000800 stops. The first frame's exposure differs from its own histogram target by at most 0.0000005 stops. All six comparisons have identical center RGB values in the first frame and the settled control. Across all RGB channels and pixels, their mean absolute differences range from 0.0028801 to 0.5562403 on the 0 to 255 sRGB code scale. Temporal filtering can change those pixels while the destination exposure stays correct.

The native first frames and both native controls were opened and inspected. The FSR and window-clock first frames were also inspected. The shuttle exterior stays visible on the first outside frame. The return frame shows the corridor's floor, rails and walls; the bright opening remains bright in its settled control.

## Coverage and mutation evidence

The combined Checked and Release focused suites each passed 243 tests and skipped five opt-in timing tests. The Simulation interpolation suite passed 21 tests. The union of the collected coverage reports covers all 36 instrumented changed C# lines and all 26 changed branches relative to the integrated source parent `f97afc5663c5406f92307f4ff93a37e5fd532a93`. Rendering contributes 33 covered lines and Simulation contributes three. The JSON retains each file's counts. Payload declarations, comments and shader execution are not C# instrumented lines; this is not a shader line-coverage claim.

The initial scripted Rendering run killed 36 of 40 selected mutants. Two survived, and the compiler rejected two. Two new GPU behavior tests kill the survivors: retained accumulation after a cut and the tracer's conversion of a retained reflection. Both tests pass on normal code and fail on their corresponding mutants. The compiler-rejected mutations were replaced with equivalent mistakes that compile and were killed. The final table has 40 killed mutants, zero survivors, zero timeouts and zero no-coverage results.

Early regression-fixture iterations failed before any mutant ran: the first reached an exposure limit, the second had no retained reflection, the third used a transfer layout unsupported by that history image, and a stale reference caused a compile failure during its correction. These are unscored fixture failures. The final reflection fixture uses the existing sample producer and accumulator, and passes Vulkan validation. Assertions and production behavior were not weakened.

Both completed mutation runs restored their input bytes, executable modes, semantic index, HEAD and merge state. Each rebuilt the normal test assembly. Independent restoration checks passed. The source parent retains the final 128-to-256 bloom response and all 16 ambient source tests. This stage also removes a redundant camera-cut predicate wrapper and corrects the architecture table's old gradual-cut exposure description.

## Evidence and remaining work

[The evidence manifest](hardening-2026-11-27.json) records runtime source hashes, every final mutant, per-file coverage, per-frame comparison values, capture hashes and the external artifact folder. The artifact folder holds the original baseline and mutant logs, exact histograms, scene radiance readings, all 201 PNG captures, test logs and real shell exit codes.

The official changed-line Stryker cloud run completed on `cf84f02bc5fabfedb22c2b17835ae0d194c4cb91` against `d15fdf702e29a891fd298c30e8c9f0deac518a15`, with exit code 0 and no validity problems or Dubious tests. Core's changed lines are XML documentation. Simulation produced three no-mutant rows because Stryker's mutations failed to compile. No executable Stryker mutant ran, so this supplies no mutation score.

Each Simulation row has a compilable replacement, run through `tools/mutate.py` against the existing interpolation tests. All three replacements were killed. Two initial replacements triggered the unused-field analyzer and are unscored build failures. Both scripted runs restored source bytes, modes, the semantic index and commit identity, then rebuilt normal assemblies. The [unified ledger](official-hardening-2026-11-28.json) holds the three official classifications and all 43 final scripted kills: 40 Rendering and three Simulation, with no survivors or timeouts.

The current-main integration retains the analytic raw meter and its adaptation write dependency. The legacy meter's exposure read is unnecessary because the analytic meter reads no exposure value. The imported synchronization test now observes adaptation's compute read/write dependency and checks the raw-scene histogram; its old exposure-reading expectation was first observed failing on the analytic source. The added ambient-reflection, impostor and volume source regressions are retained. All five tests in those four imported test classes passed in Checked, with no skips or failures. No production source bytes change from the measured and captured `cf84` candidate.

The external Linux harness compiled against both immutable API inputs and passed native/FSR batch smokes, actual two-presentation attribution, and the public outside cut at tick 1417. Its per-presentation observer records applied exposure and center RGB even for hidden draws. Ordinary timing disables those observation copies. Diagnostic source restoration and pristine rebuilds passed. These are Mac harness checks, with no Linux performance claim.

The sole approved Linux job ended `FAILED` during baseline diagnostics. It reported three Vulkan allocations larger than their selected memory heap, before candidate diagnostics or either matched round. The [terminal evidence](linux-2026-11-28/README.md) preserves the returned files, approved harness, provenance and canonical audit. Execution took 302.108 seconds. The canonical audit counted $0.05953728 against the approved maximum of $0.1552. No retry ran.

That earlier run left Linux acceptance on hold. [Issue #35](https://github.com/alexispurslane/tertius/issues/35) records the allocation defect, since fixed and merged. Its incomplete result supplies no request-to-fence-complete offscreen cut latency, ordinary unobserved GPU cost or shared correctness conclusion. The corrected Design A run linked above records the current result. The sole review's F1 finding is resolved. The draft pull request stays In Progress for the parent's decision, CI and merge.
