# First displayed frames after camera cuts

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

Official changed-line Stryker work for Core and Simulation is still pending. Core's seam diff contains documentation changes, while Simulation contains executable changes; the official tool must classify its rows. This report does not declare that run inapplicable. Matched Linux measurements and a fresh read-only review of the final combined head also remain. The draft pull request stays In Progress for those stages and parent-owned CI and merge.
