---
id: ter-ww0h
status: in_progress
deps: [ter-mfyv]
links: []
created: 2026-11-01T02:46:28Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Measure how much direct-light work goes to lamps that walls block, before any wall culling

The user asked on 2026-10-31 whether lamps that walls block can be culled before the direct light pass, and chose to measure first. Two conservative designs wait on this number: (A) rooms and doorways on the CPU, which drops a lamp whose light cannot enter a room that the camera sees, and (B) a per-tile shadow test on the GPU, which drops a shadowed lamp for each 16 x 16 pixel tile that its shadow map proves blocked. A ray from the lamp to the camera is not a valid test: it culls a lamp around a corner that lights a floor seen through a doorway, and a lamp inside its own shade.

Work, on main after ter-mfyv merges (its changes to the loop count): a temporary probe in the direct pass, kept out of the commits, counts on each film of demo-exit at 1920 x 1080: (1) for the lamps with shadow tiles, the lamp-pixel pairs that reach the shadow test, and the share of them whose visibility is 0; (2) the share of (16 x 16 tile, shadowed lamp) pairs in which every pixel that reaches the shadow test has visibility 0, which bounds what design B can drop; (3) the lamps with visibility 0 at every pixel of the view, which bounds what design A can drop; (4) the unshadowed lamps, which shine through walls today, so a wall cull of them would change the image. Give each count per film and its estimated share of the direct pass's time, from the per-lamp costs that the profile of ter-mfyv measures. Record the numbers and the recommendation as a note; build no culling.

## Acceptance Criteria

A ticket note gives the four counts per film of demo-exit and the estimated time each design could save, and the probe is not in the commits.

## Notes

**2026-11-01T15:49:35Z**

2026-11-01: workflow run cc31bfac-6ab6-4171-97a3-c9bc5552525b (version 869b0b07), branch ter-ww0h-wall-blocked-lamps from 612d6aa0 (main after ter-mfyv's merge). The brief asks first for the per-lamp costs on the current code, because ter-mfyv's tile bounds already skip the taps of fully blocked pixels.
