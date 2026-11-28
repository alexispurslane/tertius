# Corrected Linux run stopped at its capacity check

Linux acceptance remains **HOLD**. The corrected job completed both trees' validating diagnostics, restored production source and rebuilt pristine assemblies. Its capacity estimate then stopped the job before either matched timing round. The candidate passed its four diagnostic cut comparisons against its own references. The synthetic FSR references disagree between trees, which independently blocks shared correctness.

## Terminal result and cost

| Quantity | Result |
|---|---|
| Job | `e1315547-2360-47f4-b91d-681cf8fe10c0-u1` |
| Endpoint / worker | `shmmav4u60m5u8` / `rdbu7v3vrgdxmz` |
| Worker image | `8cb5570f06810215acaa4311758417d7d159aca1` |
| Baseline | `b002351eb48d33f008f8333e53e61345de3260cd` |
| Measured candidate | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| Provider status / canonical result | `FAILED` / `failed` |
| Original client / handler command exit | `1` / `1` |
| Handler command time | 513.980206 s |
| Execution / queue delay | 523.039 s / 7.876 s |
| Canonical counted cost | $0.09488624 |
| Approved command / execution limits | 840 s / 900 s |
| Approved maximum cost | $0.1552 |
| Corrected submissions / automatic retries | 1 / 0 |
| Terminal receipt | 2026-11-28 06:39:10 UTC |

The [canonical audit](canonical-audit.json) counts reported execution plus 60 seconds for worker start and 10 seconds for idle, at $0.00016 per second. This is the canonical cost model. The provider invoice was not independently reconciled. The original client collected the terminal result and settled its pending record. Both original processes have exited. No second client or job ran.

The [earlier failed job](../linux-2026-11-28/README.md), `40d9f654-f5dc-4671-8a58-01c21712037f-u2`, separately counted $0.05953728. Its result and this corrected attempt remain separate.

## Capacity stop

After diagnostics and pristine rebuilds, the worker projected 1612.708646 seconds of remaining work. Its remaining command allowance was 317.072196 seconds. The reviewed estimate retains its 25% margin and 20-second reserve. It rejected the remaining work and returned exit 1, before any matched process started. The [capacity record](returned/linux-out/capacity.json) retains every input. The [capacity chart](chart.html) compares the remaining allowance and projection. This conservative projection can overestimate pristine work because diagnostics enable validation and extra timestamps. It is not a measured completion time.

## Provenance and restoration

- The GPU was NVIDIA RTX 2000 Ada Generation, UUID `GPU-41976f7e-5e33-342b-ff31-97707cb59880`, driver `595.112.07`. Initial memory was 16380 MiB total and 15949 MiB free, above the 2048 MiB gate.
- Both immutable commits passed the shared asset-source, converter and dev-shell comparison. Both allocator blobs were `f7aae7bd20d48ea3adf0d565ed476a6d2ec54594`. The baseline includes merged #35.
- The NOAA mirror passed its 9,370,338-byte size and SHA-256 checks. One canonical setup produced ten GLBs and Earth `material.zip`, with manifest `210510fe09f7195174b57d4491223a5721fc03c6a33325b4e24bbf0063319f75`. All 68 approved source files passed their checks. Both trees used `/tmp/tertius-job-hyyrkzp5/before/content`.
- All 20 diagnostic validation checks recorded zero messages. Both diagnostic test processes passed. Their builds and both pristine rebuilds exited 0.
- Both [pristine assembly records](returned/linux-out/after-pristine-binaries.json) pin their commit, fixture hash, restored production diff and assembly hashes. The [final restoration record](returned/linux-out/restoration.json) confirms both tracked production diffs were empty after cleanup. No instrumented assembly is claimed as a pristine timing result.
- Real-scene output was 1280 by 720 pixels. Observed native rendering was 1280 by 720; FSR rendering was 848 by 477. Synthetic output was 160 by 90, with native rendering at 160 by 90 and FSR rendering at 96 by 54. Every observed extent and its count remains in the raw and summarized records.

## Partial cut observations

A correct presentation has exposure within 1/16 stop and each center RGB channel within two 8-bit sRGB codes of its own settled destination reference. One stop doubles exposure. These are single diagnostic observations, with validation, extra GPU timestamps and extent logging. They supply no matched performance verdict.

A presentation ends when the offscreen Vulkan submission's fence completes. Request-to-presentation time starts at the camera request. Each cut presentation copies eight observation bytes. Cut recovery also requests pixel captures. These times include that overhead and exclude PNG encoding. They measure no desktop compositor, vblank or display scanout.

| Tree | Mode | Case | First presentation, ms | First correct presentation, ms | API return, ms | First presentation correct? |
|---|---|---|---:|---:|---:|---|
| Baseline | Native | Outside, tick 1417 | 28.190591 | Not reached | 28.248352 | No |
| Candidate | Native | Outside, tick 1417 | 27.266536 | 27.266536 | 27.326607 | Yes |
| Baseline | FSR | Outside, tick 1417 | 27.534043 | Not reached | 27.598235 | No |
| Candidate | FSR | Outside, tick 1417 | 36.802843 | 36.802843 | 36.921657 | Yes |
| Baseline | Native | Synthetic extreme | 6.038143 | Not reached | 6.071323 | No |
| Candidate | Native | Synthetic extreme | 6.132375 | 6.132375 | 6.167086 | Yes |
| Baseline | FSR | Synthetic extreme | 6.983049 | Not reached | 7.024890 | No |
| Candidate | FSR | Synthetic extreme | 7.109171 | 7.109171 | 7.145912 | Yes |

The synthetic extreme changes incident irradiance from 1361 to 10^-12 W/m² with compensation of -13 stops. Every first API call made one scene draw and one presentation. Candidate cuts needed no followup frame. Baseline cuts remained incorrect through 90 followup frames, or 1.5 seconds of additional simulation time. Their first-correct times are right-censored: the observation ended before a correct presentation. The observed wall-time limits were 1622.540105 ms and 1532.782422 ms for native and FSR outside, and 462.554158 ms and 541.013037 ms for native and FSR synthetic extreme. They are not measured recovery times.

The first API return also reports correctness independently. It agrees with first-presentation correctness in all eight rows. [diagnostic-partial.json](diagnostic-partial.json) retains enqueue times, actual presentation timestamps in the linked raw rows, draw counts, exposure, RGB, censoring and diagnostic pass costs. The summary rechecks presentation timestamps and per-tree correctness against raw observations.

## Shared reference failure

| Mode | Destination reference | Baseline exposure, stops | Candidate exposure, stops | Candidate minus baseline, stops | Within shared tolerance? |
|---|---|---:|---:|---:|---|
| Native | Outside | -7.6414650 | -7.6427135 | -0.0012485 | Yes |
| FSR | Outside | -7.6416025 | -7.6427364 | -0.0011339 | Yes |
| Native | Synthetic extreme | 10.0000000 | 10.0000000 | 0.0000000 | Yes |
| FSR | Synthetic extreme | 8.5841560 | 8.9148850 | 0.3307290 | **No** |

All four reference pairs have equal center RGB channels. The synthetic FSR exposure difference exceeds the unchanged 0.0625-stop tolerance. A black center pixel alone does not establish correct exposure. Per-tree cut success therefore cannot establish shared correctness. This report makes no claim about the cause of that reference difference.

## Ordinary frames and remaining evidence

Each of the four diagnostic steady lanes contains 96 wall samples after 24 warmup frames. Each measured API call made one draw and one presentation, requested no capture and copied zero observation bytes. The GPU frame identities are 24 through 119, all unique. The first corresponds to the final warmup draw. Keeping identities 25 through 119 leaves 95 GPU observations inside the measured wall interval; the final wall frame's GPU result is unavailable. These validating, instrumented diagnostics are not pristine ordinary timing evidence. Their identity checks remain in the summary, without a performance claim.

Diagnostic exposure-pass timestamps retain medians and ranges in the summary. Their extra query boundaries and validation prevent use as ordinary costs. Baseline reduction and adaptation share one dispatch, so the timestamps cannot split their individual costs. The source comparison includes the camera-cut implementation and the Design A reflection changes together. It does not isolate image-format cost.

The following required evidence is missing:

1. Both reversed matched rounds: baseline then candidate, followed by candidate then baseline. None of their four pristine processes ran, in either native or FSR mode.
2. The pristine return cut at tick 1462, ordinary synthetic sources, second extreme source, fixed-exposure control and continuous-camera control. Diagnostics intentionally cover only outside and one extreme case per mode.
3. Pristine ordinary wall/GPU medians and ranges, both paired round differences, matched GPU clocks and a matched cut-latency comparison.
4. Final cooked-asset and source comparisons, the final compressed-output gate, the worker's complete phase and the completed-run analyzer. Initial asset checks and final source restoration do not replace these checks.

The reviewed completed-run analyzer was not executed. Its complete-phase gate remains unchanged. No retry, scene substitution, cache substitution, threshold relaxation or new infrastructure followed this stop.

## Publication and handoff

All 34 returned artifacts and the original result are under [returned](returned/result.json). [terminal.json](terminal.json) records the terminal outcome, exact missing results and unchanged source paths. [terminal-evidence.tar.gz](terminal-evidence.tar.gz) preserves 64 files: every returned artifact, approval, submitted command, reviewed worker and fixture, scripts, original client logs, exit, audit and analysis inputs. [archive-manifest.json](archive-manifest.json) records each member's byte count and SHA-256. The provider's returned command log is retained exactly, including its truncated traceback.

This publication changes research evidence only. The measured candidate remains `588b6d2747560c73aac542fb25ce80eec1ba8db7`, tree `76b5d43a9f3f841b19d7d542b93dc0308ba7a43e`. All measured source, tests, tools, analyzers, scripts, native dependencies, Nix inputs and `ARCHITECTURE.md` keep their recorded Git identities. The already published full Rendering Checked run on that source passed 3798 tests and skipped 12, with zero failures. Existing 22 fault injections, hardening and 134 game PNGs were not repeated.

PR #32 stays draft and In Progress. F1 is resolved. The parent owns the next decision, required CI and metrics on the final branch head, and merge. Future paid work needs a new decision. This result authorizes no retry. The evidence push omits the pre-push hook because that hook unconditionally builds the metrics tool and this recovery explicitly forbids local heavy work. The normal documentation commit hook still runs. Required CI and final-head metrics remain pending with the parent.
