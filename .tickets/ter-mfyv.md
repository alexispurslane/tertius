---
id: ter-mfyv
status: open
deps: []
links: []
created: 2026-11-01T02:14:19Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Direct light within its share of the 12 ms frame: 6.01 ms to 3.80 ms on demo-exit

Part 3 of docs/plans/rendering-performance.md, split out of ter-3g1g by the orchestrator on 2026-10-31, so that the largest groups of passes are cut in parallel instead of one after another. The measurement (ter-3g1g, note of 2026-10-31): demo-exit at 1920 x 1080, Release, main 87ee9caf, Apple M1 Max, load 2 to 3. Two runs without a trace gave a mean GPU span of 18.04 and 17.94 ms; a trace of every frame without counters gave 17.56 ms. Each pass's share is its own busy time per frame in that trace (the mean over all 3,889 frames), times (12 ms - 1.87 ms of gaps between passes) / 16.03 ms = 0.632.

The direct light pass takes 6.009 ms of the frame's 17.56 ms in the trace, 34 % of the frame, and its share is 3.798 ms, 2.21 ms less. It is the largest pass by far. History: ter-p1u7 (a per-pixel bound on the lights, dropped by the user because it was slower), ter-ox1a (a light's reach from the last frame's exposure), ter-uqmn (the shading models' cost); the profile of main in ter-p1u7 found about 3.6 ms of demo-exit's lighting in the lamps' reach (every lamp at every pixel) and about 1.3 ms in the lamps' shadows, limited by PCF sample rate. ARCHITECTURE.md, 'The scenes and the cost of the shadows', has the halls that isolate the light loop.

## Acceptance Criteria

The direct light pass's own busy time on demo-exit (a trace of every frame, two runs or a paired trace) is at most 3.80 ms, or the changes that keep the image are exhausted and a note gives the remainder and each candidate default change with its measured saving and captures. Every change keeps the image by the pixel comparison of decision 7. ARCHITECTURE.md gives the new cost.

## Notes

**2026-11-01T03:59:33Z**

Profile of the direct light pass (builder, 2026-10-31), step 1 of the brief. Base ef3b6ae5, Release, 1920 x 1080, Apple M1 Max, tools/gpu-trace.sh, 120 frames per trace, each under the GPU lock. Means of the trace's own busy time in ms. Traces in /tmp/mfyv/traces (not kept).

Limiter, from traces with counters (deck from frame 399, load 5.8 to 4.6; chute-top from frame 1139, load 3.5 to 4.4):
- deck: 5.405 ms. Texture Sample limiter 53 % (utilization 45 %), Texture Filtering 45 %, ALU 34 % (F32 utilization 14 %), Texture Cache 9 %, Buffer Read 3 %, Fragment Occupancy 13 %, reads 1.5 GB/s and writes 1.3 GB/s, about 8 MB and 7 MB per frame.
- chute-top: 5.778 ms. Texture Sample 47 %, Texture Filtering 42 %, ALU 38 %, Fragment Occupancy 13 %, reads 0.9 GB/s.
- A third trace with counters (floor, frame 2412) and a fourth (a probe) hung in xctrace after the demo exited (exit 124 after 480 s), so the limiters come from two films.
So the pass is bound by texture sampling and its latency, not by memory: it moves about 15 MB per frame, a G-buffer read and two writes. The design's estimate was 16 PCF taps per shadowed light where it shines (docs/plans/local-light-shadows.md, "Cost"), and those taps are the largest sampling work: 10 shadowed lamps of 16 taps each, against one cascade lookup of 16 to 32 taps for the sun and about 8 G-buffer fetches.

Where the time goes, from probes on the deck window without counters (load 2 to 8; base trace 5.365 ms):
- The lamps' tile taps: a probe whose tileShadow returns 1 takes 3.851 ms, so about 1.5 ms. That probe also changes the image (light through walls), so the share is approximate.
- The hair loops: directLight without the fiber loops takes 5.300 ms, no measurable change. The shader's size does not limit the pass.
- The light list in a uniform buffer (constant address space on Metal) instead of a storage buffer: the run's timer gave 4.84 ms against 4.75 ms on the base (the trace's export hung). The buffer's address space does not limit the pass.
- A light list per tile or cluster by reach: a probe counted, for each subgroup of pixels, the lights of the list whose reach holds a pixel of the subgroup, in the last of 60 film frames of each of the nine films (the count went through the HDR image's alpha, so the exposure stayed as shipped). In all nine films, 99 to 100 % of the pixels lie in subgroups that every light of the list but at most one reaches (unbelt: 12 to 13 of its 15.5 lights). At the exposure of a film, a lamp's reach is 20 to 45 m or more, larger than the station's rooms. So a list by reach culls nothing on demo-exit, and none is built.
- Probes that removed the sun's cascade lookup ran at 1.25 to 2.07 ms, but they are not valid: they let the sunlight into the station, so the automatic exposure fell and the lamps' reach shrank, and fewer lamps stayed in the loop. A probe measures a cost only when it keeps the exposure.
- A bounds test before the 16 taps: a probe that returns 1 or 0 when two plain gathers of the tile's depth all pass or all fail the comparison, and else takes the 16 taps, ran at 4.711 ms (its untouched passes 4 to 7 % slower than the base trace's), so about 0.65 to 0.9 ms less. A probe that decides only deep umbra (5 % behind the caster) saves 0.15 ms, so most of the saving is lit pixels.

**2026-11-01T13:15:13Z**

Change 1, the tile bounds (builder, 2026-11-01). ShadowAtlas gets a bounds image: RG32F, half the tile's side, one layer per tile, 32 MB at the default atlas. Texel k of a layer holds the nearest and the farthest reversed depth of the tile's texels 2k to 2k + 5 on each axis, so it holds the 5 x 5 texels that the taps of tileShadow compare. shadow_bounds.comp writes the bounds of each tile that the local shadow pass draws, in the same pass. tileShadow fetches one bounds texel first and returns 0 or 1 without its 16 taps when every texel of the window fails or passes the comparison; it takes the taps within 1/256 of a texel's edge, where the hardware's rounding of a tap can reach past the window. The result of the early-out is the taps' own result, so the method is the same.

Image: the 15 comparison captures (exit-films, nine films of demo-exit, and the review scenes lamp-hall-16, lamp-hall-16-spot, lamp-hall-16-hair, lamp-hall-64, pillar-room, spot-box, at 1920 x 1080, captures/mfyv/c3-bounds against captures/mfyv/base-a): 13 byte-identical, chute 1 pixel at 1 level, lamp-hall-16-hair 19 pixels (0.0009 %) at 1 level. Two base runs are byte-identical, so the base is deterministic.

Cost, the deck window (gpu-trace.sh --no-counters --start 399 --frames 120, the trace's own busy time, mean ms per frame):
| Run | Load at start | Direct light | Local shadows | Frame span |
|---|---|---|---|---|
| base 1 | 3.6 | 5.365 | 0.298 | 17.824 |
| base 2 | 13.5 | 5.248 | 0.296 | 17.316 |
| bounds 1 | 23.0 | 4.907 | 0.636 | 17.951 |
| bounds 2 | 11.2 | 4.745 | 0.427 | 17.062 |
The direct light pass loses 0.34 to 0.62 ms, and the local shadow pass gains 0.13 to 0.34 ms for the bounds dispatches and their barrier; the first bounds run ran at load 23. A full trace of the bounds failed on the full disk (ENOSPC); the final measurement covers it.
