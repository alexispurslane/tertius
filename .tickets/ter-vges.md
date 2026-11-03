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

**2026-11-03T14:50:51Z**

2026-11-03, the fix stage after the design review (verdict: changes required), in docs/plans/temporal-renderer.md:
- The bounce model is now kept as tool code: docs/research/temporal-renderer/bounce_model.py, standard library only, about 3 minutes with python3 -I. It reproduces the doc's figures without noise (95 and 90 % at g = 0.5, 66 and 57 % at g = 0.8, gaps of 64 and 160 frames), with the clamp and no noise (99.7 % and 86 to 89 %), and the clamp's bias (0.4, 0.9 and 2.2 % for a slow read). It does not reproduce two figures of the earlier design B, the fast read: frame 160 is 99 %, not 93 %, and the shown history's spread grows by about 10 % at g = 0.8, not 3 times. So decision 25 now reads the hit's fast history. The later bounces then settle with about the slow history's own 32 frames (36 frames at g = 0.8), the still shows 97 and 93 % at g = 0.5 and 75 and 68 % at g = 0.8, and the clamp's bias stays at 0.6 %. WarmUpFrames stays 32.
- ReSTIR (decision 28, section 7): a shadowed star outweighed every lamp in p̂. The far lights now take the exact loop with the frame's taps, outside the resampling, and a near light's p̂ is 0 where its tile's bounds show it fully shadowed. Ticket 3 also measures the cabin of cabin-light. The budget's direct light is now 0.6 to 1.3 ms, the frame 7.4 to 10.7 ms, and the controller's scale 0.86 to 1.
- FSR's masks (decision 30, section 9): AMD's 3.1.4 generator makes only the reactive mask, in a call of its own, and the transparency and composition generator is deprecated. FSR now gets the reactive mask alone, with a coverage image of the transparent pass as the second design if ticket 5's films show ghosts on glass.
- The still (decision 32, section 10): one predicate, today's metering rule (step 0, endless or at least 0.09 s), in FrameHistory. A real-time film continues, and a time-lapse film draws stills. The warm-up count is 0 under the three zero cases, so today's GPU tests take no longer. The warm-up draws adapt the exposure by the step divided by their count, the first one refreshes every face, FSR gets 1000 / 60 ms, and only the shown draw enters RenderStats and gpu-timings.json. The purity claim is narrowed to FixedExposure or a step of 0 or endless.
- FrameHistory (decision 20, section 3) owns the record of the last drawn frame, the placement history and the light keys included, with Begin, Drew after the submission, usage code and an interface comment. Each pair of images takes its parity, and each history its render size, from that record.
- The ray budget (decision 38, section 6) reads each signal's history length, and a pixel whose block's ray does not fit traces its own ray. So every lit pixel has a sample, the surface age is gone, and the fix's weights never sum to 0.
- Smaller fixes. The mip bias takes AMD's − 1. MaxRadiance is 32,256, and the HDR image stays at most 64,512. The controller has Next(frame) and Measure(timings). Two submissions keep the swapchain wait out of the controller's measure (decision 37). Ticket 3's interim composite is stated. The claim about the clamp after a fix is corrected. The hand-over band follows MaxRoughness, with HandOverWidth 0.1. The diagram has its missing flows. Decisions 4 and 26 quote the user's word "irradiance". ter-s6zr's accepted misses are in Known limits and ticket 8. A new ticket 9, a timing spike of the trace, runs beside tickets 1 and 2. Section 12 lists the missing docs, comments and tests. Every prose sentence has 25 words or fewer, except the list of 26 closed ids.
- ter-m24j is Changed: ticket 4 makes its dark panel visible in the demo's paint. The doc's open question asks the user: a light of the panel's size before ticket 4, or wait for ter-80o1 (recommended).

**2026-11-03T14:52:55Z**

2026-11-03: the orchestrator settled the fix stage's one fork, the dark panel of ter-m24j, as option B: wait for the panel lights of ter-80o1. Reason: the user's rule for small forks inside a chosen design (memory, 2026-10-31), the project's preference for the real mechanism, and the user's acceptance of the same dark panel in the map's reflections (ter-ylzk). The doc's open question became decision 39.
