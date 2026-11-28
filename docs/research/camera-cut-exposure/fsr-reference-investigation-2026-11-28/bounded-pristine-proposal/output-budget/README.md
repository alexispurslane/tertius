# Complete evidence within the existing output limit

The proposal is ready as to output admission. It remains inert. Duration policy and paid submission remain separate parent decisions.

The old worker accepted 6,100,000 compressed bytes, then the real canonical collector omitted that measurement file. The [red regression](red.log) records that failure against worker source from `ae0cc3b49b09d3f51f5642225e6ec534b2e5a9a7`. No service call or submission occurred.

The corrected worker returns one archive and one status file. It includes raw measurement rows without truncation, their provenance, the eight pinned diagnostic gzip files, clocks and all logs. It includes final asset checks, source manifests, phase and restoration records. It omits a redundant raw/gzip copy only after verifying that both copies contain exactly the same bytes. Diagnostic gzip files keep their original pinned bytes. New measurement rows remain raw JSON inside the archive.

The exact output settings in [worker.py](../worker.py) are:

```python
OUTPUT_GLOBS = ['linux-out/return/*']
OUTPUT_LIMIT = 8_000_000
FINAL_METADATA_RESERVE = 65_536  # Base64 bytes, included within OUTPUT_LIMIT.
```

where

- `OUTPUT_GLOBS` selects only the finalized return folder. It excludes the worker's unpacked evidence and any rejected archive.
- `OUTPUT_LIMIT` is the canonical handler's unchanged aggregate limit for base64 file contents, in bytes.
- `FINAL_METADATA_RESERVE` reserves base64 bytes for the final status file inside that aggregate limit.

The archive can occupy at most 7,934,464 base64 bytes, or 5,950,848 raw archive bytes. The worker counts base64 padding separately for every returned file. The handler's separate 200,000-byte log tail, result JSON keys and command-step records do not consume its aggregate file-content limit. They still contribute to the service response and share its return deadline.

## Final writes and failure behavior

1. The worker finishes measurement analysis, final asset checks and final source manifests.
2. Its `finally` block writes source restoration and the final phase. This block also runs after a measurement failure.
3. It writes an archive manifest with file sizes and SHA-256 hashes, then compresses the evidence once. Normalized archive timestamps make repeated size checks reproducible.
4. It admits the archive only when the archive fits with the full status reserve. It then writes the status file, measures both returned files with per-file base64 rounding, and makes no later selected-file writes.

If required output is absent or restoration fails, the worker cannot report complete evidence. If measurement fails, a fitting archive retains its partial rows and failure records. If the archive exceeds the budget, the worker returns only a small `overflow` report, including the last phase and restoration result. All measurements then remain only on the worker. They are unavailable to acceptance and can disappear when that worker is reclaimed. A packaging error also produces a small explicit failure report. These failures cause a nonzero worker exit. None causes an automatic retry.

The [analyzer](../analyze_runs.py) requires a successful canonical result with no skipped files. It verifies the archive hash and every manifest entry. It checks the eight retained diagnostic hashes against the archived config before analyzing all four visits. A missing archive, required row or retained diagnostic fails analysis. Raw censoring fields remain in the rows. The original first-presentation and zero-followup checks stay in force.

## Offline evidence

The [offline report](offline-check.json) uses the actual canonical `handler.collect` and the exact emitted output glob. An audit hook forbids sockets and child processes. It imports neither credentials nor a submission client.

The accepted replay contains every saved top-level Linux diagnostic/provenance/log file, all eight pinned diagnostic gzip files, and the unchanged archived local measurement rows. It repeats each local visit into two rounds solely to exercise transport and analysis. It simulates final writes and adds random clock padding. It supplies no new Linux measurements, independent timing rounds or new source-restoration evidence. The older archived fixture lacks derived size fields, so this replay alone uses `require_sizes=False`. Production analysis defaults to checking those fields.

| Replay | Raw archive bytes | Archive base64 bytes | Total returned base64 bytes | Collector skips | Result |
|---|---:|---:|---:|---:|---|
| Saved evidence and final-write replay | 580,543 | 774,060 | 775,052 | 0 | Complete archive and status. Analyzer consumes 24 summary rows. |
| Same evidence plus a 6,100,000-byte random log | 6,684,088 | 8,912,120 | 1,224 | 0 | Explicit overflow. Only status returns. |

The [13 focused tests](../test_output_budget.py) pass. [Their log](tests.log) covers these behaviors:

- Accepted and rejected payloads retain the required final metadata with zero collector skips.
- The archive at the reserved size boundary is accepted. A limit one byte smaller rejects it.
- One hundred one-byte files need 400 base64 bytes. A 399-byte limit skips one file.
- Failure finalization archives the final phase and restoration record. Packaging errors retain failure metadata.
- Returned analysis rejects a missing archive, changed archive, skipped output, missing measurement row and missing pinned diagnostic.
- The inert emitter embeds every required input, uses the exact sealed return glob, and preserves false approval flags.

No engine build, capture, mutation run, full suite or delegated review was repeated. Engine source, fixture C#, old builds, smoke evidence and diagnostic artifacts remain unchanged. The source hashes in the offline report pin the corrected worker and analyzer separately from those earlier engine checks.

## Inert command and remaining decision

The [inert emitter](../emit_proposal.py) adapts the existing embedded-payload command. It always starts its PowerShell output with `throw`. Its payload includes the worker, fixture, analyzer, config, three film scripts and all eight retained diagnostics. It creates no new submission mechanism, isolated config option or endpoint selector. [The emitted metadata](proposal.json) records every payload hash. The external review copy is `/Users/alexispurslane/Movies/Blue Danube/exposure-bloom-recovery/issue-4-evidence/design-a-linux-proposal/bounded-pristine-proposal/proposal.ps1`.

The proposed scope remains four fresh pristine processes: baseline/candidate, then candidate/baseline. Each process covers both real film cuts and 120 ordinary frames per mode, in native and FSR modes. Each ordinary lane discards 24 warmup frames and retains 96 wall samples and 95 aligned GPU samples. Each cut observes one presentation with zero recovery followups.

| Pin or bound | Value |
|---|---|
| Baseline commit | `b002351eb48d33f008f8333e53e61345de3260cd` |
| Candidate engine commit | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| Fixture C# SHA-256 | `70cc38ab48e94728d9706609cd8d3f343eb2ab5ded8ad71f183f3e5dc11b106d` |
| Proposed command / execution seconds | 1,920 / 1,980 |
| Conditional canonical reservation | $0.328 |
| Checked-in command / execution seconds | 840 / 900 |
| Reservation under checked-in policy | $0.1552, insufficient for this scope |
| Authorized submissions | 0 |

No timing or cost bound changed. The existing capacity projection includes final verification reserve. Local archive time is recorded only as local packaging feedback. It gives no Linux timing guarantee. A hard service timeout can still interrupt finalization or output delivery.

The remaining real fork is the [duration policy decision](../duration-policy/README.md#owner-decision-and-rollback). The canonical CLI has no supported isolated config option. Approving the exact two-field duration diff exposes that submitting checkout's other GPU jobs to higher limits. Normal canonical routing can create an endpoint with a higher default or adjust worker counts. Keeping the current policy requires replanning the scope or a separately designed isolated path. The parent must settle that choice and separately authorize spending. This correction supplies neither approval.

## Replay

Run from the repository root. Both commands stay offline:

```powershell
nix develop --command python3 -I docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/test_output_budget.py
nix develop --command python3 -I docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/output-budget/offline_check.py --out '/tmp/tertius-issue4-output-replay'
```

The real returned analyzer uses its default size checks:

```powershell
nix develop --command python3 -I docs/research/camera-cut-exposure/fsr-reference-investigation-2026-11-28/bounded-pristine-proposal/analyze_runs.py --returned '<canonical result folder>'
```
