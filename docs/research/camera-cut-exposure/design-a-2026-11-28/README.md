# Design A implementation milestone for #4

Glossy reflections now keep 32-bit linear scene radiance per color channel, in W/(m² sr), through hit and miss producers, real and impostor emission, hit-light storage, own and block ray records, resolve samples, both fast-history parities, slow history, shared scratch, temporal and spatial filtering, reflection sharing and composition. Original, precise-capped and precise-uncapped modes use the same light units and formats. Camera exposure applies downstream to displayed color.

The implementation now has [scoped hardening and fresh final game captures](hardening-and-film.md). The original milestone evidence below keeps its source provenance. Integration through the normal hook and the final matched Linux proposal follow this evidence. PR #32 stays draft and In Progress. No new reviewer or cloud job ran.

## Runtime evidence

The restored F1 baseline run passed 16 tests and failed both glossy endpoint cases, with a real exit of 2. It failed on raw scene radiance. The saved original RED evidence remains unchanged.

On the final frozen implementation, Checked passes 80 focused checks plus six actual impostor checks in each of the three history modes: 258 passed, zero failed, zero skipped. Vulkan validation is clean. A live-output readback run then passes both F1 endpoint cases and preserves their actual raw values. The earlier broad development run passed 228 tests; its unaffected checks supplement the final scoped runs and do not replace the pending normal hook.

The initial original-mode batch included one test that explicitly requires the default precise history. That batch passed 144 tests and failed that fixture assertion. The final mode-specific runs retain the test's default-mode requirement and select the applicable tests for original mode. No assertion was weakened to accommodate that run.

| Observation | Incoming exposure (stops) | Used exposure (stops) | Center raw HDR RGB, W/(m² sr) | Scene draws |
|---|---:|---:|---|---:|
| Independent cold destination | Fresh renderer | 1.3179092 | [0.067438096, 0.07332414, 0.08792042] | 2 |
| Independent settled destination | Destination history | 1.3179092 | [0.067438096, 0.07332414, 0.08792042] | 2 |
| Independent one-draw estimator reference | Destination already exposed correctly | 1.3179092 | [0.067806594, 0.07394369, 0.0889896] | 1 |
| First cut from black | +64 | 1.3179092 | [0.067806594, 0.07394369, 0.0889896] | 1 |
| First cut from bright emission | -64 | 1.3179092 | [0.067806594, 0.07394369, 0.0889896] | 1 |

One stop doubles the exposure multiplier. The independent destination first settles, then restarts its sampler once while keeping its destination illumination and already correct exposure. That gives an independent reference with the same one-draw sampling index as the tested cut. The raw comparison keeps its 0.5% tolerance against this reference. Exposure independently compares with the settled destination within 1/16 stop. Initial cold and settled readings remain in the log. This avoids comparing a one-draw random estimate with a two-draw still average. Still warmups are unchanged.

The tests also check ordinary frames, a repeated cut, actual surface hits, emitted light, native and FSR first cuts from both exposure histories, retained reflection reads, both history parities and all history modes. Real impostor hits preserve emission at 10^18 W/(m² sr) at fixed display exposures of ±64 stops. The public source-emission bound remains 2^64 W/(m² sr); the tests of larger requested emission compare against that documented bound. Synthetic filter samples at 10^-30 and 10^30 exercise physical second moments against an independent double-precision reference.

A new color regression first failed on the one-scale reflection fit. It places red at 10^-30 beside green at 10^30 and requires an admitted neighbor to receive positive red light. The implementation now normalizes each channel separately. This regression passes on all three modes.

## Numerical and unit boundaries

The former glossy 32,256 radiance budget and half quantization are deliberately replaced by Design A. Diffuse estimators retain their exposure conversion, half storage and deviation bound. Probability floors, MostRayWeight = 4, hit-distance bounds, ages, motion marks and history lengths keep their own constraints. The importance guide remains a separately bounded pre-exposed sampling distribution. It supplies probabilities, never reflected radiance. The shared sent-light helper returns scene radiance; the guide applies its own encoding explicitly.

The supported physical source sum stays below 2^119. Glossy producers have a scene-unit numerical guard at 2^124, above that physical sum. A weight at most four keeps samples at or below 2^126. The guard prevents unbounded numerical feedback and is independent of display exposure. Physical short-history repair uses an incremental weighted mean. Per-channel neighborhood normalization protects spatial second moments before squaring. Reflection sharing fits normalized values and bounds each restored product before multiplication. Geometric guides remain half floats and bind separate typed, synchronized images. Original-mode fallback guide synchronization is fixed.

The reflection reference tool keeps its existing artifact schema. Its probe applies exposure only when serializing the scene-unit result into that schema. Both original and precise probe bindings match the new scene formats. Existing artifacts keep their original provenance.

## Image memory

All outputs are 1280 × 720. Native renders 1280 × 720; FSR uses a fixed scale of 2/3 and renders the observed 848 × 477 rectangle. The rows below sum the affected light images, slow history, two shared glossy scratch slots and typed empty images. Both fast-history parities and ceil-divided block extents are included. Unchanged resources are excluded from both totals.

The candidate uses actual allocated image descriptions and VkImageMemoryRequirements. Baseline requirements are reconstructed on the same Mac device with the original formats and identical image descriptions. Temporary baseline image handles are queried without allocating baseline image memory. These are image-memory requirements in bytes, including driver padding. They are not total renderer memory, allocator block reservations, peak process memory or Linux measurements.

| History mode | Path | Render pixels | Before bytes | After bytes | Added bytes |
|---|---|---|---:|---:|---:|
| Default (precise uncapped) | Native | 1280 × 720 | 84,869,248 | 124,518,656 | 39,649,408 |
| Default (precise uncapped) | FSR | 848 × 477 | 38,027,392 | 54,919,424 | 16,892,032 |
| Original | Native | 1280 × 720 | 47,186,048 | 124,518,656 | 77,332,608 |
| Original | FSR | 848 × 477 | 21,561,472 | 54,919,424 | 33,357,952 |
| Precise capped | Native | 1280 × 720 | 84,869,248 | 124,518,656 | 39,649,408 |
| Precise capped | FSR | 848 × 477 | 38,027,392 | 54,919,424 | 16,892,032 |

The original mode also gains a 32-bit slow history and two shared 32-bit scratch slots. Its existing half scratch remains available for diffuse and geometric bindings. The precise modes already had the 32-bit slow history and scratch. Their added memory comes mainly from hit, ray, sample and fast-history images. A format changes from 8 to 16 bytes per RGBA texel; driver padding makes the exact requirement delta differ from that nominal payload difference. Each JSON file records every resource, extent, format and requirement.

## Main and allocator provenance

Baseline main is b002351eb48d33f008f8333e53e61345de3260cd. Its only production-source change from the prior #3 baseline 19e17c5ba364392ca6bf84b00c11a7d709771ded is the allocator fix. The pending branch merge retains the analytic raw meter and adaptation dependency. Baseline and candidate allocator bytes both have Git blob f7aae7bd20d48ea3adf0d565ed476a6d2ec54594. The #35 production patch SHA-256 remains 61c94080b16aacd672c3ebc233590eeac63dc19d4d067dbd0d0eb3b70f800813. Its already tested evidence remains under docs/research/valid-memory-heaps.

## Integration boundary

Scoped hardening and fresh native/FSR game cuts are complete. [The follow-up report](hardening-and-film.md) holds coverage, the unified mutant table and all first-cut/control readings. The normal hook, local merge commit, push and F1 thread resolution follow. The final immutable candidate pins the matched Linux proposal. No paid retry is approved.

The external artifact root is /Users/alexispurslane/Movies/Blue Danube/exposure-bloom-recovery/issue-4-evidence. design-a-next-stage.md is the continuation brief. evidence.tar.gz preserves logs, commands, source snapshots and hashes. The source manifest pins this milestone without claiming an uncreated candidate commit or a completed normal hook.
