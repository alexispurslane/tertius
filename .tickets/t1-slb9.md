---
id: t1-slb9
status: closed
deps: []
links: []
created: 2026-11-23T13:37:41Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, testing, temporal]
---
# No test fails when a pixel's last place is shifted by half a pixel: the reprojection's place has no test of its own

Found by the mutants of t1-wl7c on 2026-11-23 (mutant S07). lastPlaceAt gives a pixel's place in the last frame's image from its motion; the surface share's build moved it unchanged from screen_trace.glsl to a shared header. A mutant that shifts that place by half a pixel passed every test of the touched classes. The lead of t1-wl7c reads it this way: the shift changes a pixel's reprojection only where its four taps disagree, so tests on flat, still scenes cannot see it.

What depends on the place: which four texels of the last frame a pixel reads its histories, its light age and its surface test from. A half pixel off blurs every history by one more bilinear step per draw and moves edges.

Add a test that names that behavior: a still scene with a sharp edge in the history (a lit surface beside a dark one), where each pixel must read its own last texel, so that a place half a pixel off shows as a blurred or moved edge after a few draws. Red on the mutant first. The lead's note in t1-wl7c names the tests that come closest and why they miss it.


## Notes

**2026-11-23T22:26:03Z**

2026-11-23, 17:26 New York time, line orchestrator: closed with the still place's fix (t0-y88b, merge 77d71a6de). The ticket's mutant, lastPlaceAt shifted by half a pixel (S16 of docs/research/t0-y88b-still-place/tools/mutants.json), is killed by StillPlaceTests.AStillPixelsLastPlaceReadsItsOwnTexelWithTheWholeWeight at 1280 x 720 and at 3840 x 2160, with a real run (its test host waited 90 s for the GPU lock).
