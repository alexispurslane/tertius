# Direct-light pair research

Research owner: pair-tile. Implementation and merge owner: t2-mlvy.
All artifacts below are under `/private/tmp/pair-tile`, unless an absolute path says otherwise.
No engine change is committed or merged.

## Current conclusion

The earlier timings do not prove a register-pressure cause. Register pressure means the temporary storage that each shader invocation needs. Occupancy means how many shader invocations can run at once.

The offline compiler gives evidence that register demand can reduce occupancy. It gives no evidence of stack traffic in main's direct shader. Stack traffic means reads and writes of per-invocation memory outside the registers. These are offline compilations, not the exact runtime MoltenVK pipelines. Runtime spills remain unmeasured.

The disabled extra loop doubles the compiled body and raises its highest referenced register. A hardware model places these two variants on different occupancy steps. This is a plausible cause of its slowdown, not a causal measurement. Unexecuted code size alone does not establish an instruction-cache problem.

The counter trace recorded and its standard report completed successfully. The image and timing comparisons of A, B and the two comparison-sample filters remain queued. No GPU gain is claimed yet.

## What the earlier timings establish

The fixed-exposure old-main deck measurements supplied by t2-mlvy are:

| Variant | Direct busy median, ms |
| --- | ---: |
| maind, every operation | 4.190 |
| x2d, stop after light geometry | 1.100 |
| x3d, omit shadows | 3.142 |
| x4d, constant response with shadows | 1.825 |
| x5, separate shadow and response loops | 6.690 |
| Extra visible-floor loop disabled at runtime | 4.701 |

Busy time is the duration of the pass's GPU intervals, with its own overlapping intervals counted once. Different passes can overlap. Their busy times therefore do not add to the frame span.

Removing shadows changes which pixels reach the response. Stopping after geometry lets the compiler discard values used later. The split-loop variant repeats light geometry and adds an array. These changes do not isolate one cost.

The unadjusted four-way residual is `4.190 + 1.100 - 3.142 - 1.825 = 0.323 ms`. The earlier estimate of about 1 ms also adjusts for the average surviving pair count. Such an adjustment does not establish the cause.

The original no-shadow figure of 0.75 ms is unsuitable for comparison. Automatic exposure changed the light reach. All new deck work uses exposure 0.7041458 stops, compiled into the binary.

## Offline native shader evidence

Body size is the fragment machine-code length after the constant-program prologue. Highest register is the largest referenced 32-bit register index. It is not the runtime allocated register count. Stack columns count static instruction sites. They do not count executed accesses per pixel. Local arrays and function calls can also need stack traffic.

| Shader | Body bytes | Highest register | Stack load sites | Stack store sites |
| --- | ---: | ---: | ---: | ---: |
| maind direct | 155554 | r107 | 0 | 0 |
| x3d direct | 53536 | r92 | 0 | 0 |
| x4d direct | 116070 | r71 | 0 | 0 |
| x5 direct | 191864 | r122 | 11 | 9 |
| Disabled extra loop direct | 314350 | r117 | 0 | 0 |
| m2d direct | 157348 | r116 | 0 | 0 |
| Key and fill version 1 direct | 157864 | r122 | 0 | 0 |
| Key and fill version 1 corners | 58466 | r127 | 119 | 30 |
| Key and fill version 2 direct | 161048 | r126 | 0 | 0 |
| Key and fill version 2 corners | 29534 | r127 | 22 | 13 |
| A direct | 157338 | r121 | 0 | 0 |
| B direct | 157256 | r116 | 0 | 0 |
| Baseline one-sided glass | 99678 | r127 | 166 | 93 |
| A one-sided glass | 100130 | r127 | 162 | 91 |
| B one-sided glass | 99200 | r127 | 150 | 87 |
| Baseline two-sided glass | 171440 | r127 | 390 | 153 |
| A two-sided glass | 170730 | r127 | 306 | 158 |
| B two-sided glass | 171422 | r127 | 384 | 152 |
| Sixteen-sample filter direct | 158102 | r112 | 0 | 0 |
| Sixteen-sample filter one-sided glass | 99996 | r127 | 150 | 92 |
| Sixteen-sample filter two-sided glass | 171890 | r127 | 390 | 153 |

The corner shader does have stack traffic. Its r127 result is not evidence that it avoids spills. The glass shaders also have substantial stack traffic in this offline compilation.

Version 2's direct shader has a 2.35% larger body than m2d and reaches r126, with no stack traffic. It remains in the same conditional occupancy step. Its corner rewrite nearly halves body size and removes most static stack instruction sites. Its runtime gain remains unmeasured here. The tile owner reports that version 1 saved about 2 ms of deck frame span, but blurred shadows and failed the image requirement. Version 2 restores shadows per pixel. Those results belong to the tile owner's experiment.

The Asahi driver's occupancy table uses 16-bit register units. It allows 448 resident threads through 232 units and 384 threads through 256 units. Under that model, r107 fits the 448-thread step, while r117 requires the 384-thread step. m2d, A, B and key-and-fill version 1 all fit the same 384-thread step. This prediction assumes that runtime register allocation follows the offline reference counts.

Source: [Asahi occupancy model at pinned revision](https://github.com/chaotic-cx/mesa-mirror/blob/6d4d60f15c982b8db8bd5e424259ef3c0a3a7f18/src/asahi/compiler/agx_performance.c#L10).

The two uses of m2d's r116 are in the local shadow-atlas filter. A gather, which returns four depth comparisons, writes r113 through r116 at native offsets 0x017ede and 0x022794. The compiler issues nine gathers before reducing their 36 results. The highest register therefore holds a shadow comparison. It is not itself a far-point input. The other live values at those instructions remain unmapped.

Three bounded follow-up experiments found no lower-register candidate. C1 delays rectangle-corner construction until after the far-point return. It retains 157348 body bytes and r116, with zero stack traffic. C2 attaches the standard SPIR-V DontUnroll hint to the shadow filter's three-row loop. The standalone MoltenVK converter drops the hint, so the native result stays unchanged. C3 finishes the far-point diffuse result inside its branch. It grows to 157480 bytes and r123, with zero stack traffic. None is queued for a GPU test.

Apple's [WWDC20 shader-core explanation](https://developer.apple.com/videos/play/wwdc2020/10632/?time=2273) also states that register demand can reduce occupancy. It does not establish this renderer's runtime allocation.

## Prototype A

A moves the surface tangent frame and LTC lookup before the rectangle-light loop. The tangent frame is two surface directions used to express the lamp corners. LTC is a lookup-based approximation of the material's reflection lobe, integrated over the rectangle.

Both quantities depend on the surface and view, not the lamp. The existing rectangle-shape bit guards the setup. The shader retains the arithmetic expressions and light accumulation order. Its compiled lookup is outside the loop's backward jump. The cost is a longer lifetime for the setup values. Direct reaches r121 against r116, with no offline stack traffic.

Patch: `prototypes/a/lighting.patch`. Binary: `bin-a/Tertius.Demo`.

## Prototype B

B finishes the diffuse integral before it constructs the specular corners. This narrows the period during which both sets of temporary values must remain available. It keeps the LTC lookup inside the light loop. It can change compiler scheduling and rounding.

The direct result remains r116 with no offline stack traffic. Its static instruction count equals main's. One-sided glass has fewer stack instruction sites. These results give only a weak performance prediction.

Patch: `prototypes/b/lighting.patch`. Binary: `bin-b/Tertius.Demo`.

Both prototypes independently use merged main d4a2d11f9. Both Release demo builds passed with zero warnings and errors. Both shaders passed `spirv-val --target-env vulkan1.3`. The generated baseline shader hashes match bin-m2d. The worktree sources were restored after export.

## Sixteen-sample filter

The tile owner proposed replacing the nine four-result shadow gathers with sixteen one-result bilinear comparison samples. The mathematical filter is the same. The hardware rounds its interpolation weights, so the rendered image can change. Vulkan permits as few as four fractional bits. The change therefore needs visual review on the Mac and cannot claim bitwise equivalence across Vulkan drivers.

The offline gate passes: r112 represents 226 half-register units, below the model's 232-unit boundary. The direct shader has no stack traffic. The compiler still batches the samples, but the block produces fewer comparison results. A CPU calculation over 10000 random binary 5-by-5 shadow patterns confirms the unquantized filter weights, with maximum difference 3.33e-16. The existing atlas sampler already has linear comparison filtering. Explicit zero gradients preserve defined sampling under per-pixel branches.

This candidate is `c16` in the artifacts, and C2 in the tile owner's message. The earlier `prototypes/c2` directory is the unsuccessful DontUnroll hint experiment. The proposed filter is `prototypes/c16/shadows.patch`, with binary `bin-c16/Tertius.Demo`. Its Release build passed with zero warnings and errors. Both glass specializations were inspected offline. Source was restored afterward.

One c16 still was added to the existing shared batch, after main, A and B. Its trace was added after the existing main/A/B batch, conditional on a visual-review marker. No new queue take was added. Both batches retain their 18-minute limits.

## Why these small changes come before a new light organization

| Approach | Situation it serves | Fit to this renderer |
| --- | --- | --- |
| Tiled or clustered reach lists | Many small lights, with most light volumes missing a tile | Large reach rejects few of 13 to 64 lamps. Lists add overhead without reducing many pairs. |
| One additive draw per lamp | Small projected light volumes | Large lamps repeat surface reads and setup over much of the screen. Tile memory helps some GPUs but is not a portable Vulkan guarantee. Blending changes rounding. |
| Separate shadow and response passes | Enough saved shader state to repay intermediate storage | A full R32F shadow image per lamp is 47.9 MB for 13 lamps or 235.9 MB for 64 at 1280 by 720, before read/write traffic. Geometry is repeated. |
| Shared work across nearby pixels | Similar receiver geometry and smoothly varying light | Normals, silhouettes, shadows and highlights break exact sharing. Interpolation requires an image-quality judgment. |
| Reuse opaque light for glass | The same receiver and material | Glass has a different position, normal and material. The opaque light value does not transfer directly. |
| 16-bit arithmetic | Hardware and drivers with enabled support, and suitable numeric ranges | Device support is not enabled today. Any use needs Vulkan feature checks, a fallback, and image review. Positions, shadow depth, cancellation-sensitive edges and sums need more precision. |

The source already separates shape loops and material paths. Hoisting per-surface work and narrowing temporary lifetimes preserve that model. No reservoir sampling is proposed.

## Corner target size

The CPU-only Metal query in `tile-size.m` creates render encoders with matching attachment formats. It makes no draw or dispatch and never commits a command buffer. `tile-size.log` records the M1 Max's selected tile dimensions.

| Attachments | Chosen hardware tile | Tiles for 161 by 91 | Tiles for 1280 by 720 |
| --- | --- | ---: | ---: |
| Version 1 corners, two RGBA32Float and one RGBA32Uint | 32 by 16 | 36 | 1800 |
| Version 2 corners, RGBA32Uint and R32Float | 32 by 32 | 18 | 920 |

These are hardware render tiles, distinct from key-and-fill's eight-pixel light grid. A small target can limit parallel work and leave a long final wave. There is no verified fixed cores-per-tile rule here. In particular, version 1's measured corner cost came from 36 tiles, not about 15.

The owner's spread-corners experiment adds discarded fragments and sparse active shader lanes as it adds hardware tiles. Its timing therefore does not isolate tile count. If it improves time, a compact compute dispatch of independent corner jobs is a candidate that avoids the large sparse image.

## Measurement plan already queued

1. Counter trace of bin-m2d, deck script, start 1, 120 frames. Output: `trace-main-counters`.
2. Shared take with one 1920 by 1080 deck still each for main, A, B and c16. Output: `stills`.
3. Exclusive batch of no-counter deck traces for main, A and B, start 1, 120 frames. Outputs: `trace-main`, `trace-a`, `trace-b`. The c16 trace follows only if its image passes visual review.

The counter trace diagnoses limiters. Its instrument changes timing, so it does not set the performance budget. The uninstrumented trace comparisons use direct and transparent busy medians. The frame budget remains the GPU frame span's 95th percentile at 1280 by 720 render size, upscaled to 1920 by 1080.

The image comparison records absolute RGB differences in eight-bit steps and writes difference images amplified 32 times. Visual review is required before any recommendation.

## Reproduction and limits

`native-inspection.json` holds every offline result. `compile-native.py` creates offline Metal libraries and pipelines. `inspect-native.py` extracts and decodes the fragment body. The decoder is dougallj/applegpu at revision 28dd80468d4a6c94fa4dac34ed488196f5fedc5f. All opcode words were recognized, although some tile and interpolation instructions have generic decoder labels.

The Metal compiler is version 27.1.266.1, target applegpu_g13s. Direct uses standalone MoltenVKShaderConverter. Glass uses SPIRV-Cross 1.4.357.0 because the standalone converter rejected runtime texture arrays. The glass pipeline has the real mesh vertex declaration and material specialization, but not the full runtime resource mapping or blending state. Native size and register comparisons therefore need runtime confirmation.

`gpu-trace-bin.sh` takes prebuilt binaries and uses the main checkout's absolute GPU lock helper. `trace-watch.py` only signals its own xctrace child if recording hangs. Still and trace batches have 18-minute limits after acquiring the lock.

The tile agent received the variant confounds, main's absence of offline stack traffic, the corner and glass stack counts, the two prototype results, and the conditional occupancy prediction.

## Nine-sample follow-up

Castaño's 2013 [Shadow Mapping Summary](https://www.ludicon.com/castano/blog/articles/shadow-mapping-summary-part-1/) describes smooth cascaded sun shadows for The Witness. Its stylized surfaces exposed random-filter noise. It used a compact Gaussian-like filter and measured an optimized bilinear-sample version against gathers on a GTX 480 and an Intel HD 4000. It does not require a denoiser. Its performance numbers do not transfer to this M1 Max.

The same weight-pairing method applies to our exact box filter per lamp. Per axis, pair the texel weights (1-f, 1), (1, 1), (f, 0). The three sample weights are (2-f, 2, f), with interpolation fractions (1/(2-f), 1/2, 0). Their product needs nine bilinear comparison samples. It keeps main's nine texture instructions and returns nine comparison results instead of 36. The fixed cost is two reciprocals and extra coordinate/weight arithmetic. It does not change how cost scales with lamp count.

The analytic filter check covers 10000 random binary patterns and four endpoint cases, with maximum absolute difference 3.33e-16. Hardware interpolation still changes rounding. This is c9 in the artifacts. Its offline direct body is 156972 bytes, highest r112 (226 half-register units), with no stack traffic. One-sided glass: 99194 bytes, r127, 146 stack-load sites, 92 stack-store sites. Two-sided glass: 171110 bytes, r127, 378/151 stack sites. The direct Release build passed with zero warnings/errors. No source edits remain in the worktree.

A rounding-bound correction was sent to the owner. A one-axis sample error cannot simply be divided by 16 and treated as the full two-axis filter error. Its effect repeats over the other axis, and both axes can contribute. Assuming nearest rounding of only the hardware interpolation fractions, a simple full-filter bound is 3.125% absolute at four fractional bits and 0.1953125% at eight. A CPU grid search over phases gives binary-pattern examples of 2.3063899414% at four bits (both phases 0.118) and 0.1433004410% at eight bits (both phases 0.068). These are constructed worst cases, not rendered-image errors; coordinate and arithmetic rounding are excluded.

The owner authorized c9's addition to the existing batches. Stills run main/A/B/c9/c16. Timing runs main/A/B/c9, then c16 only if its image is approved and at least five minutes remain in the 18-minute take. Each sampled filter has its own visual-review marker. No new queue take was added.

## Counter attribution and frame selection

The owner's deck-m2d-1 report gives direct mean busy/alone time 4.107/4.060 ms and glass 2.048/0.352 ms. About 83% of glass busy time overlaps other passes. Whole-GPU counters averaged over glass therefore mostly mix different passes. counter-attribution.py records both the all-interval and alone-interval subset, with sample counts. The alone subset can select a different phase of a pass; it does not prove a cause and does not exclude unlabelled or foreign-process activity.

The trace report includes 151 draws, but gpu-timings.json times 119. The renderer's default adds 32 warm-up draws. The attribution script excludes those first 32 after validating the counts. The uninstrumented gpu-timings.json frame span, not the trace report's warm-up-inclusive p95, is the budget quantity.

## Main counter trace, first result

The main counter trace completed successfully. The standard report includes 151 draws. Direct light: arithmetic-unit limiter 39.56%, arithmetic-unit utilization 36.62%, texture-sample limiter 4.99%, last-level-cache limiter 4.53%, read bandwidth 1.06 GB/s, fragment occupancy 12.33%. Direct mean busy/alone time is 4.205/4.157 ms. Arithmetic is the largest reported limiter but is far from fully utilized. This is evidence against texture or external-memory throughput being the main limit. Low occupancy remains consistent with several causes, including register constraints, dependency chains, or too little parallel work. It does not prove the register hypothesis or identify a runtime allocation step.

Glass all-interval counters: arithmetic-unit limiter 25.63%, texture-sample limiter 2.59%, fragment occupancy 8.49%. Mean busy/alone/shared time is 2.087/0.359/1.728 ms. The alone subset of the 119 timed frames is being computed separately. These counters do not measure stack spills. Counter-instrumented timing is not a performance claim. The recorded timed clock averaged 1269 MHz, with p10 1174 MHz and p90 1292 MHz.

The attribution report is now complete: `trace-main-counters/counter-attribution.json`. After the 32 warm-up draws, direct alone gives 39.82% arithmetic limiter, 5.04% texture limiter, 11.97% fragment occupancy over 50001 samples. Removing overlap barely changes the conclusion. Glass alone gives 6.04% arithmetic limiter, 1.42% texture limiter and 1.80% fragment occupancy over 3419 samples and 33.2 ms total across 119 draws. Glass's all-interval arithmetic limiter is 25.71%. This phase dependence prevents a reliable glass-shader limiter claim. The all-interval row mixes later passes, while the alone subset selects a short different phase. No runtime spill or exact allocation claim follows.

## Stills wrapper failure and replacement jobs

At about 02:30 UTC, the first shared take failed before running the demo. Its log path was `stills/take.log`, but the parent folder did not yet exist; the body would have created it too late. `stills-take.log` preserves the helper error. I created the folder, terminated only my own still-queued exclusive helper PID 43831 (exit 143, no GPU work), notified the tile owner, and requeued the same shared stills followed by the same exclusive timing batch. The completed counter trace remains valid. New shell sessions are 59722 (shared stills) and 77953 (exclusive timing); their outer logs are `stills-take-retry.log` and `traces-take-retry.log`. No test scope changed and no completed GPU run is duplicated.

## Disk-full audit and cleanup

At 02:45 UTC, the completed counter capture, JSON reports, shader archives and Release build logs were intact. Every JSON under this research folder parsed. Both queued helpers were alive and had not acquired their take, so their empty outer logs were expected. No stale queue entry belonged to this research. The earlier still wrapper failed because the log parent folder did not exist; its replacement jobs were already queued. No extra rerun or requeue was needed. Both take bodies now print the available disk space before work. Deleted the regenerable gpu-counter-value.xml export after confirming counter-attribution.json retains 119 timed frames, 32 excluded warm-up frames and both pass reports; the original run.trace and reproduction script remain. Freed 2114804038 logical bytes (2114805760 allocated bytes). No superseded trace or uncited build copy remained.

## Offline split by lamp shape

The two compiled halves remove only the unwanted shapeLight calls from modelLight. They preserve every retained lighting term, including a far rectangle lamp's point approximation. No GPU work or full demo build was performed. Both halves were also compiled with c9. All four SPIR-V files validated, and every native instruction decoded.

| Shader | Body bytes | Highest register | 16-bit units through that register | Stack load/store sites |
| --- | ---: | ---: | ---: | ---: |
| split-c9-points-tubes | 92100 | r100 | 202 | 0 / 0 |
| split-c9-rectangles | 65586 | r112 | 226 | 0 / 0 |
| split-main-points-tubes | 92436 | r103 | 208 | 0 / 0 |
| split-main-rectangles | 65734 | r117 | 236 | 0 / 0 |

The point/tube half removes LTC lookup references. The rectangle half must retain the far-point response; removing all GGX point response would change existing behavior. Main rectangles alone reaches r117 (236 units), slightly higher than unsplit main's r116 (234). The c9 rectangle half reaches r112 (226), equal to unsplit c9. Thus splitting removes code but does not reduce the rectangle peak below c9 by itself. It adds surface reads/setup and an extra output operation. No timing gain can be inferred.

The proposed additive draws also require a correctness design. demodulated calls exposed, which caps each channel at MaxRadiance (32256) after the complete direct sum. Separately capped halves added by blending can exceed that cap. The extra RGBA16Float store changes rounding; output alpha would need its own treatment. The offline halves retain the existing epilogue solely for comparable compilation, not as a proven equivalent renderer implementation.

## Complete Asahi occupancy table

Maximum 16-bit units → best-case resident threads: 104→1024, 112→896, 128→832, 136→768, 144→704, 160→640, 184→576, 208→512, 232→448, 256→384.

Using twice (highest 32-bit register index plus one): x4d r71→144→704; x3d r92→186→512; old main r107→216→448; m2d r116→234→384; c9 r112→226→448; c9 point/tube half r100→202→512. The next target beyond c9 is r103, nine fewer 32-bit registers. Further targets are r91, r79 and r71. The timing variants also remove work, and x4d/x3d compare with old main, not m2d. Their time cannot isolate the occupancy effect. The tile owner dropped the two-draw split; no GPU test is planned for it.

## Deck still review

All five 1920×1080 stills completed. Main, A, B, c9 and c16 were viewed beside one another; both filters' 32× difference images were also inspected. The frame shadows on the floor and the glass-frame edges retain contrast, with no visible new block or edge artifact. A is byte-identical in displayed RGB. B changes 0.1513% of pixels, mean absolute RGB difference 0.000609 steps, maximum 5 steps. c9 changes 6.9647%, mean 0.030014 steps, maximum 8; c16 changes 9.4825%, mean 0.040729, maximum 11. Both filters have a 99th-percentile per-pixel maximum-channel difference of one step. c9 has 302 pixels above two steps and c16 427, of 2,073,600. These are eight-bit RGB steps, not percent radiance. Both single stills pass visual review for the planned trace. This is not a Linux-driver or motion-quality validation. Markers were written before the timing batch reached the filter rows.

## Completed timing batch

All five no-counter captures completed in one exclusive take, 02:53:55–02:59:19 UTC (5 min 24 s), with no watchdog, disk-full or lock-release error. GPU clocks averaged 1292.0–1293.0 MHz. Each renderer timing file contains 119 measured frames. Pass values below are Instruments busy medians; the timed frame span excludes warm-up.

| Variant | Direct busy ms | Transparent busy ms | Transparent alone ms | Frame median ms | Frame p95 ms |
| --- | ---: | ---: | ---: | ---: | ---: |
| main | 4.093083 | 2.084958 | 0.295667 | 24.381833 | 25.003458 |
| a | 4.009209 | 2.365833 | 0.287209 | 24.383667 | 25.162084 |
| b | 4.151416 | 2.151501 | 0.316875 | 24.472750 | 25.204250 |
| c9 | 3.695583 | 2.124708 | 0.311917 | 24.206166 | 25.562125 |
| c16 | 3.759520 | 2.309750 | 0.303083 | 24.389625 | 25.488291 |

A saves 0.084 ms in direct, but transparent grows by 0.281 ms and frame median is unchanged. B loses 0.058 ms direct and 0.067 ms transparent. c16 saves 0.334 ms direct but transparent grows by 0.225 ms and frame median is unchanged. c9 is the useful candidate: direct saves 0.3975 ms (9.7%), transparent changes by +0.040 ms, below the agreed 0.05 ms threshold for a glass claim. c9 frame median saves 0.176 ms, while p95 grows by 0.559 ms. It therefore demonstrates a direct-pass gain, not a p95 frame-budget improvement. No candidate demonstrates a p95 gain in this batch. Busy pass times overlap and cannot be summed into a frame saving.

The lower offline register peak and measured direct gain are consistent with the register hypothesis but do not prove it: the sample filter also changes arithmetic. The useful follow-up belongs to the tile owner: retain c9 as a candidate, validate moving views and Linux drivers, and measure the final combined renderer's frame p95. No further GPU job is queued by this research. All source work remains in scratch and the research worktree is clean.
