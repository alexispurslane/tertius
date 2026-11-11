---
id: te-vnjc
status: in_progress
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

