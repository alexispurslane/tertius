# Design A hardening and final game captures

The glossy path keeps 32-bit linear scene radiance, in W/(m² sr), through producers, ray records, samples, both fast histories, slow history, scratch, filtering, sharing and composition. Camera exposure applies downstream. The supported estimator weights and geometric metadata keep their existing bounds.

## Hardening

The frozen implementation retains the 258 previously passing focused checks across original, precise-capped and precise-uncapped modes. The new changing-radiance parity regression passes three cases, one per mode. A scoped coverage run passed 153 tests. Supplementary resize and allocator coverage passed 58 tests. The restored parity run passed all three cases. Every reported passing run has zero failures and zero skips.

The first scripted pass killed 15 of 20 selected faults. One parity fault survived and four faults did not compile. The new parity regression kills the survivor in all modes: the faulty second draw gives 1.5 × 10^20 W/(m² sr) against the hand-calculated mean 2 × 10^20. Compilable replacements kill the four rejected faults. The final set has 20 valid faults, 20 kills, zero survivors, zero timeouts, zero no-coverage outcomes and zero compiler failures. No equivalent mutant or testing judgment remains among those 20 initial faults.

Both tools/mutate.py runs checked their baseline groups, restored exact source bytes and modes, retained HEAD/index/merge metadata, and rebuilt the normal assembly. Independent source hashes match. An exact post-restoration method filter selected zero tests; the corrected wildcard filter ran all three cases successfully. The cleanup tool later expanded one test variable to its exact named tuple type. Its assertions stayed unchanged, the affected test passed, and production hashes stayed unchanged.

The follow-up coverage reports cover 65/66 changed instrumented C# lines and 35/36 changed branches. The scope is the staged implementation and incoming allocator fix against recovery HEAD 468dc19d0f5e247819f24bfaea297919439c7b4f. The constructor cleanup is now covered by real-device failures after the first fallback allocation and after both fallback allocations. Both cases assert the allocator's live allocation count returns to its starting value, check its invariants, and require clean Vulkan validation. Removing either disposal fails those checks through tools/mutate.py. The current set therefore has 22 valid faults, 22 kills, zero survivors, zero timeouts, zero no-coverage outcomes and zero compiler failures.

Only TemporalSignal.CheckDevice's unsupported-format refusal remains uncovered. Vulkan's required optimal-tiling format table supplies storage-image and color-attachment support for R32G32B32A32_SFLOAT. The real device reports those capabilities. Reaching the refusal would require a nonconforming device or a production capability-mocking API. This guard retains its concrete defensive role without such an API. The [Vulkan required-format table](https://registry.khronos.org/vulkan/specs/latest/html/vkspec.html#features-required-format-support) defines that support.

The first ordinary commit hook failed: 3,807 total, 3,793 passed, two failed and 12 skipped. Its complete original log and hash remain in the follow-up archive. No commit occurred. The still-surface probe retained a depth-ratio error of 0.0004884004592895508 at pixels 7580 and 7643, with zero moved marks. Its 16-bit target was 0.99951171875 and its 32-bit still ratio was 1.0000001192092896. The old half-float probe rounded their difference to 0.00048828125 and hid the final 32-bit step. The revised test allows one half-float step below one plus three single-float steps at one, rounding Vulkan's 2.5-ULP division bound upward. Its spatial tolerance and strict zero moved-mark invariant remain.

The obsolete hit-light cap test expected 32,256 W/(m² sr). Its replacement checks physical emission of 100,000 and the public emission bound 2^64 W/(m² sr), with ray weights at most four and finite geometric metadata. The fixture uses the first draw, before reprojected glossy estimates contribute. An initial two-draw fixture produced a valid reflected estimate 1.2547974 times its emission and failed that incorrect independent-source assumption. The corrected two source cases pass with the original numeric assertions. The other 28 affected cases passed, including ambient, rounding and constructor cleanup. The overlong ambient test delegates its following-frame checks to a named helper. All production bytes still match the captured and hardened input.

GLSL is excluded from managed line and branch coverage because Microsoft code coverage cannot instrument SPIR-V. The scripted GPU faults check scene units, float32 formats, fast and slow history, both parities, numeric range, per-channel sharing, composite conversion, typed fallback bindings, synchronization and hit-distance metadata. Compile-time C# format declarations also have no managed coverage points; their GPU behavior faults were killed.

The unified table retains 42 earlier scripted kills for unchanged behaviors. Old S24 is retired because glossy exposure conversion is no longer the selected behavior. S23 remains valid for diffuse producing-exposure conversion. Core and Simulation source is byte-identical to the recorded official run, so that evidence is retained. Its three official no-mutant classifications still have no official mutation score. No cloud rerun occurred. The current set and retained evidence have separate provenance and are not pooled into one score.

[The unified mutant table](mutants.md), [hardening results](hardening.json), [per-file coverage](coverage.json) and [capture source hashes](final-source-sha256.json) and [final integration source hashes](integrated-source-sha256.json) preserve the details. No new soak or MTBF claim is made.

## Final game captures

The public Host ScriptRunner and HeadlessPlayback ran the real demo-surface scene and Vulkan renderer after Design A. Six processes produced 134 fresh PNGs: 49 film frames and nine independent captures per destination, for native and FSR. Every process passed with zero validation messages. The independent still controls retain their existing two draws per capture. Each tested first movie cut draws and presents exactly once.

All outputs are 1280 × 720 pixels. Native shades 1280 × 720 pixels. FSR shades the observed 848 × 477 active rectangle. The output target is offscreen, with the headless film clock. This is game correctness and appearance evidence on the Mac. It makes no desktop-window or Linux performance claim.

| Path | Tick | First applied stops | Independent settled stops | Draws | Presentations | Center sRGB |
|---|---:|---:|---:|---:|---:|---|
| native | 1417 | -7.6409826 | -7.6409974 | 1 | 1 | [56, 54, 50] |
| native | 1462 | 2.87346 | 2.87346 | 1 | 1 | [71, 67, 61] |
| fsr | 1417 | -7.6406617 | -7.6406984 | 1 | 1 | [56, 55, 51] |
| fsr | 1462 | 2.8750825 | 2.8750825 | 1 | 1 | [70, 66, 60] |

One stop doubles display exposure. All four first-cut center colors match their independent controls exactly. The largest exposure difference is 0.0000367 stop, below the 1/16-stop tolerance. [The film readings and PNG hashes](film.json) include actual active sizes, raw scene radiance, applied exposure and independent control values.

The four selected first-cut PNGs were opened and inspected. Outside views retain the cockpit hull, window and pilot. Return views retain aisle tiles and yellow rails beneath the bright doorway. FSR shows stepped rail and hull edges on its first reset frame. No whole-image equality claim is made from the center-pixel comparison.

## Integration and Linux boundary

Main baseline b002351eb48d33f008f8333e53e61345de3260cd differs in production source from 19e17c5ba364392ca6bf84b00c11a7d709771ded only by allocator fix #35. Baseline and candidate preserve allocator blob f7aae7bd20d48ea3adf0d565ed476a6d2ec54594 and source patch SHA-256 61c94080b16aacd672c3ebc233590eeac63dc19d4d067dbd0d0eb3b70f800813. The previous memory-requirement measurements remain in the main report. No format or allocation path changed during this hardening stage.

The original normal hook failed as recorded above. The corrected normal hook passed 3798 tests and skipped 12 on candidate `588b6d2747560c73aac542fb25ce80eec1ba8db7`. The commit, push and F1 resolution are complete. The [single corrected Linux submission](../design-a-linux-2026-11-28/README.md) then stopped before matched rounds at its capacity check. It separately counted $0.09488624. The prior failed job remains `40d9f654-f5dc-4671-8a58-01c21712037f-u2`, with canonical cost $0.05953728. PR #32 stays draft and In Progress. No further paid retry is approved. Parent orchestration owns the next decision, CI, metrics, merge and spending decisions.

The external evidence root is /Users/alexispurslane/Movies/Blue Danube/exposure-bloom-recovery/issue-4-evidence. No heavy command remains from hardening or captures.
