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

**2026-11-03T12:30:45Z**

Launched 2026-11-03: workflow run 11bf7522-9923-4941-b1c5-617ca81ac0b3, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (design as the build stage at Opus xhigh, one review, one fix), branch ter-vges-temporal-renderer-design from 987b8d2c2.

**2026-11-03T13:20:35Z**

2026-11-03: the design is in docs/plans/temporal-renderer.md, sections 1 to 14, with Portable Vulkan, Known limits and Sources. Decision 8 of docs/plans/rendering-performance.md is marked superseded, with its text kept. One fork waits for the user, written into the doc as its open question: the light of a diffuse ray's hit. Option A is one bounce, as decision 30 of screen-space-illumination.md says. Option B is many bounces, through the hit's own indirect history, at no extra GPU time. The doc recommends B. The order of work, for the orchestrator to create as tickets under ter-89xq: (1) FSR 3.1's source, build and host. (2) Motion vectors, the frame history and the surface test. (3) The direct signal: the accumulator, the composite, ReSTIR DI and the still rule, after 2. (4) The screen tracer, after 3. (5) FSR 3.1 in the frame, with dynamic resolution, after 1 and 4. (6) The budget and the defaults, after 5. (7) The Linux run, after 5 and ter-kzj3. Ticket 1's lavapipe run needs ter-kzj3's first step, a Linux machine or VM. Later tickets: ter-hdbw after 5, ter-13rm after 4, ter-7khp after 6. Section 13 proposes the fate of each open rendering ticket. No other ticket was edited. ter-nw4a, ter-16vv, ter-s6zr and ter-coy0 are cancelled by the user's note of 2026-11-02, but their files still say status open. The goal of ter-pq5y (12 ms at a render size of 1080p) gives way to section 11's budget, so the orchestrator re-scopes that epic. Settled by the design and open to review: a diffuse miss reads the map along its own direction at roughness 0.5 (decision 25). Headless runs use a fixed scale of 2/3 (decision 32). The roughness limit is 0.5 (decision 26). A still costs 33 draws, about 7 s more per script of 20 stills. The design takes about 0.5 GB more memory at a 1080p output. Ticket 1 must take the names of FSR's pre-exposure constants from the pinned source.

**2026-11-03T13:31:51Z**

2026-11-03: the user chose option B of the open question, many bounces: a diffuse ray's hit also brings its own indirect diffuse history of the last frame (bounces = 1). The orchestrator's model, added to the user's question: each bounce passes through the 32-frame diffuse history again, so the later bounces settle with a time constant of 32 / (1 - g) frames, where g is the share of light that the room returns per bounce (64 frames at g = 0.5, 160 at g = 0.8, without the anti-lag). A still of 32 warm-up draws shows about 90 % of the settled light at g = 0.5 and 57 % at g = 0.8. ter-bsxh is closed, so ter-13rm alone closes the gap between the screen's many bounces and the map's one bounce.

**2026-11-03T13:53:08Z**

2026-11-03, the continuation after the user's answer (option B, many bounces), in docs/plans/temporal-renderer.md:
- Decision 17 under "Made by the user" now holds the many bounces, and it replaces decision 30 of screen-space-illumination.md. The section "Open question" is gone, with every mention of it and of its options. So the settled decisions are now 18 to 36, one higher than before. The notes above that name decisions 25, 26 and 32 mean today's 26, 27 and 33.
- The term bounces is gone from section 6's hit light. It had one value, and no setting and no named test needs a single bounce.
- Section 6, "How the later bounces settle", corrects the lag. Each bounce passes through the diffuse history again, so the gap shrinks by e every 32 / (1 - g) frames (64 frames at g = 0.5, 160 at g = 0.8). A simulation of the accumulator, written for this design and not kept, gives these figures. A still shows 95 % of the settled light at g = 0.5 and 66 % at g = 0.8, because the reset frame's hits start from the map's closed form. With a dark start, the figures are 90 % and 57 %. The clamp's box follows the fast history, which leads during a rise. So the clamp only lifts the slow history, and the rise does not oscillate. So no fix is needed. With noise of 25 % or more, the clamp rarely acts. The clamp's bias under skewed noise compounds, to about 1 % low at g = 0.5 and 2 % at g = 0.8. WarmUpFrames stays 32. Two designs for the history that a hit reads: the slow one wins over the fast one. Known limits 3, 12 and 13 hold the result.
- With ter-bsxh closed, ter-13rm alone closes the gap between the screen's many bounces and the map's single bounce (section 6, Known limit 12).
- Section 13 lists the 26 tickets that the user closed in main's commit 9dd66085f, and gives a fate to each open rendering ticket. Four closed tickets carry ideas into build tickets. ter-s6zr's corrected read goes into a new ticket 8, after 4 and before 6. ter-coy0's rule for a lamp's billboard goes into ticket 4 (decision 36). ter-ch7z's bloom width and ter-htg7's render-size split go into ticket 5.
- New settled decision 35: the corrected read serves the reflection misses and every other reader of the map's specular term, and the diffuse misses keep the plain read. New settled decision 36: an impostor billboard writes its instance's light mark.
- Section 11 adds the corrected read's cost (0.2 to 0.6 ms) and a subsection on how each ticket measures its part. The frame's estimate is now 7.3 to 10.6 ms at 1280 x 720. Section 14 adds ticket 8 and an acceptance list for each ticket.
