# Pristine Linux attempt failed before measurement

Job `886d3f2c-6e01-406e-a4f4-50b51d417e1f-u1` failed while the canonical handler tried to start Nix. Linux rejected its single 409,197-byte command with `OSError: [Errno 7] Argument list too long: 'nix'`. The measurement worker never started. The preflight review checked payload identities, duration and return size, but missed the Linux command-argument limit. [Issue #37](https://github.com/alexispurslane/tertius/issues/37) tracks that defect.

Linux acceptance remains **HOLD**. This is transport failure evidence. It supplies no benchmark data.

| Recorded quantity | Result |
|---|---|
| Baseline source | `b002351eb48d33f008f8333e53e61345de3260cd` |
| Candidate source | `588b6d2747560c73aac542fb25ce80eec1ba8db7` |
| Reviewed proposal head | `4898a0de22edceeb5e680f5bdba193e50ff1c367` |
| Endpoint / image commit | `shmmav4u60m5u8` / `8cb5570f06810215acaa4311758417d7d159aca1` |
| Authorized command / execution limit | 1,920 / 1,980 seconds |
| Authorized modeled reservation | $0.328 |
| Runpod execution / queue time | 7.915 / 5.517 seconds |
| Canonical counted cost | $0.0124664 |
| Returned worker files | None |
| Output-collector skips | Unknown: the handler raised before collection |
| Paid submissions / retries | One / zero |

The canonical counted cost uses Runpod's execution time and the existing start/idle allowances at $0.00016 per second. Execution time alone contributes $0.0012664. No final individual provider invoice was returned.

## Restoration

The durable tmux wrapper restored the original local configuration before making cleanup API calls. The original SHA-256 is `d6579e48c7fa847e54c07e2cfac3757a77451882882ea4d4856bfd2920cbc52f`. The restored GPU policy allows 840 command seconds and 900 execution seconds. The checkout was clean immediately after restoration.

Supported API readback confirmed the pre-existing GPU endpoint's default remained 900,000 ms. The CPU endpoint's default remained 7,200,000 ms. Routing created no endpoint, and cleanup changed no endpoint. [restoration.json](restoration.json) and the before/after endpoint records preserve the proof.

## Missing evidence

Neither film cut at ticks 1417 and 1462 ran in either native or FSR mode. No ordinary frames, reversed timing rounds, first-correct latency or incorrect-first-frame censoring observations returned. The worker phase, checksum manifest and sealed measurement archive are absent. The reviewed analyzer did not run on this incomplete return. The earlier Linux diagnostics and separately labeled local correctness evidence remain unchanged.

The single approval is consumed. There is no paid retry. The parent retains duration-policy approval, new spending approval, readiness, required CI and metrics, and merge.

## Retained records

[terminal.json](terminal.json) states the outcome and limits. [result.json](result.json) retains the full Runpod SDK exception. The [evidence archive](terminal-evidence.tar.gz) and [manifest](archive-manifest.json) preserve approval, receipt, counted cost, canonical logs, endpoint records and restoration. Published receipt and audit records retain the exact command's SHA-256 while its full bytes remain in the external folder named by the manifest. The temporary policy bytes and active measurement config remain external.

The evidence files and archive were checked against every recorded size and SHA-256. No new renderer run, diagnostic run, reviewer, build suite, histogram replay or screenshot capture was performed for this publication.
