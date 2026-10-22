---
id: ter-a4s2
status: closed
deps: [ter-3oer]
links: []
created: 2026-10-22T01:57:47Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-9gj5
tags: [tooling, tests]
---
# Fast test suites

The full run of tools/test-all.sh takes about 20 minutes. On 2026-10-21 in Debug: Core 8 s, Simulation 10 s, Rendering 14 s, Physics 1 min 55 s, Host over 5 min, and the script runs the modules one after another on a 10-core machine. Steps: (1) measure the duration of every test (a TRX report per module) and list the slowest 50 with what each one does (World and Jolt setup, scripted headless runs, GPU captures, long simulations); (2) find the shared causes; (3) propose fixes for the user to choose, such as running the modules in parallel in test-all.sh, xUnit parallelism for test classes that share no process-wide state (mind the Jolt pitfalls: the process-wide contact table and the native lock), shared fixtures for expensive setup, shorter simulations where a test needs fewer ticks, and a fast tier of tests for each commit beside the full run before a merge. Keep the determinism rules: most tests run single-threaded on purpose, and one test covers parallel determinism.

## Acceptance Criteria

A measured before-and-after table per module and configuration; the full run of both configurations is much faster with no test removed without the user's consent.


## Result (2026-10-22)

The full run of `tools/test-all.sh`, with the build, went from about 20 minutes to 2 minutes 9 seconds on the 10-core machine. No test was removed.

| Module | Before: Debug, modules one after another | After: Checked and Release, all ten runs at once |
|---|---|---|
| Core | 8.8 s | 10.7 s |
| Simulation | 12.0 s | 24.5 s |
| Physics | 133.3 s | 92 s |
| Rendering | 14.1 s | 21 s |
| Host | 491.0 s | 116 s |

The after column is the time of each module while all ten runs share the machine, so the small modules look slower than alone. The changes, in order of effect:

1. The Checked configuration: Debug's contracts with Release's optimized code. The user chose it over an optimized Debug on 2026-10-22. The Blue Danube generator test took 161 s in Debug and 28 s in Checked.
2. joltc gives each thread its own temp allocator (`native/joltc-thread-temp-allocator.patch`), and each backend has its own gate instead of the process-wide native lock. So worlds step at the same time, and the GPU collection that serialized the Host tests is gone. `ConcurrentBackendsTests` aborts the process with the unpatched library.
3. `JoltBackendOptions.Unpaced` steps Jolt with one worker thread in tests and headless tools: a small scene ran 2.3 times faster than with Jolt's default workers.
4. The entity table's invariant check scans the unused tail of each column once with a vectorized byte compare, and keeps each shape's bounding radius. The radius alone took 76 percent of the film fixture's tick thread.
5. `tools/test-all.sh` runs every configuration and module at once.
6. The rehearsal of the film script generator runs without puffs, which no cue reads.

Two fixes for flaky tests under load: every allocation test measures with `Allocations.Fewest`, the fewest bytes of up to three windows, because the runtime allocates a few kilobytes once on a busy machine. The free-play input test waits for a new tick between batches of posted input.

Rejected by the user: a world option that steps physics on the ticking thread (25 percent less, but most tests would no longer run the physics thread of the game).
