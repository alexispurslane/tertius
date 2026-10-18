---
id: ter-sn28
status: open
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

