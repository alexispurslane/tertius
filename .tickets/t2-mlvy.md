---
id: t2-mlvy
status: closed
deps: []
links: []
created: 2026-11-11T18:51:20Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance, lights]
---
# Light list per screen tile: a second try at the light budget, as two designs and a prototype

User, 2026-11-11: 'The light budget per screen tile got built once, but badly. We should try again.' Study the last attempt (ter-69s8), say what went wrong, write two designs, and prototype the more promising one on this branch. Measure the prototype against main: Metal System Traces of the deck and of lamp-hall-64-moving, the image difference on the deck, lamp-hall-64 and rect-lamps, a moving-lamp film, and the step in light across tile edges on a smooth lit floor. No merge into main and no ticket closure: the orchestrator takes the two designs and their forks to the user.


## Notes

**2026-11-11T23:11:00Z**

Outcome of 2026-11-11 (docs/plans/light-pairs.md, docs/research/light-pairs/measurements.md). No prototype wins, and the branch holds no change under src.

The diagnosis of ter-69s8: the branch built no part of the renderer, the "patchwork" renders are identical images of a fixture error, and the budget's rule cannot cut a hall. The measurements here add that the deck has nothing for a budget to drop: a floor pixel has about 8 pairs of a point and a light, and each one shows.

The deck's direct pass, 4.19 ms, with the exposure fixed: the lamps' pairs hold 2.7 ms, the steps and the fixed work 1.1 ms, the sun's cascade lookups 0.03 ms. The rectangle lamps' two edge sums hold 1.66 ms. About 1 ms appears only because the shadow lookups and the responses share one loop body. The hall's direct pass, 3.80 ms: 64 pairs per pixel, about 2 ms of responses, 0.7 ms of shadow tiles.

Three prototypes:
- The visible floor (visible-floor.patch, 23 CPU and 6 GPU tests, green): the deck 4.60 ms at a floor of 2^-9 against 4.19 ms, with 1.4 % less light. The hall 3.65 ms against 3.80 ms, with 12.5 % less light. Its unused code alone costs 0.5 ms.
- The bounds of the cascades (cascade-bounds.patch, 2 GPU tests, green, with every shadow test): exact, settles every cascade lookup of the deck, wins 0.1 ms and costs 0.43 ms for its pass. It raises the sampled images of a stage from 17 to 18.
- Loops of their own: the sun's loop is about 1 ms slower in the frame. Change 1 of t6-h5e3-six-frame-cuts with the point pairs in a loop of their own wins 0.2 ms in the direct pass and loses 0.5 ms in the transparent pass.

A full Checked run of the rendering tests with both patches: 3,066 of 3,069 passed. The three failures are the tests of DescriptorLayoutsTests that state 15 and 17 sampled images, which the bounds image raises by one.

One trap: a variant that changes the image moves the automatic exposure, and with it every lamp's reach. Three variants of the first round were read wrongly for that reason (t2-bne2).

Not done: the stills and the moving-lamp film of the floor against main. Their take stopped on a script error after the first still, and the repeat waits in the GPU queue.

Follow-ups: t2-bne2 (a fixed exposure for the trace tools), t2-3dut (a shader per screen tile, design A), t2-d75t (xctrace does not end).

**2026-11-12T06:39:26Z**

Outcome of 2026-11-12 (docs/plans/light-pairs.md, 'Key and fill, version 1', 'Key and fill' and 'The nine-read shadow filter'; docs/research/light-pairs/measurements.md). The user chose key and fill on 2026-11-11, and the branch holds its version 2 under src with 37 GPU tests green. Neither version wins as built.

Version 1 (key-fill-v1.patch) reads a fill with its shadow from four corners 8 pixels apart. It saves 1.82 ms of the deck's direct pass and 2.29 ms of the hall's, and it fails on the image: blotch 4.52 % on the deck, 6.72 % in the hall, 8.43 % in the cabin, with soft blobs in place of the greenhouse frame's shadows.

Version 2 keeps every lamp's shadow lookup at the pixel, and a lamp's response is full by its key share and a point's diffuse light by the rest. The image passes: blotch 0.37 % on the deck, 2.09 % in the hall, 0.32 % in the cabin, and no step at tile edges. The direct pass saves 0.70 ms on the deck and 0.89 ms in the hall. The corner and tile pass costs 0.64 ms and 1.27 ms, so the two together are 0.06 ms under every light pair on the deck and 0.38 ms over it in the hall.

Why the corner and tile pass costs so much, from cost-only builds: its fixed cost is 0.06 ms, the lamp loop holds 1.06 ms in the hall and the ring walk 0.14 to 0.34 ms. The time does not follow the count of corners (16 times the corners cost twice the time), and the same work in shorter fragments runs in half the time. Tiles of 16 pixels with a sixteenth of the lamps per fragment run in 0.265 ms in the hall and 0.252 ms on the deck. The cause inside the GPU is not known: the offline compile shows r127 and spilled values read in the lamp loop, and no trace ties the time to that. So version 2 can save about 0.45 to 0.6 ms after a real pass of that shape (t2-vsqh).

In the code since this note: the corner shader holds its greatest losses in five named values in place of two arrays with a run-time index (the orchestrator's hint). The stills are the same pixel for pixel, 37 of 37 GPU tests pass, and the pass fell from 1.266 to 1.036 ms in the hall and from 0.636 to 0.607 ms on the deck. The offline count of stack instruction sites did not fall (22 loads and 13 stores before, 23 and 11 after), so the gain is measured and its cause is not.

Refuted on the way: a split of the direct shader into one for the sun, the point and the tube lamps and one for the rectangle lamps (no register gain beyond the nine-read filter, and two capped draws do not add up to one); the corner pass without shadow lookups (it saves 0.25 ms in the hall and fails on the deck's image, blotch 4.58 %, because a hidden lamp takes a key place); corners spread over the full target with a discard (twice the time); tiles of 16 pixels with the ring walk as built (2.25 ms in the hall); a list of lamps by reach for the corner (the direct pass walks every lamp too).

The pair research (docs/research/light-pairs/pair/) found one gain apart from key and fill: the nine-read shadow filter, 0.40 ms of the deck's direct pass (t2-gfq5).

One event: the disk was full from about 21:30 to 21:40 EST on 2026-11-11, and a server restart at about 22:10 cancelled two waiting takes. Every broken trace and the film ran again.

Not done: no coverage, no mutation testing and no ARCHITECTURE.md text for key and fill, because the prototype does not merge. Follow-ups: t2-gfq5 (the nine-read shadow filter), t2-vsqh (the corner pass in shorter fragments, which waits for the user's decision).

**2026-11-14T16:21:37Z**

Closing note (2026-11-14). The three stages of this ticket are done.

1. The research: docs/plans/light-pairs.md holds the study of the first attempt, the two designs and the prototypes, and docs/research/light-pairs/ holds the records.
2. The local shadow filter by driver (t2-gfq5), merged as 6e587063d.
3. Key and fill with the corner pass (t2-vsqh), merged as 72fa83533. It is the renderer's default, with 4 key lights. On the Apple M1 Max, the corner, tile and direct passes take 3.47 ms against 3.82 ms on the deck, and 3.48 ms against 3.83 ms in the hall of 64 lamps. On the RTX 2000 Ada, the hall's frame gains 0.33 ms and the deck is neutral. The key and fill tests pass on both machines: 50 of 50 on the card on main at 416e05ebd.

What stays open:

- The known bias (ARCHITECTURE.md, 'Key and fill', 'The known bias'): the fill is diffuse light only, so the specular light of the fill lamps is missing. The hall is 2.47 % darker, the deck 0.31 % and the cabin 0.08 %. Ticket t2-kibe holds a cheap specular fill, which is not built.
- The register step of Apple's GPU: the key and fill direct shader sits at register 115 with no room, and a merge of main moved it to 116 once and took the whole gain on the Mac. Ticket t2-x53j holds the guard and the room.
- Ticket t2-lcmo holds the 0.19 ms that the surface test costs on the deck on the Mac.
