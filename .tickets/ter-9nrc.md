---
id: ter-9nrc
status: open
deps: [ter-nmbm]
links: []
created: 2026-11-03T19:30:31Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, performance]
---
# The direct signal's passes above their budget: light candidates, direct light and accumulate direct

Ticket ter-nmbm measured the new passes in Release on the M1 Max with Metal traces (tools/gpu-trace.sh --no-counters, 120 frames, interleaved with the base 2d0ea65e8; ARCHITECTURE.md, "ReSTIR DI", holds the table). At 1280 x 720, light candidates + direct light took about 1.66 ms on lamp-hall-64's film and 3.15 ms on demo-exit's deck from frame 399, against the budget of 0.6 to 1.3 ms (docs/plans/temporal-renderer.md, section 14, ticket 3). accumulate direct took about 0.67 ms on the hall and 1.15 ms on the deck, against 0.15 to 0.30 ms. The frame span still fell on the hall (11.7 to 8.0 ms) and held on the deck. Candidates to measure: one accumulation dispatch for both parts, so that the 3 x 3 surface keys (surfaceKeyAt reads the distance, the normals and the shading target, and builds a view ray) are read once per pixel and not once per part and loop; a surface key image that the surface test writes; the direct pass's shadow lookups of the exact star on the deck; and the light candidates' four unshadowed evaluations. Find the cause with a trace with counters before a change.

