---
id: ter-sn28
status: in_progress
deps: []
links: []
created: 2026-10-18T18:04:36Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [performance, frames]
---
# Cut the sync point's cost per frame that follows a sample

User decision (2026-10-18): the rails components (ter-6r8c) merged with their benchmark accepted, and this ticket looks at the remaining cost. In KeplerFrameTickBenchmarks (1,000 Kepler frames without bodies, benchmarks/Tertius.Benchmarks/WorldTickBenchmarks.cs), the sync point still takes about 200 us with one worker, about 200 ns per frame, although FollowOwners only copies samples for these frames. Profile FollowOwners and the float view fill (FrameOwnerRow, SetBackTransform, MoveMembership), find what costs the time, and cut it. Record the benchmark before and after in a note. Numbers at merge: tick with all workers 365 -> 349 us, with one worker 353 -> 460 us; sync point 358-382 -> 253-258 us (all) and 334 -> 203 us (one).


## Notes

**2026-10-18T19:34:48Z**

Profile done (branch ter-sn28-sync-profile, base 9721338, busy machine so numbers are noisy). Kepler scene, one worker, sync point 202 us: spatial.Update 54 (every Kepler frame changes its 32 m cell every tick), mirror pass in step 2c 46, mirror pass in step 7a 44 (repeats 2c when the tree did not change), follow pass 36, Frames.Update 9, CopyDirtyRows 7, AdvanceNumericFrames 5. The per-frame cost is layered calls and struct copies, not the handle checks. Prototype P1 (c1b03ea, one walk of the tree per pass with struct visitors): sync point 202 -> 151 us (one worker), 217 -> 162 us (all), same state hash, all suites green. Prototype P2 (06c1a98, CopyDirtyRows per chunk on the workers, merged in chunk order): -450 us at 100k entities, nothing below 4,096 rows. Parallel loops pay off only at scale: one Parallel.For costs 4-7 us and about 2 KB. Larger scenes: routing (37% at 100k), render snapshot, CopyDirtyRows, spatial grid, numeric gather (102 us at 2k bodies). Ranked: P1; skip the 7a mirror pass when the tree did not change; a cheaper spatial move; numeric gather fixes; P2; a parallel stable sort in routing; parallel follow and mirror passes last. Full report in the session scratchpad sn28/report.md. Numbers must be measured again on a quiet machine.
