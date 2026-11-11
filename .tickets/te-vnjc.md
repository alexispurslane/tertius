---
id: te-vnjc
status: closed
deps: []
links: []
created: 2026-11-11T20:31:56Z
type: feature
priority: 1
assignee: Alexis Purslane
---
# Shared mode of the GPU lock for untimed GPU work

Untimed GPU work (stills, films for image metrics, survey splits, GPU tests) waits about an hour behind the exclusive GPU lock, though a take of it keeps the GPU busy about 10 % of the time. Give tools/gpu-lock.sh a shared mode: up to TERTIUS_GPU_SHARED_CAP (3) shared holders at once, never beside an exclusive holder, first in first out across both modes, with the record, liveness, stale and signal rules of the exclusive holder. Timed runs (gpu-trace.sh, the survey's timing part) stay exclusive, and the survey's untimed parts run shared.

## Acceptance Criteria

tools/test_gpu_lock.py passes with tests for each rule. Three headless stills rendered at once equal a still rendered alone, bit for bit. tools/README.md documents the mode and the gap with old helpers.


## Notes

2026-11-11. Design: the shared holders keep the same lock directory, with one record file each in `shared/<ticket>`. Each run takes the GPU only while its ticket is the oldest, so the order is first in, first out across both modes, and the mode of another ticket is never read. Every older helper fails its `mkdir` while shared runs hold the lock, so it waits for them. The one gap left: an older helper never clears a dead shared holder's record. tools/README.md, "The shared mode", says how agents close it.

Tests: `tools/test_gpu_lock.py`, 64 tests in 141 s, and `tools/test_renderer_survey.py`, 51 tests. Both pass. The new lock tests were red first: the old helper read `--shared` as its log.

Bit for bit: the Release demo rendered `/private/tmp/te-vnjc/still.json` (demo-exit, deck camera after 399 ticks, a film of 24 frames, then a still, 1920x1080 headless). One render ran alone, then three ran at once, inside one exclusive take of 48 s. All 24 film frames, the still and `exposure.json` are identical in all four outputs (still SHA-1 `9cebfa79...`). Only `gpu-timings.json` differs, because it holds measured times.

The survey's `run` now also honors `TERTIUS_SURVEY_HOLDS_LOCK=1` with its default chunks. That fixes t1-1zic.
