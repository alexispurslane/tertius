---
id: ter-uqmn
status: in_progress
deps: []
links: []
created: 2026-10-27T12:22:51Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Bring the shading models' cost in the direct and transparent passes down

ter-3dp5 measured the skin and hair epic on demo-exit's deck window (120 frames from 399, 1920x1080, Release, traces with --no-counters, 2026-10-27): with no skin and no hair on screen, the direct pass grows by 0.4 to 0.9 ms under SeparableSubsurface (base 5.37 to 5.75, branch 6.13 and 6.28 ms) and by 0.8 to 1.3 ms under NoSubsurface (6.59 and 6.69 ms, two runs that agree within 0.1 ms), and the transparent pass by 0.4 to 0.7 ms (1.62 to 1.71 against 2.10 to 2.32 ms). The design estimated 0.02 ms for the shading target's read. That the direct pass is 0.3 to 0.6 ms slower with the scatter off, where it writes one attachment fewer, is not explained; the off runs' other passes are also 0.05 to 0.2 ms slower, so part of it may be the machine's state. No run isolates the cause. Candidates: the per-light choice by model in respond and takesLight inside the light loop, and the two parts of DirectTerms kept apart per light; the shading target's fetch; the larger shader's occupancy; for the transparent pass, forwardShading and the runs by sides. First isolate the cause (for example a build whose respond is brdf alone), then: a loop specialized per model, or the standard path first with hair and skin as rare branches. Measure on demo-exit and skin-and-hair.json, and keep the pixels bit for bit. See docs/plans/skin-and-hair.md, Cost.


## Notes

**2026-10-31T14:22:50Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 88962f00-4bb7-41be-89dd-bb3951a51fd8), run c8b52f8d-5ec7-4fd0-87a4-adc3660b394e, branch ter-uqmn-shading-model-cost from cd8b233c.

**2026-10-31T16:34:07Z**

2026-10-31: the first run (c8b52f8d, old script version 88962f00) isolated the cause (the hair branch inside the light loop, about 0.5 ms of the deck's direct pass) and committed a loop per model (2f881901), then stopped at a fork because the loop per model changes pixels on this Mac (230 pixels of demo-exit by 1 of 255; worst 3 pixels by 7 in one hair-glass frame) through Metal's fast math. The user accepted the pixel change for about 0.5 ms on the direct pass and 0.15 ms on the transparent pass. Continuation run 5a158d46-a326-4677-816b-e019d9f53fd0 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) finishes the docs and runs the review and fix stages.

**2026-10-31T17:15:33Z**

2026-10-31: run 5a158d46 updated the docs (069c5bf7); its review stopped at correctness 'unverified' and fulfillment 'changes-required', with 8 findings (saved in /tmp/uqmn-review-5a158d46.json). The three medium ones: the one-sided transparent pipeline still holds the fiber loop although it never shades a fiber; the transparent pass's remaining 0.1 ms is attributed by elimination only; a SIMD group with hair and other pixels runs the light work twice. Fix continuation run a8e4f431-94e3-4fce-80d4-0bc143994512 (version 3cde7d7d-f5f8-4f91-aee3-b2258cb90dc6) adds a fiber specialization constant, measures a hair view under many lamps and NoSubsurface, and resolves the rest.

**2026-10-31T19:51:28Z**

2026-10-31: run a8e4f431 ended failed (output_json), because its fixer ended a turn while a timing series ran in the background (old script version 3cde7d7d). The fixer thread finished later and reported done=true on 867a0ec3, clean: all 8 findings of review 5a158d46 resolved (shadesFibers in the one-sided pipelines, lamp-hall-16-hair, the NoSubsurface series, ticket ter-qsye for the unattributed remainder, doc fixes). The orchestrator merged main into the branch (ef561998): shadesFibers takes constant_id 3, which ter-o0a6 freed. Continuation run c1a6ae45-170c-435f-bf56-9b4f606f6ede (version 869b0b07-3413-4b06-8707-a88a91bf6e91), review base 43f2b9cc, runs the checks, the review and a fix if needed.
