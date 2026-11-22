---
id: t4f-71qp
status: open
deps: []
links: []
created: 2026-11-22T07:58:43Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# The region's drift test restarts its count at every change of the render size: measure how often the windowed game's scale changes

The bounce light's region test (t0-acxm, docs/plans/bounce-light-confidence.md section 8.7) needs 36 valid draws in a row at one render size. A change of the render size restarts the count, because it moves the scene points under the taps and changes the anchors that the dispatch covers (regionContinues in region_drift.glsl). Headless runs keep a fixed scale of 2/3, so no take or test sees the windowed game. Under Fsr3Upscaling(FrameTimeTarget.Default), RenderScaleController moves the scale by 0.25 or 0.05 of the gap to its wanted value after each measured frame, unless the frame lies within 3 % of the budget. So the scale can change in most draws, and the region test then never runs. Also, the window of 18 draws matches the jitter's cycle only at the scale 2/3 (8 / scale² phases). To do: log the render size of each draw in a windowed run of the deck and of the bay, count the runs of 36 draws at one size, and decide whether the region test needs a rule that survives a scale step (for example a count that keeps going while the scale moves by less than one block), or whether the controller should hold its scale longer.


## Notes

**2026-11-22T14:32:28Z**

Measured by the acceptance takes of Design X (t0-5tyj, 2026-11-22, Mac, X = the line's head 57b4045ae). Record: docs/research/design-x-acceptance-2026-11-22/README.md, "The remedy under the dynamic render scale". Chart: docs/research/design-x-acceptance-2026-11-22/pics/dynamic-scale-cabin.png.

Method: a scratch patch outside src (docs/research/design-x-acceptance-2026-11-22/scratch-dynamic-scale.patch, after the survey's probe patch and the region log patch). TERTIUS_X_FRAME_TIME_TARGET=<ms> gives the headless renderer FrameTimeTarget(<ms>, 0.5, 1) in place of the fixed 2/3, and the controller then measures each frame's GPU timings as in the window. TERTIUS_X_SIZE_LOG logs each draw's render size. The survey's switch cabin ran at 1920 x 1080 at a load of 7 to 9 over one minute.

Switch film, lamps off at draw 193, on at draw 393:
- Fixed scale 2/3 (control, the patch inactive): 0 size changes, test on 99.2 % of the anchor-draws, fires 53.7 % / 50.3 % in the 100 draws after off / on (part B's numbers exactly). Slowest region 77 / 51 draws.
- Dynamic, the game's default target 16.67 ms: the scale moves from 0.50 to 0.58 and back in a cycle of about 60 draws. 71 size changes in draws 36 to 192, 40 in the 100 draws after off and 43 in the 100 after on. The longest run at one size is 25 to 31 draws, and no run reaches 36, so the test never runs (0 % of anchor-draws tested). Slowest region: not settled within 200 draws after off (r10), 98 after on. Corners r00 / r02: 115 / 98 draws (fixed X 77 / 50, M 143 / 92, X0 not settled).
- Dynamic, target 8 ms: the scale stays at its lowest value 0.50 in every draw, so the size never changes and the test runs (26.5 % of anchor-draws tested, fires 55.2 % / 49.4 %). Slowest region 81 / 51 draws, as at the fixed scale.

Reading: on this Mac the game's default target keeps the scale moving, so the remedy is off in the windowed game, and X settles between main and X0 after the lamps go off, and like main after they come on. The jitter's phase count also follows the render width (Fsr3Upscaler.JitterPhaseCount: 8 times the square of output over render width), so the 18-draw window matches the jitter cycle only at 2/3.

**2026-11-22T15:00:20Z**

Main under the same dynamic scale (t0-5tyj, 2026-11-22): main c4c1f1f2f with its own survey probe and the same scratch patch, target 16.67 ms. Scale 0.500 to 0.531. Size changes: 28 before the switch, 25 in the 100 draws after off and 9 in the 100 after on. Longest runs at one size: 37, 36 and 75 draws. Slowest region 188 after off and not within 100 after on. r00 113 / not settled, r10 185 / not settled, r12 188 / not settled.
A repeat of X's dynamic run: scale 0.501 to 0.561, 39 / 26 / 28 changes, longest runs 14 / 10 / 19, no anchor-draw tested. r00, r10 and r12 not within 200 draws after off; 88, 82 and 82 after on.
So without its region test, X settles slower than main after the lamps go off (the edge regions stay outside 5 % for all 200 draws, main 185 to 188), and faster after they come on (82 to 98 draws, main not within 100). The moving scale adds about 0.2 to 0.3 % of settled wiggle in r11 on both builds (standard deviation over draws 0.4 % fixed, 0.54 to 0.75 % dynamic). The width moves in steps of 8 px (median 8 up and 8 down, largest 16 up and 40 down), in 187 of 456 draws of X's switch run. The anchor ring is allocated for the largest render size (510 anchors at the dynamic target), so at the scale 0.50 only 135 of them (26.5 %) lie in the render area.
