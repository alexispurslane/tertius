# The portable screen-walk port (ter-rgg9)

Date: 2026-11-05. Review base: `34c28466e641a864fd6a915c029e08f07b3b9ccc`.
Branch: `ter-rgg9-portable-screen-walk`. The parent owns coverage, mutation testing and integration.

## Scope and source situation

This port takes only the already-selected two-image bounds, loop and facing changes from
[walk-hill-climb.md](walk-hill-climb.md). It introduces no new traversal algorithm.
The bounds and loop conditions use vector Boolean operations instead of scalar short-circuit flow.
The facing test alone consumes an unnormalized normal. Shading normals remain normalized.
Both use the reconstruction and lower-hemisphere folding owned by `octahedral.glsl`.
The loop retains the original negated end comparison, including its result for an unordered comparison.

The source experiment used a 24-core Apple M1 Max, 1280 × 720 images and about 0.61–0.92 million active rays per view.
Its four saved views contain 3,038,431 rays. Reflection and diffuse walks have finite caps of 256 and 62 steps here.
Its exact gate concerns hit identity, not denoised image similarity.
The port has the same input scale, hardware and quality target, but a corrected traversal.
It retains the integrated tracer and History Rule A supplied by the review base.

These changes remove constant instruction costs, not an asymptotic traversal cost.
They add no texture read, queue, sort or allocation and need no optional subgroup feature.
The two images, their mip meanings, arithmetic order, per-ray integer words, caps, slab checks and
`childAt`/`solved` tracking remain unchanged. No 0.005-pixel nudge returns.
No one-image pyramid, cap-54 approximation, scheduler or private Metal API is adopted.
Linux runtime remains user-deferred under ter-cgif. This Mac's results do not establish Linux performance.

## Test-first evidence and saved rays

Three compiled-shader tests first failed on the baseline, then passed after the port.
They exercise real compiled probes: bounds need no conditional branch, facing needs no normalization,
and the walk branches on the vector-combined cap and ray-end condition.
These are execution-cost checks, not source-text snapshots.
The existing C# `ScreenRay` reference and real shader correctness tests are not changed.

The saved-ray replay extracts declarations from this checkout's current `screen_ray.glsl` and
`screen_walk.glsl`. It runs both the review-base walk and the port through Vulkan under the project GPU lock.
It retains the original integer-words inputs, block mapping, reciprocal values, images and ray directions.
It calls the full corrected walk, including exact cell tracking, rather than the old V6 step.
The replay uses the current walk's integer hit pixel as identity; a miss has no pixel.
Saved receivers have no hair-card identity, so both replays use `NoCard`.
The unchanged real-GPU hair-card tests cover that separate rule.

All 3,038,431 active rays keep the same hit pixel or miss after the port.
The 16 dispatch-level counts are in [saved-ray-gate.json](portable-screen-walk/saved-ray-gate.json).
Every captured file listed with a byte count in the retained hill-climb provenance also matches its SHA-256 hash.

The current port differs from the saved cap-62 V6 baseline on 195,044 hit identities.
The older harness therefore fails the independent applicability gate.
Its 21.8–23.6% extracted-walk saving and projected 3.14-ms whole trace are not measurements of this corrected walk.
The port's zero-change gate compares against the integrated review base, not against those old V6 outcomes.

The replay driver comes from Git object `349ac0e36235358983c694e97adfaf97381472af`.
Only its SPIR-V path is used; private Metal packaging and workgroup configuration are removed from the temporary driver.
It is compiled through a temporary MSBuild include under this checkout's ignored test output directory.
No preserved tracer checkout is changed or built, and no temporary driver joins the final test suite.
The extraction scripts, generated shaders, raw outcomes and logs remain under `/private/tmp/ter-rgg9-20261105`.
Their source hashes and the captured input hashes are in [provenance.json](portable-screen-walk/provenance.json).

## Busy-deck measurements

Each build runs three 120-frame stretches of the real `demo-exit` deck, starting after tick 399.
Each film advances one simulation tick per shown frame. The size is 1280 × 720 with two captures in flight.
Every run holds `tools/gpu-lock.sh` and samples the clock through `tools/gpu-clock.sh`.
All six runs have mean, p10 and p90 clocks of 1296 MHz, the calibrated top clock.
The baseline's built binary is preserved before rebuilding the port, allowing alternating runs without source edits.
Renderer defaults, ray budgets, histories, Rule A and denoising policy do not change between builds.
Frame span means the GPU time from the first recorded timestamp to the last, including gaps between passes.
The p95 time is the nearest-rank 95th percentile: at least 95% of measured frames finish at or below it.

| Round | Before frame mean, ms | Port frame mean, ms | Before frame p95, ms | Port frame p95, ms | Before trace interval mean, ms | Port trace interval mean, ms |
|---|---:|---:|---:|---:|---:|---:|
| 1 | 11.300 | 11.159 | 11.602 | 11.457 | 3.595 | 3.468 |
| 2 | 11.316 | 11.180 | 11.561 | 11.496 | 3.604 | 3.468 |
| 3 | 11.333 | 11.208 | 11.629 | 11.484 | 3.608 | 3.485 |

The equal-sized runs give a mean frame span of 11.316 ms before and 11.182 ms after.
The reduction is 0.134 ms, or 1.18%. The port's mean is 0.818 ms below the existing 12-ms budget.
Each run's p95 also remains below 12 ms. This is a result for this deck stretch on this Mac, not every frame or driver.

The trace interval starts at `reflection rays` and ends at `screen trace resolve`, inclusive.
Its mean falls from 3.602 to 3.474 ms, a 3.56% reduction.
These are sums of neighboring timestamps calculated per frame, not sums of pass medians or p95 values.
On MoltenVK, a timestamp can include neighboring work. This interval is not a claim about isolated trace-pass GPU cost.
The before/after whole-frame span is the primary performance result.
All six clock records, interval definitions, frame statistics and driver warnings are retained in
[timings.json](portable-screen-walk/timings.json).

Reproduction uses the project Nix shell:

```sh
nix develop --command python3 -I tools/gpu-trace.py trim scripts/demo-exit.json 399 120 /private/tmp/deck-120.json
nix develop --command bash tools/demo-timing.sh /private/tmp/deck-port /private/tmp/deck-120.json --size 1280x720 --in-flight 2 --gpu-run 'reflection rays..screen trace resolve'
```

The first setup attempt used an unknown pass name and had no built clock sampler.
It failed and is excluded from every reported measurement.
The clock sampler was then built in this checkout's Nix shell before the six valid runs.
Temporary replay compilation also needed the existing `NearPlane` declaration and a test-scoped source path.
Those setup errors were corrected before any saved-ray gate was accepted.

## History and inspected captures

A separate observation run uses the real `ScriptCommand.Run`, the same deck film and two captures in flight.
Its test-only pass records draw state but writes no rendering state.
The initial still has 32 warm-up draws, followed by 120 shown frames with 120 distinct snapshot times.
The shown draw index advances from 32 to 151. No shown frame after the first resets its history.
The history-image parity, which chooses between two history images, also alternates on each draw.
This proves the film is not a repeated frozen snapshot disguised as warm-up.

The observation run writes PNG files and averages 812.6 MHz, with clock p10 713.2 and p90 947.1 MHz.
Its 16.346-ms mean frame span is not comparable with the top-clock discard-capture timings above.
It is excluded from that comparison, rather than converted to a supposed top-clock result.
[history-observation.json](portable-screen-walk/history-observation.json) records settings, state samples,
the observation source hash and the three inspected capture hashes.

| Capture | Draw index since history reset | Inspection |
|---|---:|---|
| `/private/tmp/ter-rgg9-20261105/screenshots/deck-000000.png` | 32 | Cabin framing, lit floor and reflections are visible. |
| `/private/tmp/ter-rgg9-20261105/screenshots/deck-000059.png` | 91 | The person moves through the cabin doorway. |
| `/private/tmp/ter-rgg9-20261105/screenshots/deck-000119.png` | 151 | The person moves left onto the deck. |

Fine floor speckle remains visible. No image-quality improvement or new reference-image approval is claimed.
All 120 PNG captures remain in the retained scratch folder.

## Initial implementation validation

The code and build-input tree is `d22c4fcb51fae5ecf942d661aecdee2341d7b3b8`.
`tools/cleanup.sh` completed on the one changed C# file before the final checks.
Final full-suite and benchmark outcomes are also retained in
[validation.json](portable-screen-walk/validation.json), with hashes of the complete logs.

| Required check | Result |
|---|---|
| Complete Checked solution, under the GPU lock | 9,948 passed, 20 skipped, zero failures; 4m 01s. |
| Complete Release solution, under the GPU lock | 9,935 passed, 20 skipped, zero failures; 3m 57s. |
| Release benchmark compilation | Passed, zero warnings and errors. |
| New compiled-operation tests | Three intended failures on the baseline; all three pass after the port and in both complete suites. |
| Saved-ray replay against the integrated baseline | 3,038,431 rays, zero changed hit identities. |
| Real deck history observation | 120 advancing shown frames, no history reset after the first. |

The unchanged skips include ter-186q's far-vanishing-point walk regression, opt-in measurements,
and the isolated-pass timestamp assertion that cannot run on this late-latching driver.
The temporary replay and history-observation drivers are absent from the complete suite builds.

The two extracted current-walk modules and both real ray shaders pass `spirv-val --target-env vulkan1.3`.
The real ray shaders retain `Shader`, `SampledCubeArray`, `StorageImageExtendedFormats` and `ImageQuery` capabilities.
The port adds no required device feature.
The retained `tools/sol-walk/loop-reads.py` finds five integer-words record accesses outside the loop and zero inside,
for both extracted current-walk builds. The buffer-specific expressions match those five reads, not an empty set.
SPIRV-Cross is not in the default shell, so this offline inspection uses the package from the flake's pinned nixpkgs
through a temporary Nix shell inside the project dev shell. It installs nothing globally.
The initial unmatched-buffer expression and unavailable default-shell command are not accepted as evidence.

Coverage and mutation testing are intentionally absent because the parent owns that pass.

## Review correction: one decoder owner

The review of `1d037b55c7309a5eeed65a630f05f82d5991f661` found duplicated
octahedral reconstruction and lower-hemisphere folding in `hitsPixel` and `octDecode`.
No current rendering failure was claimed. The defect was coordinated maintenance:
a correction in the encoding owner could leave the walk using the old reconstruction.

`octahedral.glsl` now owns both operations in `octDecodeUnnormalized`.
The facing test calls that function without normalizing its result.
The shading decoder remains:

```glsl
vec3 octDecode(vec2 encoded)
{
    return normalize(octDecodeUnnormalized(encoded));
}
```

where

- `encoded` holds the two stored octahedral direction coordinates, each in [−1, 1].
- `octDecodeUnnormalized` reconstructs the three-component direction and folds the lower hemisphere.
- `normalize` scales a nonzero direction to length one.
- `octDecode` returns that unit direction for existing shading callers.

The reconstruction expression and folding arithmetic keep their original order.
The bounds and loop predicates, slab and cell tracking, images, words inputs and caps are unchanged.
This is a behavior-preserving extraction, not a new runtime fix or an algorithm choice.
The existing behavior tests and compiled-operation probes are unchanged.

The records in the preceding sections describe the initial implementation, not this correction's source identity.
The correction's separate source, shader, replay, timing and validation evidence is retained in
`portable-screen-walk/review-fix.json` and `/private/tmp/ter-rgg9-review-fix-20261105`.

### Correction validation and assessment limits

The correction's code/test/build-input tree is `49b4137202ce507ffa92c86f45a662ac0788f508`.
The three existing compiled-operation probes pass both before and after extraction.
After stripping debug metadata and compacting identifiers, all 63 production shader modules
are byte-identical to the reviewed head's modules. This includes normalized shading consumers
and both real ray kernels; the extraction preserves their instruction and arithmetic order.
All 82 production/test modules and both extracted replay modules pass Vulkan 1.3 SPIR-V validation.
Three test-only modules remain byte-different after identifier compaction;
the production-module equality does not claim binary equality for those probes.

A fresh GPU-locked replay preserves all 3,038,431 active saved-ray hit identities.
All 32 before/after outcome files are byte-identical to their retained pre-extraction counterparts,
including their stored hit-entry values. The captured inputs were independently rehashed.
The historical V6 mismatch remains 195,044 identities; its earlier speed projection still does not apply.
The finite corpus does not establish universal floating-point equivalence between normalized
and unnormalized facing for every possible grazing direction.

Fresh whole-frame measurements compare the integrated base against the final shared-decoder port,
not the extraction against its reviewed predecessor. Three alternating 120-frame runs per build
use the same 1280 × 720 film, two captures in flight and the project GPU lock.
Every run has mean, p10 and p90 GPU clocks of 1296 MHz.

| Round | Integrated-base frame mean, ms | Shared-decoder port frame mean, ms | Integrated-base frame p95, ms | Shared-decoder port frame p95, ms |
|---|---:|---:|---:|---:|
| 1 | 11.290 | 11.182 | 11.562 | 11.474 |
| 2 | 11.337 | 11.202 | 11.570 | 11.484 |
| 3 | 11.324 | 11.203 | 11.622 | 11.490 |

The mean frame span is 11.317 ms before and 11.196 ms after: a 0.121-ms reduction, or 1.07%.
The final mean is 0.804 ms below the 12-ms budget, and every measured p95 stays below that budget.
This is not a new speed claim for the decoder extraction; its production instructions are unchanged.
The neighboring trace interval remains recorded only as an interval, not isolated pass-own GPU cost.
The results concern this deck stretch on this Mac, not Linux or every possible frame.

Fresh real-renderer captures advance from draw index 32 to 151 over 120 distinct snapshot times,
with alternating history-image parity and no reset after the first shown frame.
The final capture run averages 820.1 MHz, with p10 644.0 and p90 942.6 MHz.
The PNG capture run's 16.292-ms mean frame span is excluded from the top-clock budget comparison.
The first capture attempt omitted the clock sampler. Its raw evidence remains in `screenshots-unclocked`
and `history-unclocked.json`; a fresh sampled run supplies the final inspected captures.
The three final capture paths and one-line inspections are in `review-fix.json`.
No new image-quality improvement or reference-image approval is claimed.

| Required correction check | Result |
|---|---|
| C# cleanup on the ticket's changed test file | Passed; no further source change. |
| Complete Checked solution under the GPU lock | 9,948 passed, 20 skipped, zero failures; 3m 52s 884ms. |
| Complete Release solution under the GPU lock | 9,935 passed, 20 skipped, zero failures; 3m 53s 217ms. |
| Release benchmark compilation | Passed, zero warnings and errors. |
| Existing CPU reference and real-shader correctness tests | Unchanged; included in both complete suites. |
| Temporary replay/observation drivers | Absent from the final Checked and Release test assemblies. |

The review's execution-evidence gap is resolved by these fresh builds, tests, validations and GPU dispatches.
ter-186q remains an existing skipped regression, not a fix supplied by this ticket.
Linux runtime and performance remain explicitly user-deferred under ter-cgif.
Coverage, mutation testing, integration, push and ticket closure remain parent-owned and were not performed.
These are retained scope limits, not silently claimed checks or new design forks.
