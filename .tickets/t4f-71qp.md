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

