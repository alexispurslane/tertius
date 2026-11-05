# Automatic semantic-frame box projection

The original implementation record below describes reviewed commit `46a0f4f244bc3b820e1921c97f654ffc06d63ea5`. The final boundary correction and refreshed evidence are in "Boundary review fix" and [review-fix.json](automatic-box-projection/review-fix.json).

Ticket `ter-7tnv`, November 5, 2026. The settled design is [semantic-frame-box-projection.md](../../plans/semantic-frame-box-projection.md). This replaces the distance-cube search, not its receiving-position ownership fix. The [search report](corrected-environment-misses.md) and its images remain historical evidence. In particular, its +2.762 ms deck and +2.175 ms floor differences are not measurements of this replacement.

## Source and prerequisite

The original review base is `34c28466e641a864fd6a915c029e08f07b3b9ccc`. The original search implementation `d42045d1ee44f4cc64a6e7f99736f523f8f2bb75` and receiving-position fix `a541601534246e87dad7e4c74b51da3f5c693eee` remain in ancestry. The authorized prerequisite is integrated main `7d9f6e6739b8fcd1b96fcd307c6434e3d4a8ebaf`. Its portable screen walk and placement-history fixes are prerequisite changes, not new box-projection work. No unfinished FSR or noise prototype is imported.

The comparison control is the integrated prerequisite plus the previous distance search and a passive appended frame-constant payload. That payload makes the observing test runner binary-compatible with both variants; the control does not fit boxes or use box projection. It is not pristine original main. Preserved control and replacement binaries live under `captures/ter-7tnv-box/{control,box}-bin/`. `control-code-tree.txt`, `box-code-tree.txt` and `capture-inputs.sha256` identify their build inputs and binaries.

## One enclosure per existing group

A projection enclosure is a box used to select a direction in a captured environment map. It is not an ownership boundary, authored room volume or traced surface. Preparation derives the enclosure from the same prepared-instance map assignment that the renderer already uses. There is no second hierarchy or classifier.

- The camera's served semantic chain still has at most 42 maps, outermost first. Off-chain descendants still use their nearest served ancestor. Frame owners still use the frame they own.
- Each instance expands only its assigned group's enclosure. A child capture draws ancestor groups too, but those ancestor groups never enlarge the child's enclosure.
- Preparation fits before main-view visibility culling. Off-screen geometry contributes. Lights without geometry, stars and sky do not.
- The complete prepared mesh radius, including skinning or morph deformation, is multiplied by the largest absolute interpolated scale component. Its sphere supplies conservative box extents. This is not a tight mesh box. An instance's separate culling-radius override does not affect it.
- Centers use the map's semantic-frame axes and eye-relative meters. A common rigid turn or translation does not inflate the local box. Object motion and membership changes can resize it. Preparation clears and rebuilds the derived bounds each time; it retains no smoothed proxy state.

The appended GPU payload uses two four-component vectors per map. The first vector's fourth component marks a populated box. Existing frame-constant offsets stay unchanged; total frame constants grow from 2,416 to 3,760 bytes, below Vulkan's minimum 16,384-byte uniform-buffer range.

## Shared corrected lookup

`environmentSpecular` is the single corrected specular reader. It turns the receiving position and reflection direction into the selected map's axes. `boxProjectedDirection` intersects that ray with the enclosure analytically. A receiver inside the enclosure uses its positive exit. A receiver outside uses its positive entry. An inward boundary ray uses the opposite exit, and an outward boundary ray without a positive intersection stays directional. The direction from the existing capture eye to that point selects the existing prefiltered map. The reader does not change the material roughness.

The slab intersection handles parallel axes explicitly, without division by zero. Finite input checks and coordinate scaling precede intersection. Empty, invalid, degenerate, numerically unusable or non-intersected boxes keep the original finite direction. A zero or non-finite input direction gets finite positive Z. A zero-length or non-finite resulting lookup also falls back. No result depends on a driver turning NaN into a useful value.

One necessary exception preserves sky behavior: when the existing level-zero specular map alpha says that the original direction is fully open sky, the reader leaves that direction unchanged. The pure enclosure model moved those reads onto a wall in two exact sky controls. The alpha guard restores both controls byte for byte. It adds no texture, ownership classification or hierarchy walk. Partially mixed radiance remains an approximation.

`ShadingInput.position` remains authoritative. The six-operation receiving-position ownership fix is retained. Diffuse reads remain directional. Stars retain their original world direction. Sky composition, ray counts, settings, hit-light composition, exposure and temporal accumulation policies stay unchanged. The analytic helper returns only a lookup direction. Its intersection distance never enters real reflected-motion or history hit-distance records.

## Behavioral evidence

The initial red run had eight intended behavioral failures and nine passing controls: five missing fitted bounds and three uncorrected lookup directions. A separate real-reader red read about `0.0000219` radiance where the receiving-position-dependent box lookup must read `8 ± 0.005`. A constructor-naming compile error before that run was corrected and is not counted as behavioral red.

The CPU tests cover exclusive assignment, ancestor exclusion, off-screen transformed extents, unchanged culling radii, rigid-frame invariance, the served limit, membership rebuilds and empty light-only groups. Deformation tests cover both skinning and morphing. Existing snapshot-chain tests cover nested/off-chain/owner assignment; existing map-chain tests cover the served limit and capture membership.

Real Vulkan probes cover inside and outside intersections, parallel axes, boundary points, non-intersections, invalid and degenerate boxes, zero lookup, invalid direction, huge/tiny inputs and common rigid rotation. Readback uses RGBA16F; the direction tolerance is one half-float step plus a small arithmetic allowance, not an image-quality relaxation.

The real renderer checks receiving-position-dependent radiance, mirror/glass/off-screen lookup placement, rough openings, unchanged diffuse reads, byte-exact open-sky controls, advancing accumulation and zero real hit distance for bright corrected misses. Old search tests predicted intersections with individual physical walls. Their replacements independently predict the fitted enclosure's lookup direction; the stripe bound stays one cube texel (`0.0625` radians), and the rough-opening bound stays `0.4` meters in captured-wall coordinates. Keeping the old physical-wall expectation would test a different algorithm, not strengthen the enclosure approximation.

## Limits and follow-up

A map can hold several separate objects at different depths. One enclosure cannot recover those depths. A child map also contains ancestor radiance; correcting it with the child's local enclosure remains approximate. Large or sparse root groups can make loose boxes and weak correction. There is no added ownership texture, map compositing scheme or hierarchy walk to hide these limits. Box projection does not fix screen-tracer grain or establish final image-quality acceptance.

The old search and its CPU twin are removed. Distance-cube allocation, face writes, downsampling and descriptor production remain and still cost time. Follow-up `t7cem-d5f9` tracks their removal separately; this reader replacement does not claim those costs as savings.

The parent owns changed-line coverage, mutation testing, final budget and quality acceptance, FSR reconciliation, integration, closure and push. Linux runtime execution remains deferred under `ter-cgif`. Vulkan 1.3 module validation is not Linux execution.

## Warmed real-renderer captures

[provenance.json](automatic-box-projection/provenance.json) retains exact settings, capture scripts, first/last observation rows, image and binary hashes, source identities, timing pairs and observing-runner source. The replacement capture, timing and final test code/build-input identity is `cda9d325a704aa321e2f921a7d3b60144373705d`. The control identity is `6fc8947dc81ba55c7f45ee59dece54d1b0ae8d13`. Their Rendering DLL SHA-256 values are respectively `694e7a5f9591ac811d0679690fbaf982e0381441f68b36d427335469c667e091` and `b4e9bd12960c3b81dd36f8c2006cd41db18765e9bc472c1f8b7437e6d7c3d4ba`. Final Release validation rebuilt exactly the captured replacement DLL.

Both variants use 1280 × 720 output and rendering, SMAA with no FSR, the default ACES-fitted tone curve, environment faces of 128 texels with one face refreshed per advancing frame, and 32 warm-up draws. The screen tracer keeps 256 reflection steps, 62 diffuse steps and maximum reflection roughness 0.5. Auto exposure keeps minimum −14 stops, maximum 10 stops, compensation 0, dark/bright shares 0.1, and dark/light adaptation times 3/1 seconds. Bloom keeps threshold 1, knee 0.5, strength 0.25 and six levels. The capture runner enables neither impostors nor capture-in-flight mode. Full settings are in the provenance file.

| View | Snapshot seconds, first → next | Served maps | Control exposure, stops | Box exposure, stops | Replacement image |
|---|---|---:|---:|---:|---|
| Terminal interior/reflection | 5.233333333333333 → 5.25 | 3 | −2.9894981 | −2.96673 | `captures/ter-7tnv-box/box-terminal/terminal.png` |
| Floor and nearby objects | 52.78333333333333 → 52.8 | 2 | 3.7050846 | 3.6925075 | `captures/ter-7tnv-box/box-floor/floor.png` |
| Nested cabin interior | 16.666666666666668 → 16.683333333333334 | 4 | −8.356823 | −8.340269 | `captures/ter-7tnv-box/box-cabin/cabin.png` |

Each view records draw indices 0 through 33 with exactly one reset. The still's warm-up draws share its snapshot; the next snapshot advances by 1/60 second without resetting. The separate real-renderer history test also checks accumulated specular-history length above one. Both variants' twelve CLI PNGs match their observer counterparts byte for byte. Their simulation dumps, settings and non-box history observation fields match exactly. The small resulting exposure differences come from the unchanged automatic exposure policy, not retuning. Exposure stays constant across each pair of consecutive captures.

The three replacement images were opened and shown to the user with captions. Terminal grain remains severe. The floor shows broad curved background bands and grain in both variants; box projection is not a cure for those artifacts. The cabin shows its frame structure, glazing and shadows in a four-map chain with nonidentity ancestor axes. No reference image was added or re-approved.

The actual enclosures expose the approximation's scale. The terminal's receiving group includes planet-scale geometry: its sphere-derived enclosure is about 12,742,000 meters across. It cannot act as a tight room enclosure. The floor's receiving group spans about 203 × 220 × 203 meters. The cabin's receiving group spans about 17.46 meters per axis despite much larger ancestor groups. No ancestor capture contribution inflates that cabin box. These are limits of the selected group model, not a reason to introduce authored rooms or silently exclude group geometry.

## Bounded whole-frame GPU measurements

A GPU frame span is the elapsed GPU time from the frame's first recorded timestamp to its last, including gaps between passes. It excludes CPU preparation and presentation waits. These measurements do not isolate the lookup's own pass cost or measure CPU enclosure fitting.

Three matched pairs ran for each of the existing deck and floor windows, with 120 measured frames per run. Order was control/box, box/control, control/box. Output stayed 1280 × 720 with no FSR, two frames in flight and discarded film images. Snapshot windows, renderer settings, exposure policy and history policy stayed matched. GPU work used the normal lock and clock recorder. All twelve timed spans had mean, p10 and p90 clock 1296 MHz; all six within-pair comparisons differed by 0%, below the 3% limit.

| Window | Median control run mean, ms | Median box run mean, ms | Median within-pair box minus control, ms |
|---|---:|---:|---:|
| Deck | 14.094 | 11.721 | −2.355 |
| Floor | 12.837 | 10.854 | −1.986 |

Each run mean averages its 120 frame spans. Each middle column is the median of three run means. The final column is the median of three paired differences, not the difference of the middle columns. The first deck pair was preliminary; the table uses the completed bounded set. It compares box projection with the integrated discarded-search control, not with directional-only original main. It makes no universal speed promise and does not establish final budget acceptance. Retained distance producers still run. MoltenVK's individual pass timestamps can include the following pass's work, so no own-pass cost is inferred from them.

The initial timing batch stopped at the 5 GiB free-disk guard after its first pair. No further heavy phase started below that guard. Space recovered without any worker artifact deletion, and the remaining pairs resumed while preserving the first pair. No other worker's outputs, caches or checkout were removed.

## Implementation validation before boundary review

| Check | Result |
|---|---|
| Final focused enclosure cases, Checked | 37 passed, zero failed |
| Final complete deformation class, Checked | 30 passed, including skinning and morph enclosure cases |
| Full Checked solution suite | 9,990 passed, 20 explicit skips, zero failed; 10,010 total |
| Full Release solution suite | 9,977 passed, 20 explicit skips, zero failed; 9,997 total |
| Checked and Release solution builds | Both passed, zero warnings and errors |
| Explicit Release benchmark compilation | Passed, zero warnings and errors |
| Scoped cleanup | Roslyn fixes and CSharpier on the eight owned C# files, then again on the logically grouped reset block |
| Vulkan 1.3 validation | All 89 retained compiled Release engine/test/reference modules passed |
| Code/build-input and binary identity | Capture, timing, final tests and staged source agree; final Release Rendering DLL equals the preserved capture DLL |

Logs are under `captures/ter-7tnv-box/logs/`: `full-Checked.log`, `full-Release.log`, `solution-Checked.log`, `solution-Release.log`, `benchmark.log`, `final-focused.log`, `cleanup-final.log` and `compare-<round>-<window>.log`. Initial capture instrumentation rejected the still's infinite exposure step as JSON, then its fix hit a serializer-options analyzer; both were corrected in the observer only. The first readiness wrapper finished every build/test/module check but caught a trailing blank line created by `tk` in the follow-up ticket. That whitespace was removed; it did not change the tested code identity. Coverage and mutations were not run.

The first normal commit hook requested clearer grouping in the preparation reset block. Two blank lines now separate environment faces/bounds from the neighboring reset groups. No behavior changed. All captures, focused/full suites, solution/benchmark builds, module validation and matched timing pairs were refreshed on the final identity above. The twelve fresh PNGs also equal the previously inspected images byte for byte. The earlier complete evidence set, identity `5f31543cc9014d5343cd986c063e85ef964ccbd0`, is preserved under `captures/ter-7tnv-box-before-reset-grouping/`, including its provenance. The earlier chart used that earlier set; the final table and provenance use only the refreshed set. No process was cancelled: the queue cleared before the proposed redundant timing cancellation, and every refreshed pair completed.

## Boundary review fix, 2026-11-05

This stage started from clean reviewed commit `46a0f4f244bc3b820e1921c97f654ffc06d63ea5` in the same bound checkout and branch. The original review base and all required ticket/prerequisite commits remain ancestors. No further merge, checkout change, agent, workflow, coverage run or mutation run occurred.

### Boundary finding and behavioral evidence

The reviewed helper selected a zero-parameter entry on a boundary. The worker reproduced the defect on all six faces, with both inward and outward directions. The real `environmentSpecular` reader returned zero radiance instead of `8 ± 0.005` from the opposite bright wall. The original outward-boundary probe also failed its revised directional expectation. The red run had 14 intended failures and 36 passing controls, not compilation failures.

The shared helper now rejects an exit at or behind the receiver. It chooses a positive entry when one exists, or the positive exit otherwise. The helper still returns only a lookup direction. No reader receives a proxy hit distance. All 50 focused cases pass, including unchanged diffuse/sky controls, receiving-position-dependent reads, real advancing history and zero real hit distance for corrected misses.

The old outward probe expected the starting face's direction `(1, 1, -4)`. That expectation accepted a zero-parameter self-intersection. Its replacement expects the original direction `(0, 0, -1)`. The half-float tolerance did not change. No physical-wall or image-quality assertion was weakened in this fix.

### Binding-document finding

The stale live definition read:

> `v`, `r_hit` = the distance-cube search's direction and roughness at its hit. Without a hit they are exactly `R⁻¹ d` and `r`.

The live equation and level expression also used `r_hit`. The Vulkan safety row still asserted `r_hit = min(r sqrt(reach), 1)`. These statements described the retired search. The binding document now defines the box-projected lookup direction and keeps material roughness unchanged. Its safety table covers finite inputs, scaled coordinates, explicit parallel axes, strictly positive intersections and nonzero lookup directions. The temporal-renderer plan states the same boundary rule. `ARCHITECTURE.md` was updated last among the engine documents.

### CPU assessment gap

The CPU probe reuses `SceneBatcherBenchmarks.Prepare`, with 20,000 static unit cubes and no deformed instances. Each condition has three interleaved control/box pairs, 64 warm-up calls and 128 measured calls per run. The control is the integrated discarded-search renderer, not original directional-only main. The probe measures complete one-thread preparation, including fitting and constant writes, not an isolated fitting method.

| Map preparation | Control median (ms) | Fixed-box median (ms) | Median paired difference (ms) |
|---|---:|---:|---:|
| Maps disabled | 1.893188 | 1.915792 | +0.022604 |
| One map, one face | 2.341541 | 2.395396 | +0.050604 |
| One map, six faces | 2.624937 | 2.681375 | +0.038041 |
| Four maps, one face | 2.313792 | 2.366833 | +0.030771 |
| Four maps, 24 faces | 5.203646 | 5.251521 | +0.033541 |

The first two time columns are medians of run medians. The difference column is the median of the three within-pair differences. All 30 measured windows allocate zero managed bytes on the calling thread. The record includes each run's stopwatch frequency and samples' summaries. CPU frequency was not pinned. The maps-disabled difference shows background variation, so these numbers do not isolate fitting cost or establish a fixed budget.

### Refreshed captures and GPU evidence

The worker inspected and showed three fresh warmed real-renderer images: terminal interior/reflection, floor/objects and the four-map nested cabin. Each CLI PNG matches its observer PNG and the corresponding reviewed-box PNG byte for byte. All twelve earlier control/replacement PNGs retain their original hashes. Severe terminal grain, doorway saturation and curved floor-background bands remain. No reference image was replaced or re-approved.

The renderer keeps 1280 × 720 output/rendering, SMAA, 128-texel map faces, one face per frame and 32 warm-up draws. The tracer keeps 256 reflection steps, 62 diffuse steps and maximum reflection roughness 0.5. Auto exposure keeps −14/10 stop limits, zero compensation, 0.1 dark/bright shares and 3/1 second adaptation times. Bloom and all other settings remain unchanged. Each capture records draw indices 0–33 with exactly one initial reset. The next snapshot advances by 1/60 second without resetting. The separate history test verifies actual accumulated history above one.

| View | Snapshot seconds, first → next | Maps | Exposure (stops) | Refreshed image |
|---|---|---:|---:|---|
| Terminal | 5.233333333333333 → 5.25 | 3 | −2.96673 | `captures/ter-7tnv-box/boundary-fix-terminal/terminal.png` |
| Floor | 52.78333333333333 → 52.8 | 2 | 3.6925075 | `captures/ter-7tnv-box/boundary-fix-floor/floor.png` |
| Nested cabin | 16.666666666666668 → 16.683333333333334 | 4 | −8.340269 | `captures/ter-7tnv-box/boundary-fix-cabin/cabin.png` |

Three fresh matched 120-frame pairs per deck/floor window compare the reviewed box reader with the boundary-fixed box reader. All twelve spans record mean/p10/p90 clocks of 1296 MHz. Every pair passes the 3% clock comparison. The median paired whole-frame GPU differences are +0.004103 ms on deck and +0.007843 ms on floor. The pair ranges are −0.001493 to +0.008243 ms and −0.019458 to +0.018734 ms. These are bounded observations, not a speedup, an isolated pass cost or final budget acceptance. Both variants retain distance producers. Earlier search-versus-box results remain historical measurements of a different comparison.

### Final fix validation and remaining assessment limits

The tested source/test/build-input tree is `10497903e0e39b2839d943b59b6bb380d54d4515`. The final Release Rendering DLL and capture DLL match SHA-256 `6f78de402f668e2944bee803bc24a0f5365f5a09d392729afb15ae5914d1e7ea`. The structured record retains log hashes, all test-output stamps, binary hashes, scripts, capture settings/history, CPU runner source and GPU pairs.

| Required check | Final fix result |
|---|---|
| Focused enclosure cases, Checked | 50 passed, zero failed |
| Complete deformation class, Checked | 30 passed, zero failed |
| Full Checked solution | 10,003 passed, 20 explicit skips, zero failed; 10,023 total |
| Full Release solution | 9,990 passed, 20 explicit skips, zero failed; 10,010 total |
| Checked/Release solution and Release benchmark builds | Passed, zero warnings and errors |
| Scoped cleanup | Roslyn and CSharpier on the two changed C# test files |
| Vulkan 1.3 module validation | All 90 retained compiled Release modules passed |
| Source/capture/final binary identity | Matching code trees and Release Rendering DLL hashes |

All builds, tests, renders and numerical probes ran through Nix. One heavy command ran at a time, with GPU work under the normal queue. The evidence summarizer initially rejected the solution log's module rows between its header and counts. Its parser was corrected. The completed product tests were not waived or reclassified.

The runtime assessment now has fresh worker-executed tests and captures, not only the reviewer's static derivation. This does not claim that the read-only reviewer ran tests. Linux execution remains deferred under `ter-cgif`. Module validation does not establish Linux execution. Changed-line coverage and mutations remain parent-owned and were not run. Final image-quality/frame-budget acceptance, FSR reconciliation, integration, push and closure remain parent-owned. Mixed ancestor radiance, loose/large/sparse enclosures and retained producer costs remain explicit limits. No unresolved implementation fork remains.
