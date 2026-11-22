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

**2026-11-22T15:09:21Z**

Decision of the user, 2026-11-22, on the remedy under a moving render scale, in the user's words: "I want videos of the walk and lamp switch under normal engine conditions — that is, dynamic render scaling — because that's always on (or should be). When we merge Design X, we should *not* hold render size, we should let it scale as before, and yes, we'll get main-like behavior, but we'll have a platform of better logic to work off of, to try to improve". The orchestrator's reading, which binds this line: (1) Design X merges with the render scale controller as it is. No hold of the render size, not before the merge and not as a planned fix after it. My design H (the held scale) is rejected. (2) The user accepts that Design X behaves about like main while the render size moves. (3) How the region's drift test and a moving render size fit together stays open as later work on top of the merged Design X, in t4f-71qp. No design and no build now. A test that survives a size change is one candidate for then, not a decision. (4) The merge itself still waits for the videos, the rest of the acceptance record and the combined check. The measured facts behind it are in t0-5tyj and in the acceptance record (docs/research/design-x-acceptance-2026-11-22/README.md, the section on the dynamic render scale): at the default target of 16.67 ms on this Mac the render width changed in 187 of 456 draws, the test never ran, and Design X settled about like main under the same moving scale.

**2026-11-22T21:37:23Z**

Evidence from take B of t0-3vwx, 2026-11-22: under the card's size log of a lamp switch, the region's test runs on only 11.7 % and 12.1 % of anchor-draws in the 100 draws after off and after on, because each size change restarts its count. Design X without the cap then settles slower than main after off (141 against 121 draws, whole image) where it was faster at a fixed scale (66 against 122). The combined check's lead's reading, not measured: before the wobble's fix each size change stretched the picture and gave the blocks motion evidence, which made the histories follow the light sooner (settle 125 / 36 before the fix, 141 / 45 after it); a log of the evidence level per block would test it. The cap of t0-zmjx removes the size changes after a switch on the card, so this ticket's case stays for sizes that move for another reason.

**2026-11-22T23:52:00Z**

From the final walk film of t0-3vwx, 2026-11-22: the region test's count also restarts under a moving camera. In the first 60 draws after a stop it runs on 34.7 % of the anchor-draws, then on 90.4 %. In that span Design X's trail fades more slowly than main's (center region +4.61 % against +3.73 % at 20 draws after the stop, slowest region's settle 47 draws against 25). The link between the two is my reading and is not measured.
