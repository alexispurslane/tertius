---
id: ter-ep5j
status: open
deps: []
links: []
created: 2026-10-18T23:24:21Z
type: chore
priority: 4
assignee: Alexis Purslane
tags: [performance]
---
# Further sync point work for large worlds, when a scene needs it

Follow-up of ter-sn28 (2026-10-18). Not built there, because no scene needs it yet. Measured on a busy M1 Max: (1) the numeric gather on the workers, per-chunk lists joined in chunk order, about 60 us in the Mixed scene; (2) the follow and mirror passes on the workers, only above about 1,000 frames, about 10 us per pass; (3) the end of the tick (step 12) on the workers, 122 us at 100,000 entities; (4) a worker set that the World keeps for its whole life, to remove the garbage of each Parallel.For (about 2 KB per call, about 12 KB per tick in a large world), which ARCHITECTURE.md records as waiting for a measurement. At 100,000 entities the render snapshot (1.1 ms, already parallel) and the routing (559 us) are now the largest steps. Start only when a scene or the demo needs the time, and measure on a quiet machine.

