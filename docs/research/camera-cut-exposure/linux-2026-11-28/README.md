# Linux comparison stopped by baseline Vulkan validation

The sole approved job ended `FAILED` before any matched timing run. Linux acceptance is **HOLD**. The failure occurred in the baseline's independent outside reference at tick 1417. It does not establish a camera-cut regression.

## Terminal result and cost

| Quantity | Result |
|---|---|
| Job | `40d9f654-f5dc-4671-8a58-01c21712037f-u2` |
| Endpoint / worker | `shmmav4u60m5u8` / `ui9kej3wnuyoxt` |
| Image | `8cb5570f06810215acaa4311758417d7d159aca1` |
| Baseline | `19e17c5ba364392ca6bf84b00c11a7d709771ded` |
| Candidate input | `a7a78944450584ad1c2ed2d0f3d38c583d80725c` |
| Provider status / canonical result | `FAILED` / `failed` |
| Original client / handler command exit | `1` / `1` |
| Execution / queue delay | 302.108 s / 21.098 s |
| Canonical counted cost | $0.05953728 |
| Approved command / execution limits | 840 s / 900 s |
| Approved maximum cost | $0.1552 |
| Submissions / automatic retries | 1 / 0 |

The canonical cost uses the reported execution time plus 60 seconds for worker start and 10 seconds for idle, at $0.00016 per second. The provider invoice was not independently reconciled. The original client collected the terminal response and settled its pending audit record. No second client or job submitted work.

## Evidence reached before failure

- The device was NVIDIA RTX 2000 Ada Generation, UUID `GPU-41976f7e-5e33-342b-ff31-97707cb59880`, driver `595.112.07`.
- Both pinned trees passed the initial dev-shell, asset-source and converter hash comparison. The approved NOAA mirror passed its size and hash checks.
- The canonical asset setup passed. All ten cooked GLBs and Earth `material.zip` matched manifest `210510fe09f7195174b57d4491223a5721fc03c6a33325b4e24bbf0063319f75`. The worker checked 68 pinned source files and configured one absolute content root for both trees.
- Baseline diagnostics observed 18 drawn frames, numbered 0 through 17, with native render and output dimensions of 1280 by 720. Validation was enabled. The baseline meter had 256 bins, and each presentation had the planned eight-byte exposure/pixel observation.
- Nine cold outside captures produced 18 scene draws. The last capture contained two draws and two presentations. Its exposure was -7.641465 stops, versus -7.641466 for the previous capture. Both center RGB readings were `[56, 55, 50]` on the 0 to 255 sRGB code scale. One stop doubles the exposure multiplier. These values converged, but validation failure prevents their use as an accepted reference.
- The worker's final restoration report confirms an empty tracked production diff in both trees. The first diagnostic build passed. The failure prevented pristine rebuilds and their assembly hashes.

## Validation failure

The [raw diagnostic log](diagnostic-before.log) records one failed test. Its raw row file retains three `VUID-vkAllocateMemory-pAllocateInfo-01713` messages. They report allocation requests of 268435456, 536870912 and 1069547520 bytes from heap 2, whose reported size is 257949696 bytes.

The renderer completed the reference captures, then the harness rejected its validation messages. The test process exited 1. The worker restored diagnostic source in its final cleanup and returned the failure. [Issue #35](https://github.com/alexispurslane/tertius/issues/35) records this defect and the allocator regression needed to address it. The allocator's preference and fallback path needs confirmation before a fix.

## Limits and next stage

The job never reached candidate diagnostics, FSR diagnostics, synthetic diagnostics, the capacity estimate or either reversed matched round. It produced no ordinary unobserved wall/GPU samples, matched clock log, complete diagnostic cost comparison or cross-tree reference comparison. End-of-run asset and source comparisons were not reached. Initial provenance and source restoration are separate evidence from those missing checks.

The existing analyzer was not run because it requires a completed worker phase. No first destination cut was measured, so neither first-presentation latency nor recovery censoring has a result. The planned latency boundary remains the camera request through completion of an offscreen Vulkan submission's fence. It excludes desktop composition, vblank and display scanout. Baseline cold-reference timestamps are diagnostic observations, not ordinary-frame performance evidence.

Production source still matches the hardened and captured `cf84f02bc5fabfedb22c2b17835ae0d194c4cb91` seed. The earlier 43 scripted mutant kills, 36 covered changed lines, 26 covered changed branches, 201 game PNGs and full Rendering Checked result of 3742 passed with 12 skipped remain applicable. This evidence-only stage repeated none of those runs.

The next authorized stage is the parent's single fresh SOLhigh read-only review. The parent owns required CI, the metrics review and merge. Resolving the Linux validation prerequisite and collecting the missing Linux measurements remain necessary before declaring that comparison complete. No paid retry is authorized by this result.

## Preserved artifacts

[terminal.json](terminal.json) contains the terminal receipt, scope limits and validation messages. [archive-manifest.json](archive-manifest.json) gives every preserved file's byte count and SHA-256. [terminal-evidence.tar.gz](terminal-evidence.tar.gz) contains all returned files, decompressed and gzip raw rows, the full canonical audit row, original client logs and exit, approval, exact submitted payload and analysis inputs. [baseline-native-cut-rows.json.gz](baseline-native-cut-rows.json.gz) also exposes the raw rows directly.

The original artifact root is `/Users/alexispurslane/Movies/Blue Danube/exposure-bloom-recovery/issue-4-evidence/`. Publication adds no renderer or harness behavior change.
