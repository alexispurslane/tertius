---
id: ter-qx81
status: open
deps: []
links: []
created: 2026-11-12T12:37:47Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [perf, mac]
---
# Two lamps per pass of the direct-light loop: does a partial unroll help the Mac?

The user asked on 2026-11-12 whether a partial unroll of our shader loops helps the M1 Max reach its arithmetic peak. Loops with a constant count (the 4-step loops in lighting.glsl) are already unrolled by Apple's compiler (air-opt, ter-3ug8). The loops that stay rolled have a count from a uniform, such as the light loop over frame.counts.x in lighting.glsl (lines 773 and 886 on main at 5c83ef6c8).

Known: the calibration probe's rolled loop of one dependent FMA per pass reaches 41 % of the peak, 77 % at 2 steps per pass and 93 % at 4 (docs/research/temporal-renderer/gpu-calibration.md). But that body is tiny. The one real loop tried, the screen walk's step loop (ter-dzxf), got much slower when unrolled at cap 62: 2.53 ms rolled, 4.55 ms at 2 steps, 4.80 ms at 4 (docs/research/temporal-renderer/walk-hill-climb/attempts.md, cap62-unroll), most likely from register pressure. The tile agent also found that the direct-light pass costs more with its parts together than apart (4.19 ms against 0.75 + 1.78 ms removed), which points at register pressure.

Test: one exclusive GPU take (at most about 20 minutes) with three deck builds from main: rolled, 2 lamps per pass (with a remainder step), and 2 lamps per pass with the shadow lookup kept outside the pair. Frame span and direct-light pass time, same driver for all three. Byte-identical stills required. If a build gains above the noise, check the reflection march the same way, and check the RTX 2000 Ada through the Runpod queue (ter-94uo), because the change must not cost time on Linux drivers. If nothing gains, close with the numbers.

