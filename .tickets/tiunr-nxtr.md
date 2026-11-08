---
id: tiunr-nxtr
status: closed
deps: [tiunr-4b2g, tiunr-7haj, tiunr-okzm]
links: [ter-18hc, tiunr-wk29, ter-12xp]
created: 2026-11-08T02:19:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# The box learns the tail: a long-run second moment widens the clamp box by its standard error

User choice 2026-11-08 (design T). The slow history keeps rare bright light that the fast-history box forgets in about 20 frames, so the box clamp and anti-lag treat correct light as error: the pixel rule's pan burst (tiunr-wk29) and the dim far glow (ter-18hc). Design: each signal's slow history also keeps the mean of its squared luminance, blended and reprojected with it. The box half width becomes the larger of today's spread term and k times the standard error of the box mean against the slow mean, from that moment. Opt-in, test-first, measured on the walk and still captures and on ter-18hc's glowing panel at 4.95 m. Starts after the pixel rule's acceptance (tiunr-4b2g).


## Notes

**2026-11-08T14:41:59Z**

2026-11-08: the user asked to go straight on to the firefly limit (tiunr-5qf3) when this reports, and to see only videos at the end. The orchestrator decides from the measurements whether the box fix stays on.

**2026-11-08T15:29:33Z**

2026-11-08, implementation sub-agent: built opt-in TERTIUS_BOX_TAIL=on (uncommitted). Evidence: /private/tmp/tertius-box-tail-20261108/report.md. Choices: k = 1 (panel reads the same at k 1 to 3, a moved band keeps 4.8% after 30 frames at k 1 and 9.4% at k 2), N = (2 fast cap - 1) x max(0.36 count, 1) = 22.7, per-channel width = luminance width x slow color / slow luminance, R32F tail per part plus one shared R32F scratch, 12 B/px in the renderer. Result: still rough crop length 88 -> 1,152, final FSR noise 2.48 -> 1.49, pan crop cuts 40.4% -> 0.8%, far-plate peak 4.98% -> 0.004%, far panel at 4.95 m 0.0248 -> 0.0411 of 0.0433. Cost: lag. Rough dimming persistent 90% 40/100 -> 112/not within 180 (plate/final); switch-off rough 10/12 -> 24/29 and about 5% glow left after 180 updates; emitter move rough 29/32 -> 51/61, with a bright slab of the old reflection still visible 3 s later; walk leaves a faint ghost streak; the demo floor shows more specks. Cause: after a real change the history mixes old and new light, whose spread widens the box; the 1/L term widens it more after a cut. Default identical: 104/104 SPIR-V modules byte-identical (Checked, Release), still captures byte-identical to the earlier default. Tests: 10 new, red for the right reasons, green. Full suites pass: Checked 10,322 passed / 24 skipped, Release 10,309 / 24. Not run: coverage, mutation, ARCHITECTURE.md. Not recommended as is.

**2026-11-08T15:30:56Z**

2026-11-08: result (/private/tmp/tertius-box-tail-20261108/report.md): the tail stops the pan cuts (rough crop 40.4% to 0.8%) and lowers still rough noise (2.48 to 1.49), but real changes linger: the rough 20% dimming is not adjusted within 180 final updates (was 100), and a moved emitter leaves 5.1% of its old light. Decision by the orchestrator under the user's delegation: the tail stays off (opt-in TERTIUS_BOX_TAIL), and the work goes on to the firefly limit tiunr-5qf3.

**2026-11-08T20:05:06Z**

2026-11-08: rejected and removed before the merge into main. The box tail kept rare bright light, but real light changes lingered (a moved emitter left a slab visible 3 s later, a switched-off emitter kept about 5% of its glow). The switch TERTIUS_BOX_TAIL, BoxTail.cs, accumulate_box_tail.glsl, its _boxtail shader modules and its tests are gone. Every remaining engine SPIR-V module is byte-identical before and after the removal (75 of 75). The evidence stays in /private/tmp/tertius-box-tail-20261108/.
