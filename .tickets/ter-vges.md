---
id: ter-vges
status: in_progress
deps: []
links: []
created: 2026-11-03T12:22:22Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, design]
---
# Design: the temporal renderer, docs/plans/temporal-renderer.md

Writes the design doc of epic ter-89xq from the settled decisions in its notes and the research in docs/research/temporal-renderer/, with the order of work as a list of tickets.


## Notes

**2026-11-03T13:20:35Z**

2026-11-03: the design is in docs/plans/temporal-renderer.md, sections 1 to 14, with Portable Vulkan, Known limits and Sources. Decision 8 of docs/plans/rendering-performance.md is marked superseded, with its text kept. One fork waits for the user, written into the doc as its open question: the light of a diffuse ray's hit. Option A is one bounce, as decision 30 of screen-space-illumination.md says. Option B is many bounces, through the hit's own indirect history, at no extra GPU time. The doc recommends B. The order of work, for the orchestrator to create as tickets under ter-89xq: (1) FSR 3.1's source, build and host. (2) Motion vectors, the frame history and the surface test. (3) The direct signal: the accumulator, the composite, ReSTIR DI and the still rule, after 2. (4) The screen tracer, after 3. (5) FSR 3.1 in the frame, with dynamic resolution, after 1 and 4. (6) The budget and the defaults, after 5. (7) The Linux run, after 5 and ter-kzj3. Ticket 1's lavapipe run needs ter-kzj3's first step, a Linux machine or VM. Later tickets: ter-hdbw after 5, ter-13rm after 4, ter-7khp after 6. Section 13 proposes the fate of each open rendering ticket. No other ticket was edited. ter-nw4a, ter-16vv, ter-s6zr and ter-coy0 are cancelled by the user's note of 2026-11-02, but their files still say status open. The goal of ter-pq5y (12 ms at a render size of 1080p) gives way to section 11's budget, so the orchestrator re-scopes that epic. Settled by the design and open to review: a diffuse miss reads the map along its own direction at roughness 0.5 (decision 25). Headless runs use a fixed scale of 2/3 (decision 32). The roughness limit is 0.5 (decision 26). A still costs 33 draws, about 7 s more per script of 20 stills. The design takes about 0.5 GB more memory at a 1080p output. Ticket 1 must take the names of FSR's pre-exposure constants from the pinned source.
