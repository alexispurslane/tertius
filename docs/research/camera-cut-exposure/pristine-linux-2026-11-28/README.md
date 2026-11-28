# Pristine Linux camera-cut measurements

The approved job `f236f0d9-689b-43ad-9bd4-58f0fd11d73e-u2` completed. The reviewed analyzer passed all observation checks on the complete, hash-verified return. The candidate applied correct exposure in all eight first-frame observations. The baseline failed all eight. Each cut used one scene draw and one presentation.

This supplies the previously missing Linux measurements for both real film cuts and ordinary frames. The parent retains the overall issue #4 acceptance decision, PR readiness, required CI and current-head metrics, merge and issue closure. [PR #32](https://github.com/alexispurslane/tertius/pull/32) remains draft and In Progress.

## Inputs and execution

| Quantity | Recorded value |
|---|---|
| Baseline source | `b002351eb48d33f008f8333e53e61345de3260cd` |
| Candidate source | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| Immutable harness | `6f14335c629b25e36899699006c268026d33ce7e` |
| Reviewed launch HEAD | `b262210823b2db859129a46388f1fc0bbb0ff858` |
| GPU | NVIDIA RTX 2000 Ada Generation |
| GPU UUID | `GPU-b2f4d4ec-fd41-90c9-c419-7cf1a97fbb71` |
| NVIDIA driver | `595.112.07` |
| Endpoint | `shmmav4u60m5u8` |
| Image commit | `8cb5570f06810215acaa4311758417d7d159aca1` |
| Native render / output dimensions | 1280 × 720 / 1280 × 720 pixels |
| FSR render / output dimensions | 848 × 477 / 1280 × 720 pixels |
| Process order | Baseline, candidate, candidate, baseline |
| Canonical execution / queue time | 691.085 / 6.535 seconds |
| Canonical counted cost | $0.1217736 |
| Approved maximum modeled cost | $0.328 |
| Approved command / execution cap | 1920 / 1980 seconds |
| Paid submissions / retries | One / zero |

The canonical cost includes its unchanged 60-second start allowance and 10-second idle allowance at $0.00016 per second. Execution time alone contributes $0.1105736. No individual final provider invoice was returned. [The canonical audit](canonical-audit.json) records the exact counted value.

Each fresh process ran both rendering modes and both measurement lanes. Production source remained pristine. There were no new diagnostic processes or separate warmup processes. Ordinary frames discard their first 24 draws within each process. The launch used the same three source pins and all 15 immutable payload hashes verified before approval. [The offline transport proof](../issue37-linux-proof-2026-11-28/README.md) retains the command-size regression and exact command.

## First frame after each cut

Request-to-fence latency is elapsed wall time from requesting the film's camera change until the first offscreen Vulkan submission's fence completes. It includes the intervening script work and every preparatory GPU pass. It excludes desktop compositor and display refresh time. The cut lane includes a full image capture and an eight-byte observation of exposure and center color. It therefore does not estimate capture-free interactive cut latency.

Correct means that applied exposure differs from an independently settled identical destination by at most 1/16 stop, and each center color channel differs by at most two sRGB code values. One stop doubles the exposure multiplier. Color code values range from 0 to 255. The maximum candidate error was 0.0000394 stop, with identical center colors. The analyzer also verified agreement among destination references across both source pins and rounds.

| Mode | Film cut | Baseline first frame, round 1 / 2 (ms) | Candidate first correct frame, round 1 / 2 (ms) |
|---|---|---:|---:|
| Native | Outside, tick 1417 | 14.684 / 14.754 | 14.308 / 14.377 |
| Native | Return, tick 1462 | 17.267 / 16.745 | 18.604 / 18.946 |
| FSR | Outside, tick 1417 | 12.864 / 12.689 | 12.406 / 12.743 |
| FSR | Return, tick 1462 | 16.308 / 15.759 | 16.573 / 16.554 |

Every baseline first frame was incorrect. No follow-up frame was observed, so its time to a correct frame is unknown. These eight observations are right-censored: observation ended before a correct frame appeared. Their finite first-frame times must not be reported as baseline first-correct times or used to claim a first-correct speedup. Every candidate first frame was correct. There were no hidden extra scene draws or presentations in either tree.

## Ordinary frames without image observations

Ordinary wall time covers the renderer call through its offscreen fence wait. Each lane advances 120 real world ticks, discards 24 frames, and measures 96 wall intervals. Ordinary frames request no image captures and copy no exposure or pixel observation bytes. The 95 retained GPU timings belong to GPU frame identities 25 through 119, inside those wall intervals. Identity 24 is excluded; the last wall draw has no returned GPU timing. These are interval-contained samples, not 96 paired CPU/GPU measurements.

| Mode | Round | Baseline wall median (ms) | Candidate wall median (ms) | Baseline GPU median (ms) | Candidate GPU median (ms) |
|---|---:|---:|---:|---:|---:|
| Native | 1 | 7.966 | 8.190 | 6.586 | 6.806 |
| Native | 2 | 7.951 | 8.189 | 6.583 | 6.812 |
| FSR | 1 | 6.227 | 6.310 | 4.745 | 4.835 |
| FSR | 2 | 6.234 | 6.331 | 4.736 | 4.829 |

| Mode | Round | Candidate minus baseline wall median (ms) | Candidate minus baseline GPU median (ms) |
|---|---:|---:|---:|
| Native | 1 | +0.224246 | +0.220288 |
| Native | 2 | +0.238861 | +0.228160 |
| FSR | 1 | +0.082708 | +0.089504 |
| FSR | 2 | +0.096411 | +0.092320 |

The candidate's correct cut frames take 12.406 to 18.946 ms in the observed cut lane, compared with ordinary wall medians of 6.310 to 8.190 ms. The cut lane includes captures, observation copies and camera-change preparation; the ordinary lane excludes captures and observation copies. This comparison describes those measured paths. It does not isolate the cost of exposure metering. Two reversed rounds on one GPU do not establish a population confidence interval or a universal frame-time bound.

## Complete evidence and pristine source

The canonical collector returned exactly `linux-out/return/evidence.tar.gz` and `linux-out/return/status.json`, with zero skips. [status.json](status.json) reports complete evidence, no missing required files and 478,396 aggregate base64 bytes, below the 8,000,000-byte limit.

The [unaltered measurement archive](evidence.tar.gz) is 358,027 bytes, SHA-256 `97dbfcf1b9836e6ea23a463ad78168a29cd32de3bde0541f0403ce56ff2856aa`. It contains 47 evidence files and [their manifest](evidence-manifest.json), for 48 members total. Every recorded size and SHA-256 was verified. All 16 raw measurement row files, eight retained diagnostic files, process logs, clocks, device identity, build identities and provenance returned.

[evidence-verification.json](evidence-verification.json) records these checks:

- Both pristine build records name the exact measured commits and report clean production source. The embedded C# fixture and worker hashes match the approved inputs.
- Cooked asset records are identical before and after measurement. The cooked manifest hash remains `210510fe09f7195174b57d4491223a5721fc03c6a33325b4e24bbf0063319f75`.
- Asset sources, converter inputs and dev-shell sources match across both pins and remain unchanged after measurement. Only the three permitted runtime approval fields differ from the immutable inert config.

The unchanged [reviewed analyzer](../../../../tools/camera-cut-pristine/analyze_runs.py), SHA-256 `b97ee302a35e6dddf3fdc4ab1ba424479deb114af460d9cafbca6fed182312af`, passed on the complete canonical return. [analyzed.json](analyzed.json) preserves its 24 summary rows and four paired differences. Replay uses the original external return:

```powershell
nix develop --command python3 -I tools/camera-cut-pristine/analyze_runs.py --returned '/tmp/tertius-issue37-one-attempt/returned'
```

This run used validation-disabled pristine production timing. The separately labeled retained Linux diagnostics still cover the outside cut and one extreme synthetic case per mode and pin. They do not supply Linux return-cut validation, ordinary synthetic sources, the second extreme source, or fixed/continuous controls. Earlier Mac correctness checks and the independent raw-scene reference for extreme FSR remain separate evidence. The historical disagreement with the old synthetic FSR reference remains recorded; tolerances did not change.

## Restoration and completion

The original canonical client ended with exit code zero. The original wrapper restored configuration before any publication. [restoration.json](restoration.json) verifies exact original bytes, SHA-256 `d6579e48c7fa847e54c07e2cfac3757a77451882882ea4d4856bfd2920cbc52f`, and the original 840 command / 900 execution seconds. The checkout was clean immediately after restoration.

API readback found the original GPU endpoint at 900,000 ms and CPU endpoint at 7,200,000 ms. Both records, including worker limits, match the [before snapshot](endpoints-before.json) and [after snapshot](endpoints-after.json). Routing created or removed no endpoint. Cleanup changed no endpoint. No temporary policy edit is committed.

[no-live-command.json](no-live-command.json) records that original wrapper PID 83865 and canonical client PID 83868 ended and no pending entry remains for this job. The exclusive consumed marker remains present. No replacement client, wrapper, job, reviewer or local heavy test started during recovery. The earlier failed job `886d3f2c…` is separate history.

[terminal.json](terminal.json) holds the outcome and remaining ownership. [archive-manifest.json](archive-manifest.json) hashes every published machine-evidence file and the original external records. Published receipt and audit records omit embedded command bytes and retain their hashes. The published result omits the successful command log and replaces its step command with its hash. Original records, temporary policy bytes and executable launch files remain in `/tmp/tertius-issue37-one-attempt`.

The new work in this publication is evidence and documentation. No engine change, repeated build suite, mutation run, diagnostic run or screenshot capture was needed. The normal commit and push hooks still apply. Required CI and metrics must pass on the final ready PR head before the parent merges.
