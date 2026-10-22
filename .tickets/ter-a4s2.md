---
id: ter-a4s2
status: open
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

