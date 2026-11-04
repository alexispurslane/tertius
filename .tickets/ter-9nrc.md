---
id: ter-9nrc
status: open
deps: [ter-nmbm]
links: []
created: 2026-11-03T19:30:31Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal, performance]
---
# The direct signal's passes above their budget: light candidates, direct light and accumulate direct

Ticket ter-nmbm measured the new passes in Release on the M1 Max with Metal traces (tools/gpu-trace.sh --no-counters, 120 frames, interleaved with the base 2d0ea65e8; ARCHITECTURE.md, "ReSTIR DI", holds the table). At 1280 x 720, light candidates + direct light took about 1.66 ms on lamp-hall-64's film and 3.15 ms on demo-exit's deck from frame 399, against the budget of 0.6 to 1.3 ms (docs/plans/temporal-renderer.md, section 14, ticket 3). accumulate direct took about 0.67 ms on the hall and 1.15 ms on the deck, against 0.15 to 0.30 ms. The frame span still fell on the hall (11.7 to 8.0 ms) and held on the deck. Candidates to measure: one accumulation dispatch for both parts, so that the 3 x 3 surface keys (surfaceKeyAt reads the distance, the normals and the shading target, and builds a view ray) are read once per pixel and not once per part and loop; a surface key image that the surface test writes; the direct pass's shadow lookups of the exact star on the deck; and the light candidates' four unshadowed evaluations. Find the cause with a trace with counters before a change.


## Notes

**2026-11-03T22:13:22Z**

2026-11-03, the fix stage of ter-nmbm: the deck's traces counted the first still's 32 warm-up draws. Without them (a trace of the still alone, subtracted), at 1280 x 720 light candidates take 1.63 ms, direct light 1.90 ms and accumulate direct 1.18 ms. In the same traces every unchanged pass ran 10 to 25 % faster than in the base's, likely the GPU's clock, so the 720p span hides about 1.6 ms of added work, and the 1080p span shows about 2.0 ms. The interleaved timer runs with a control are in ARCHITECTURE.md, 'ReSTIR DI'. ReSTIR's visibility test no longer evaluates the response (lighting.glsl, pathVisibility).

**2026-11-04T02:13:20Z**

2026-11-03, ter-nmbm's continuation built the unbiased spatial combine (ter-t8z7), which adds to the direct light pass. Acceptance view of this ticket: demo-exit's deck (the time-deck window from frame 399, and its traces), at 1280 x 720 and 1920 x 1080. Interleaved timers, 7 rounds, means in ms, base c6572ca3d / biased combine c11b3cbe8 / unbiased b20714c0c: deck 1280 x 720 direct light 2.39 / 1.39 / 2.42, the changed passes 2.56 / 4.20 / 5.55, mean span 11.67 / 11.42 / 12.84; deck 1920 x 1080 direct light 4.16 / 2.77 / 4.73, changed passes 4.61 / 8.22 / 10.41, mean span 15.53 / 17.85 / 20.04. Traces with the warm-up correction: 1280 x 720 light candidates 1.60 to 1.66, direct light 3.23 to 3.30 (biased 1.85 to 1.87), accumulate direct 1.22 to 1.23; 1920 x 1080 2.54, 5.15 to 5.16 (biased 3.03), 2.40. So light candidates + direct light take about 4.9 ms at 1280 x 720 against the budget of 0.6 to 1.3 ms. The combine's own cost is the direct pass's 1.0 to 1.4 ms at 1280 x 720 and 2.0 to 2.1 ms at 1920 x 1080: a visibility test and an evaluation of the kept light at each neighbor whose own choice is another light, with that neighbor's G-buffer reads, and each combined light's shadow lookup at the pixel. Arrays of the neighbors indexed at run time doubled that cost and are gone. Candidates to measure next with counters: the neighbors' G-buffer reads for Z, the shadow lookups' gathers near edges, and the occupancy of the direct pass. Runs and traces: /private/tmp/nmbm3 (runs.txt, summary.md, traces.txt, traces.md).

**2026-11-04T02:58:15Z**

2026-11-03, the fix stage of ter-nmbm's continuation: the hall, lamp-hall-64's film from frame 1, re-measured on the final build 25f1f9485 against the base c6572ca3d, 3 interleaved traces each, warm-up corrected. At 1280 x 720 the branch's light candidates + direct light + accumulate direct + composite take 3.51 to 3.77 ms (1.13-1.21, 1.47-1.58, 0.72-0.77, 0.19-0.21) against the base's direct + ambient light of 5.21 to 5.98 ms, and the frame mean 9.29 to 9.71 ms against 10.77 to 11.92 ms. At 1920 x 1080: 6.33 to 6.88 ms against 7.89 to 8.89 ms, and the frame 13.88 to 14.83 against 14.82 to 16.31 ms. So the hall's saving fell from about 4 ms (first build, biased combine) to about 1.5 to 2.2 ms at 1280 x 720. The deck stays this ticket's acceptance view: there the four passes cost about 4.9 ms + 1.2 ms against a budget of 0.6 to 1.3 + 0.15 to 0.30 ms. Files: /private/tmp/nmbm4 (builds.txt, traces.txt, traces.md).

**2026-11-04T13:20:55Z**

2026-11-04, the user's decision (see ter-89xq's note of the same day): the first change of this ticket is the rule 'exact lights when they are few'. One model: each light of the main view is exact or sampled, and LightTable decides once per frame on the CPU. A light is exact when it holds a cascade set (as today) or when the main view keeps at most N lights. A frame with no sampled light runs no light candidates pass and no reservoir reuse, and the direct pass shades every light exactly, as EveryLight does now; EveryLight becomes the same rule with no limit. N comes from measurement: the light count at which the two costs cross, from a sweep of a scene with n lamps (n = 1, 2, 3, ... 16) plus demo-exit's deck and lamp-hall-64, at 1280 x 720 under the GPU lock with gpu-clock beside each run. The switch when the count crosses N: an exact frame's sample is already the converged light, so its history can count as full length; measure whether the switch needs a margin against flicker. Left out until a scene needs it: a per-tile or per-pixel count (the renderer has one light list per frame and no tile light lists). The invariant 'every exact light holds a cascade set' becomes 'every light with a cascade set is exact'. Expected on the deck, from the old measurements: the exact loop took 3.35 to 3.63 ms with the old ambient pass, against 6.34 to 6.49 ms for the four sampled passes (headless, 1280 x 720), about 1.3 to 2.0 ms at the windowed clock. The other candidates of this ticket (one accumulation dispatch for both parts, a surface key image, the combine's neighbor reads) stay, for scenes that sample. The user's frame target is 12 ms (ter-89xq). Priority 1.
