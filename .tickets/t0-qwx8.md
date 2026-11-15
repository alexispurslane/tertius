---
id: t0-qwx8
status: open
deps: []
links: []
created: 2026-11-12T13:56:24Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The light at a hit must not depend on whether a jittered sample lands on a sub-pixel strip

From ter-g3tk cause 1 and docs/plans/bounce-light-confidence.md, step 8: at world tick 300-306 the cabin's single-bounce region r11 falls 43 % while direct sunlight on sub-pixel sill strips rises 2 %. Design A follows such steps by evidence but does not remove the fault. Needs its own design.


## Notes

**2026-11-13T03:46:10Z**

2026-11-13 (t0-3kyb): the sun strips' jitter fault also keeps their light-age records young in a still cabin, at about 0.13 % of pixels. Under Design X those young codes become light evidence for whole 8x8 blocks in a still: extra cuts, own rays and noise there. Take T12's evidence census must count them; if they matter, this ticket blocks the Design X merge.

**2026-11-14T14:55:57Z**

2026-11-14: design written, docs/plans/sub-pixel-strips.md (two designs, R: receivers average over a longer history; H: hits read a footprint mean). Mechanism: the hit light, the guide light and the light-age record all read this draw's direct light at the jittered sample point; AMD's 18 fixed Halton phases repeat forever, so a sub-pixel sunlit strip arrives as k/N of its light, a fixed bias; at ticks 300 to 306 k moves from 3 to 1 of 18 on the far sill, which steps r11 by 33 to 41 %. Young light-age records in a still: 0.136 % of pixels with jitter, 0.000 % without. The shared part (a long Halton sequence; the record counts a change only over an area) can merge first and unblocks Design X. Questions to the user pending.

**2026-11-14T15:07:19Z**

Decision (Alexis, 2026-11-14): Design R on both points. 1: a bounce hit reads this draw's sample, as now, with no footprint mean. 2: the bounce history grows to 128 draws in a block without change evidence (32 under motion evidence), which takes in t0-70v1. Order: step 1 (t0-6gej, area changes) is the next build after the Design X builder's current step and merges before Design X; step 2 (t0-f238, jitter) and step 3R (t0-70v1) follow Design X's merge.

**2026-11-15T14:52:58Z**

Reading of the line orchestrator of Design X, 2026-11-15, after the back-face line's verdict (its record: docs/research/back-face-depth-layer/stage-2/shadow-leak.md, section "The other line's strip", on its branch at e2c9b025d). This note changes no ticket of the design. Alexis decided design R, so its fate goes to Alexis.

The verdict. The far sill's lit row of this ticket (scene orbit, camera person, output row 563, columns 892 to 1030, render row 375) is the end window's sill of rect-lamps. rect-lamps, the cabin switch, the cabin walk, the stand film and the climb all use this one scene and camera. A line cast from each point of the sill to the sun's disc, through the exported triangles, finds no sunlight there at ticks 300 and 306, and only on the outer 7 mm at tick 60. The engine lights 0.049 to 0.121 m of the sill, under one twentieth of a render row tall. It is a leak of the sun's shadow (t8-rjno). The side windows' jambs and sills leak too: the engine lights 0.120 m, the truth the outer 3 to 6 mm.

Which evidence of this ticket and of docs/plans/sub-pixel-strips.md is the leak:
- The step of r11's bounce light between world ticks 300 and 306 (ter-g3tk cause 1: -43 % of the single-bounce mean; section 2.1, take bC: -33 %). The lit row is the leak. Between the two ticks one texel of the shadow map changes on the sill's line, and the leak's lit part shrinks from 0.108 m to 0.049 m. So the step is the leak's size, which steps by one shadow texel as the ship turns, seen through the few jitter phases that land on the sliver.
- The far sill that shows under 18 jitter phases and not under 8 (takes dA and dE). The same sliver.
- The 1,067 pixels that change between jitter phases by more than a factor of 4, and the 437 changed pixels of ter-g3tk, all on the side windows' jambs and sills. Those surfaces are the leak's.
- The young light-age records in a still (0.136 % of the pixels with the jitter, 0.000 % without, census of t0-3kyb). The census ran in this view.
- The jitter's 18-draw cycle of the cabin switch at tick 60 (t0-f8oc, design doc section 11). Likely the leak: the engine lights 0.121 m there and the truth 7 mm. The drift experiment (t0-ion0) measures the cabin without the sun.
- The wander of r10 and r12 in the honest walk. The same room.
- The false hits of ter-ukw6 on the sill's thickness slab, and with them much of the ceiling's bright reflection light and specks in rect-lamps (t0-adau). The slab's fault is its own, but the light that makes it visible is the leak's: that ticket says the sill is about 1,000 times brighter than the miss light.

Two older measurements already pointed at the sun, and they still stand: with the sun off the step is gone (r11 0.0675 at both ticks), and without jitter the sun adds nothing to r11 (single bounce 0.0147 against 0.0143 with the sun off). So without jitter the pixel centers never land on the sliver.

Whether a measured case of a real strip remains: none that I know.
- A real strip thinner than a pixel exists: at tick 60 the truth keeps the outer 7 mm of the end sill lit, and 3 to 6 mm at the side windows. That is about 6 % of the leak's width. Nobody measured its effect alone.
- In the two other still scenes of the survey, the bay (bay-sunbeam) and the exit cabin (exit-cabin), the jitter changes nothing that this line measured: the regional test's share of false pixel draws is the same with and without the jitter (1.5 and 1.6 %, 1.5 and 1.2 %, t0-xlcu step 1).
- The fault as a model stays true: a lit feature thinner than a pixel arrives as k of N jitter phases. The tests of the design use made-up strips, which stay valid as tests of that model.

My reading for Alexis. Every measured case behind design R (the long Halton jitter t0-f238 and the 128-draw history t0-70v1) comes from one room, and there the strips are the shadow's leak. So hold both builds until the shadow's fix is on main. Then repeat three takes in this room: r11 at ticks 300 and 306 under 18 phases (take bC), r11 per jitter phase in the cabin switch's still control, and the census of young records. If the step, the cycle and the young records are gone, design R has no measured case, and the user's rule applies: leave out a feature that is not needed yet. One more cost counts against it then: a 128-draw history follows a change without evidence four times slower, which works against the bay film's trail (t0-acxm). If a part remains from the real 7 mm, size it with those takes and decide then.
