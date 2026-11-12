---
id: t8-yd4j
status: in_progress
deps: [ter-k7fd]
links: []
created: 2026-11-12T15:35:53Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-vy3m
---
# Cloud tests: a CPU endpoint for Stryker and the CPU test projects, and Rendering shards on the GPU endpoint

Alexis's decision of 2026-11-12 10:34. A serverless endpoint of cpu3c workers with 2 vCPUs (min 0, max 4) runs the official Stryker runs of Core, Simulation and Physics and the CPU-only test projects, with its own execution limit. The GPU endpoint keeps 6 workers; Rendering tests and mutate.py's GPU mutants run there as shards. 6 GPU + 4 CPU is the account's quota of 10: a GPU image switch fits the GPU endpoints into 6 and never lowers the CPU endpoint. One submit command for both kinds, with the same caps and audit log and each kind's price in the cost model. A cloud form of tools/stryker/changed.py with the same table and validity rules. Docs: WORKBENCH.md, tools/runpod/README.md, tools/README.md 'Mutation testing', CLAUDE.md's Testing bullets. The Mac's pre-commit hook, its test record and one Mac pass of the GPU tests before a merge stay.


## Notes

**2026-11-12T18:05:01Z**

Built and measured (2026-11-12).

The layout: GPU 5 workers and CPU 3 (cpu3c, 2 vCPUs, 4 GB, 20 GB disk), with 2 of the account's 10 free for an image switch of either kind. The ticket's GPU 6 + CPU 4 left no room for a switch: a new endpoint got a 400 from the quota and every job was refused. The script never lowers an endpoint, and a route looks only at its own kind's endpoints, so a GPU switch never takes the CPU workers.

Where tests run, decided with the orchestrator after the cost frame changed ($30 until about 2026-12-07): only the official Stryker run goes to the cloud by default (tools/stryker/cloud.py). The hook, the Checked and Release suites, the tools/mutate.py list (63 s per Rendering mutant on a GPU worker against 40 s on the Mac) and one Mac pass of the GPU tests stay on the Mac. GPU shards go to the cloud only on demand: six shards ran the Rendering tests in about 7.3 min for $0.43.

Measurements: a cpu3c worker's anonymous memory peaked at 2,295 MB of 4,096 MB, with no out-of-memory kill. The official runs, all valid:

| Module | Range | Wall time | Cost | Mutants |
|---|---|---:|---:|---|
| Core | b66db355..HEAD | 2,731 s | $0.056 | 0 in 3 spans |
| Simulation | b66db355..HEAD | 1,866 s | $0.039 | 6, all killed |
| Physics | 2b20c0dab^1..HEAD (ter-773q) | 641 s | $0.0145 | 6, all killed |

A full hardening thus takes about 46 min and $0.11 in the cloud, against more than 108 min for Core and Simulation alone on the Mac (tiunr-e8xk), because the modules run at once. A larger worker does not pay: the coverage capture uses about 1.7 cores.

The first calibration runs stopped in Stryker's initial test run on the two puff hash tests, which ter-zkdz fixed.

Review (reviewer agent): fixed B1 (cloud.py refuses an output folder with files, so a stale results.json cannot count), B2 (cloud.py asks only for files that fit 8 MB: Stryker's log of 8 to 21 MB and its report of 6 to 85 MB stay on the worker), B3 (cloud.py forwards SIGTERM, SIGHUP and SIGINT to each gpu_job.py, which cancels its job), B5 (OURS no longer matches a bare tertius-cpu), D1 (the stale copy in tools/README.md is now a pointer), D2 (protocol 3 in the handler's example, "seconds of execution"), and the CLAUDE.md bullets. B4 (free idle old endpoints of either kind) was tried and reverted: it made a GPU image switch delete the idle CPU endpoint and take its workers, against the user's rule. The README now gives that reason. Deferred: t8y-9h5t (the load rule on a worker), t8y-fvzn (one store per kind, names, results schema), t8y-3mbv (duplicate endpoints, concurrent pushes).
