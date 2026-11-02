---
id: ter-lbyp
status: open
deps: []
links: [ter-hxca, ter-773q]
created: 2026-11-02T08:47:42Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [testing, physics, tools]
---
# Find what runs about 38 busy threads at the start of a Physics test host under DOTNET_PROCESSOR_COUNT=4

Found in ter-773q on 2026-11-02. ter-773q made the default Jolt job system take its worker count from Environment.ProcessorCount, so DOTNET_PROCESSOR_COUNT=4 now bounds a default backend to 3 Jolt workers. But no Physics or Simulation test steps a default backend for long, so the change did not move these test hosts. A Physics test host with DOTNET_PROCESSOR_COUNT=4 still reaches a peak CPU rate of about 570% in its first 2 s (the rate of CPU time in a ps sample every second: 573% before ter-773q, 570% and 459% after it). One 0.5 s sample of ps -M showed 57 threads, of which 38 were busy at about 10% each, against 6 to 9 busy threads in the steady part of the run, which sits at about 400%. The logs of ter-773q were in /tmp/ter773q (sample.py, threads.py, before-physics.log, after-Physics.log, threads-physics.log). The peaks of about 700% that ter-hxca saw during the official Stryker run's mutants are also unexplained, and no mutant run was repeated. Candidates: the xunit start-up and test discovery, the tiered JIT, the .NET thread pool's thread injection, and the native threads of tests that build many backends at once (ConcurrentBackendsTests).

## Acceptance Criteria

1. The cause of the start-up peak is named with evidence (thread names or stacks from a sample). 2. Either the peak stays near 400% under DOTNET_PROCESSOR_COUNT=4, or tools/README.md, Stryker.NET pitfalls, says why it cannot and how long it lasts.

