# Rendering performance

Ticket: `ter-pq5y`, the last member of the rendering upgrade epic ([rendering-upgrade.md](rendering-upgrade.md)). The user made it the coda of the epic on 2026-10-25.

## Goal

Hold the whole frame to a fixed budget of GPU time, once every feature of the epic is in. Profile each pass to learn why it takes its time, and cut each pass that is over its share.

## Terms

- GPU span of a frame: the time from the start of the frame's first GPU pass to the end of its last GPU pass, in milliseconds. It includes the barriers and the gaps between passes. The renderer's stamps each wait for all commands before them, so its pass times include those too, and they add up to the span (ARCHITECTURE.md, "GPU timings"). A Metal System Trace times each pass's encoders on their own, so its pass times leave the gaps out.
- GPU time per frame: the mean GPU span over the frames of a run.
- Share: the part of the budget that one pass can take. Part 3 sets the shares.
- Quiet machine: no other build, test or GPU run during the measurement. The report gives the one-minute load at the start of each run.
- Limiter: the GPU unit that bounds a pass, such as memory bandwidth, the ALUs or the occupancy. A Metal System Trace reports it per pass.

## Decisions

### Made by the user

1. The budget is 12 ms of GPU time per frame. The measurement is `scripts/demo-exit.json` at 1920 × 1080, in a Release build, on the development Mac (Apple M1 Max). 12 ms gives 60 frames per second with 4.7 ms of headroom for scenes heavier than demo-exit. (User, 2026-10-25)
2. A pass over its share first gets optimizations that keep the image the same. If those are not enough, its default drops quality, such as half-resolution reflections or fewer rays, and the full quality stays a setting. (User, 2026-10-25)
3. The coda measures the frame and cuts it once. No check runs before each merge. Later tickets report their own cost, as the recipe of [rendering-upgrade.md](rendering-upgrade.md) says. (User, 2026-10-25)

### Settled by this design

4. The budget applies to the GPU span, not to the sum of the pass times. The frame rate depends on the span. On a GPU that overlaps passes, the span is shorter than the sum, and on a GPU that drains between passes, it is longer.
5. Each feature's profile starts as soon as its feature merges and the tools of Part 1 merge. It brings the feature to the cost that its design expected. Part 3 then works on the whole frame. If a profile waits for the coda, later features build on a slow pass. For example, bounce light's far rays reuse the reflections' trace.
6. The budget is measured on the Mac, but the user plays on a Linux PC (CLAUDE.md, Design preferences). An optimization must cut memory traffic, work or passes, which helps on every GPU. A change that helps only Apple's tile-based GPU needs a reason that holds on NVIDIA, AMD and Intel GPUs. The report gives that reason. Workgroup sizes stay multiples of 64, so that they fill both 32-wide and 64-wide waves.
7. An optimization that keeps the image the same is checked by a pixel comparison of the captures before and after it, not by eye alone. The snapshot tests have no reference images yet. So the comparison uses the demo captures that each feature reviewed.

## The tools

- The GPU timestamps of each pass exist (ARCHITECTURE.md, "GPU timings"). A headless run writes their summary to `gpu-timings.json`.
- Part 1 adds the frame's GPU span to that file.
- Part 1 adds a script that records a Metal System Trace of a headless run. It records with `xcrun xctrace record --template 'Metal System Trace'` and exports with `xcrun xctrace export`. Then it reports each pass's time and limiter. Xcode's xctrace 27.0 is on the development Mac.

## Order of the work

| Part | Ticket | Runs | Waits for |
|---|---|---|---|
| 1. The profiling tools | `ter-nydu` | At once | Nothing |
| 2a. The occlusion passes | `ter-izsu` | In the bounce light's chain, before its defaults | Bounce light's acceptance tests (`ter-y1s7`) and Part 1 |
| 2b. The reflection passes | `ter-ff6y` | When the reflections merge | `ter-jxv9` and Part 1 |
| 2c. The environment map's prefilter | `ter-of1p` | When the environment map merges | `ter-nl5u` and Part 1 |
| 3. The whole frame | `ter-3g1g` | Last | Every member of the epic, Parts 1 and 2 |

### Part 1: the profiling tools

- The span: `GpuTimer` reads the first and the last timestamp of each frame. `gpu-timings.json` gains an entry `frame` with the mean, median and p95 of the span, beside the passes. The printed lines gain one for the frame.
- The trace: a script in `tools/` runs a headless script under xctrace and exports the GPU intervals and the counters. It writes a table of each pass's time and limiter. The script finds out which data xctrace can export headless. If the limiters cannot be exported, the report says so and the script reports the GPU intervals only.
- Done: the span's tests pass in Checked and Release, `tools/README.md` describes the script, and one trace of demo-exit is read with it. The report gives that trace's table.

### Part 2: the profiles of the three features

Each profile follows the same steps:

1. Trace the feature's passes on a quiet machine, with the tools of Part 1.
2. Name the limiter of each pass, and compare the pass's memory traffic and work with its design's estimate.
3. Make the changes that the profile justifies, each one measured on its own before the next one.
4. Measure the cost again on a quiet machine, two runs, and write the numbers into the feature's section of ARCHITECTURE.md.

The feature's design gives the target: 1 to 3 ms for the reflections' trace ([screen-space-reflections.md](screen-space-reflections.md)), 0.1 to 0.2 ms for the prefilter ([environment-map.md](environment-map.md), "Cost"), and the occlusion passes as `ter-izsu` says.

### Part 3: the whole frame

1. Measure demo-exit at 1080p on a quiet machine, two runs, with the GPU span and a trace.
2. If the GPU time per frame is at most 12 ms, write the table of each pass's time into ARCHITECTURE.md and close.
3. Otherwise, give each pass a share: its measured time, scaled so that the shares add to 12 ms less the gaps between passes. Rank the passes by how far each one is over its share.
4. Optimize the largest first, with changes that keep the image (decision 2). Measure after each change.
5. If the frame is still over 12 ms, lower one feature's default. Choose the feature that saves the most time for the least change in the image. Show the user captures of the old and the new default before the change merges. Keep the old default as a setting.
6. Write the budget table into ARCHITECTURE.md: each pass's share and its measured time.

## Out of scope

- A check of the budget before each merge (decision 3).
- Dynamic resolution and quality presets. A preset waits until a user needs one.
- The CPU side of the frame. The demo's CPU takes about 2.2 ms per frame ([blue-danube-demo.md](blue-danube-demo.md)), well under the GPU's time.
